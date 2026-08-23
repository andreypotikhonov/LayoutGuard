using LayoutGuard.Core;
using System.Text.RegularExpressions;

namespace LayoutGuard.Windows;

internal sealed class KeyboardMonitor : IDisposable
{
    private readonly CorrectionEngine _engine;
    private readonly Func<AppSettings> _settings;
    private readonly Action<CorrectionDecision, bool?> _onCorrection;
    private readonly SecureFieldDetector _secureFields = new();
    private readonly Func<AppSettings, bool> _securityEvaluator;
    private readonly LowLevelInputHook _hook;
    private readonly System.Threading.Timer _securityTimer;
    private string _currentWord = string.Empty;
    private string _sentencePrefix = string.Empty;
    private int _securityRefreshActive;
    private volatile CorrectionOptions _correctionOptions;
    private volatile bool _shouldPause = true;

    public KeyboardMonitor(
        CorrectionEngine engine,
        Func<AppSettings> settings,
        Action<CorrectionDecision, bool?> onCorrection,
        Func<AppSettings, bool>? securityEvaluator = null)
    {
        _engine = engine;
        _settings = settings;
        _onCorrection = onCorrection;
        _securityEvaluator = securityEvaluator ?? _secureFields.ShouldPause;
        _hook = new LowLevelInputHook(HandleKey, ResetContext);
        var initialSettings = settings();
        _correctionOptions = initialSettings.ToCorrectionOptions();
        if (securityEvaluator is not null) _shouldPause = securityEvaluator(initialSettings);
        _securityTimer = new System.Threading.Timer(
            _ => RefreshSecurityState(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
    }

    public void Start() => _hook.Start();

    internal bool HandleKey(KeyStroke stroke)
    {
        if (stroke.Injected) return false;
        if (ModifierDown(0x11) || ModifierDown(0x12) || ModifierDown(0x5B) || ModifierDown(0x5C))
        {
            ResetContext();
            return false;
        }
        var settings = _settings();
        if (!settings.Enabled || _shouldPause)
        {
            ResetContext();
            return false;
        }

        if (stroke.VirtualKey is 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C) return false;
        if (stroke.VirtualKey == 0x08)
        {
            if (_currentWord.Length > 0) _currentWord = _currentWord[..^1];
            else if (_sentencePrefix.Length > 0) _sentencePrefix = _sentencePrefix[..^1];
            return false;
        }

        var text = stroke.Text;
        if (string.IsNullOrEmpty(text))
        {
            if (stroke.VirtualKey is 0x25 or 0x26 or 0x27 or 0x28 or 0x2E) ResetContext();
            return false;
        }

        var wordCharacter = text.All(character => char.IsLetter(character) || character is '-' or '\'');
        var ambiguousLeadingKey = _currentWord.Length == 0 && text.Length == 1 && "[];,.'".Contains(text);
        if (wordCharacter || ambiguousLeadingKey)
        {
            if (text.All(char.IsLetter) &&
                KeyboardLayoutConverter.NeedsWordBoundary(_currentWord, text))
            {
                if (!TextInjector.ReplacePreviousText(0, " " + text))
                {
                    ResetContext();
                    return false;
                }
                _sentencePrefix += _currentWord + " ";
                _currentWord = text;
                return true;
            }

            _currentWord += text;
            if (_currentWord.Length > 64) _currentWord = string.Empty;
            if (_currentWord.Length >= 4)
            {
                var liveOptions = _correctionOptions;
                var live = _engine.EarlyLayoutDecision(_currentWord, liveOptions);
                if (live is not null)
                {
                    var livePhrase = _engine.PlanTrailingLayoutCorrection(
                        _sentencePrefix,
                        live.Language,
                        liveOptions);
                    var deliveredLength = Math.Max(0, _currentWord.Length - text.Length);
                    var prefixLength = livePhrase?.Original.Length ?? 0;
                    var injected = TextInjector.ReplacePreviousText(
                        prefixLength + deliveredLength,
                        (livePhrase?.Replacement ?? string.Empty) + live.Replacement);
                    if (!injected)
                    {
                        ResetContext();
                        return false;
                    }
                    var switched = InputLanguageSwitcher.Select(live.Language);
                    if (!switched)
                    {
                        // Never leave a half-corrected word behind. The current
                        // physical key is going to be swallowed, so restore the
                        // complete source prefix (including that key) as Unicode.
                        TextInjector.ReplacePreviousText(
                            (livePhrase?.Replacement.Length ?? 0) + live.Replacement.Length,
                            (livePhrase?.Original ?? string.Empty) + _currentWord);
                        ResetContext();
                        return true;
                    }
                    if (livePhrase is not null)
                    {
                        _sentencePrefix = _sentencePrefix[..livePhrase.Start] + livePhrase.Replacement;
                    }
                    _currentWord = live.Replacement;
                    _onCorrection(live, switched);
                    return true;
                }
            }
            return false;
        }

        if (_currentWord.Length == 0)
        {
            AppendToSentence(text);
            return false;
        }

        var word = _currentWord;
        _currentWord = string.Empty;
        var options = _correctionOptions;
        var decision = _engine.Decide(word, options, BuildCorrectionContext(word));
        if (decision is null)
        {
            AppendToSentence(word + text);
            return false;
        }

        var boundaryPhrase = decision.Reason == CorrectionReason.WrongLayout
            ? _engine.PlanTrailingLayoutCorrection(_sentencePrefix, decision.Language, options)
            : null;
        var injectedAtBoundary = TextInjector.ReplacePreviousText(
            word.Length + (boundaryPhrase?.Original.Length ?? 0),
            (boundaryPhrase?.Replacement ?? string.Empty) + decision.Replacement + text);
        if (!injectedAtBoundary)
        {
            AppendToSentence(word);
            return false;
        }
        bool? switchedLayout = decision.Reason == CorrectionReason.WrongLayout
            ? InputLanguageSwitcher.Select(decision.Language)
            : null;
        if (boundaryPhrase is not null)
        {
            _sentencePrefix = _sentencePrefix[..boundaryPhrase.Start] + boundaryPhrase.Replacement;
        }
        _onCorrection(decision, switchedLayout);
        AppendToSentence(decision.Replacement + text);
        return true;
    }

    private void AppendToSentence(string text)
    {
        if (text.Any(character => character is '.' or '!' or '?' or '\r' or '\n'))
        {
            _sentencePrefix = string.Empty;
            return;
        }
        _sentencePrefix += text;
        if (_sentencePrefix.Length > 512) _sentencePrefix = string.Empty;
    }

    internal CorrectionContext BuildCorrectionContext(string currentWord)
    {
        var tokens = Regex.Matches(_sentencePrefix, @"[\p{L}]+(?:[-'][\p{L}]+)*")
            .Select(match => match.Value.ToLowerInvariant())
            .Where(token => KeyboardLayoutConverter.DetectLanguage(token) == SupportedLanguage.Russian)
            .TakeLast(2)
            .ToArray();
        return new CorrectionContext(
            PreviousToken1: tokens.Length > 0 ? tokens[^1] : null,
            PreviousToken2: tokens.Length > 1 ? tokens[^2] : null,
            SentenceBoundary: string.IsNullOrWhiteSpace(_sentencePrefix),
            OriginalWasCapitalized: currentWord.Length > 0 && char.IsUpper(currentWord[0]));
    }

    private void ResetContext()
    {
        _currentWord = string.Empty;
        _sentencePrefix = string.Empty;
    }

    private static bool ModifierDown(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void RefreshSecurityState()
    {
        if (Interlocked.Exchange(ref _securityRefreshActive, 1) != 0) return;
        try
        {
            var settings = _settings();
            _correctionOptions = settings.ToCorrectionOptions();
            _shouldPause = _securityEvaluator(settings);
        }
        catch
        {
            _shouldPause = true;
        }
        finally
        {
            Volatile.Write(ref _securityRefreshActive, 0);
        }
    }

    public void Dispose()
    {
        _securityTimer.Dispose();
        _hook.Dispose();
    }
}
