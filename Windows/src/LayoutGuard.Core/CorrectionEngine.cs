using WeCantSpell.Hunspell;
using System.Text;
using System.Text.RegularExpressions;

namespace LayoutGuard.Core;

public sealed class CorrectionEngine
{
    private const long EarlyLayoutMinimumPopularity = 10_000;
    private const long EarlyLayoutPopularityRatio = 50;
    private readonly WordList _russian;
    private readonly WordList _english;
    private readonly FrequencyTable _russianFrequency;
    private readonly FrequencyTable _englishFrequency;
    private readonly Dictionary<string, Dictionary<string, (string Word, long Count)>> _brokenIndexes = [];
    private readonly object _indexLock = new();

    public CorrectionEngine(string resourceDirectory)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var dictionaries = Path.Combine(resourceDirectory, "Dictionaries");
        var frequencies = Path.Combine(resourceDirectory, "Frequencies");
        _russian = WordList.CreateFromFiles(Path.Combine(dictionaries, "ru_RU.dic"));
        _english = WordList.CreateFromFiles(Path.Combine(dictionaries, "en_US.dic"));
        _russianFrequency = FrequencyTable.Load(Path.Combine(frequencies, "ru_50k.txt"));
        _englishFrequency = FrequencyTable.Load(Path.Combine(frequencies, "en_50k.txt"));
    }

    public bool IsCorrect(string word, SupportedLanguage language, CorrectionOptions? options = null)
    {
        var normalized = word.ToLowerInvariant();
        if (options?.CustomWords.Contains(normalized) == true) return true;
        return Dictionary(language).Check(normalized);
    }

    public void WarmUp(CorrectionOptions options)
    {
        _ = BrokenIndex(SupportedLanguage.Russian, options);
        _ = BrokenIndex(SupportedLanguage.English, options);
    }

    public CorrectionDecision? Decide(string word, CorrectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length > 64) return null;
        var normalized = word.ToLowerInvariant();
        var language = KeyboardLayoutConverter.DetectLanguage(normalized);
        if (language is null) return null;
        var originalCorrect = IsCorrect(normalized, language.Value, options);

        if (!originalCorrect)
        {
            var target = language == SupportedLanguage.English
                ? SupportedLanguage.Russian
                : SupportedLanguage.English;
            var converted = KeyboardLayoutConverter.Convert(normalized, target);
            if (converted is not null && IsCorrect(converted, target, options))
            {
                return Decision(word, converted, target, CorrectionReason.WrongLayout, 100);
            }
            if (converted is not null && options.CorrectTypos)
            {
                var correctedConverted = BestTypoCandidate(converted, target, options);
                if (correctedConverted is not null)
                {
                    return Decision(word, correctedConverted.Value.Word, target,
                        CorrectionReason.WrongLayout, correctedConverted.Value.Score);
                }
            }
        }

        if (options.RestoreBrokenKeys)
        {
            var restored = BestBrokenKeyCandidate(normalized, language.Value, options, originalCorrect);
            if (restored is not null)
            {
                return Decision(word, restored.Value.Word, language.Value,
                    CorrectionReason.MissingBrokenKey, restored.Value.Score);
            }
        }

        if (!originalCorrect && options.CorrectMissingSpaces)
        {
            var spaced = BestMissingSpaceCandidate(normalized, language.Value, options);
            if (spaced is not null)
            {
                return Decision(word, spaced.Value.Word, language.Value,
                    CorrectionReason.MissingSpace, spaced.Value.Score);
            }
        }

        if (!originalCorrect && options.CorrectTypos)
        {
            var corrected = BestTypoCandidate(normalized, language.Value, options);
            if (corrected is not null)
            {
                return Decision(word, corrected.Value.Word, language.Value,
                    CorrectionReason.Typo, corrected.Value.Score);
            }
        }
        return null;
    }

    public CorrectionDecision? LayoutDecision(string word, CorrectionOptions options)
    {
        if (word.Length < 4) return null;
        var decision = Decide(word, new CorrectionOptions
        {
            CorrectTypos = false,
            CorrectMissingSpaces = false,
            RestoreBrokenKeys = false,
            BrokenRussianLetters = options.BrokenRussianLetters,
            BrokenEnglishLetters = options.BrokenEnglishLetters,
            CustomWords = options.CustomWords
        });
        return decision?.Reason == CorrectionReason.WrongLayout ? decision : null;
    }

    /// <summary>
    /// Detects an unambiguous wrong-layout prefix before the complete word has
    /// been typed. A conversion is only offered when the converted prefix is
    /// common and overwhelmingly more likely than the literal prefix.
    /// </summary>
    public CorrectionDecision? EarlyLayoutDecision(string word, CorrectionOptions options)
    {
        if (word.Length is < 4 or > 12) return null;
        var normalized = word.ToLowerInvariant();
        if (options.CustomWords.Any(custom => custom.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)))
            return null;
        var source = KeyboardLayoutConverter.DetectLanguage(normalized);
        if (source is null) return null;

        var target = source == SupportedLanguage.English
            ? SupportedLanguage.Russian
            : SupportedLanguage.English;
        var converted = KeyboardLayoutConverter.Convert(normalized, target);
        if (converted is null || KeyboardLayoutConverter.DetectLanguage(converted) != target)
            return null;

        var sourcePopularity = Frequency(source.Value).GetPrefixPopularity(normalized);
        var targetPopularity = Frequency(target).GetPrefixPopularity(converted);
        if (targetPopularity < EarlyLayoutMinimumPopularity ||
            targetPopularity < Math.Max(1, sourcePopularity) * EarlyLayoutPopularityRatio)
        {
            return null;
        }

        return Decision(word, converted, target, CorrectionReason.WrongLayout, targetPopularity);
    }

    private (string Word, double Score)? BestMissingSpaceCandidate(
        string input,
        SupportedLanguage language,
        CorrectionOptions options)
    {
        if (input.Length < 7 || input.Length > 40 || input.Any(character => !char.IsLetter(character)))
            return null;

        var allowedSingles = language == SupportedLanguage.Russian ? "вксуояаи" : "ai";
        var frequency = Frequency(language);
        var best = new (double Score, List<string> Words)?[input.Length + 1, 5];
        best[0, 0] = (0, []);
        for (var end = 1; end <= input.Length; end++)
        {
            for (var start = 0; start < end; start++)
            {
                var part = input[start..end];
                if (part.Length == 1 && !allowedSingles.Contains(part[0])) continue;
                if (!IsCorrect(part, language, options)) continue;
                var count = frequency.Get(part);
                if (part.Length > 1 && count < 50) continue;

                for (var wordCount = 0; wordCount < 4; wordCount++)
                {
                    if (best[start, wordCount] is null) continue;
                    var previous = best[start, wordCount]!.Value;
                    var score = previous.Score + Math.Log10(count + 10) * Math.Max(1, part.Length);
                    var words = new List<string>(previous.Words) { part };
                    if (best[end, wordCount + 1] is null ||
                        score > best[end, wordCount + 1]!.Value.Score)
                        best[end, wordCount + 1] = (score, words);
                }
            }
        }

        for (var wordCount = 2; wordCount <= 4; wordCount++)
        {
            var result = best[input.Length, wordCount];
            if (result is null || result.Value.Words.All(word => word.Length < 3)) continue;
            return (string.Join(' ', result.Value.Words), result.Value.Score);
        }
        return null;
    }

    /// <summary>
    /// Converts the longest trailing run of words that was typed in the same wrong
    /// keyboard layout as the current word. A valid word in another language (for
    /// example, "xcode") is a hard boundary, so unrelated text is never joined to
    /// the correction.
    /// </summary>
    public PhraseCorrection? PlanTrailingLayoutCorrection(
        string prefix,
        SupportedLanguage target,
        CorrectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return null;
        var matches = Regex.Matches(prefix, @"[\p{L}]+(?:[-'][\p{L}]+)*");
        if (matches.Count == 0) return null;

        var start = prefix.Length;
        var replacements = new Dictionary<int, string>();
        for (var index = matches.Count - 1; index >= 0; index--)
        {
            var match = matches[index];
            var separatorEnd = index == matches.Count - 1 ? prefix.Length : start;
            var separator = prefix[(match.Index + match.Length)..separatorEnd];
            if (separator.Any(character => character is '.' or '!' or '?' or '\r' or '\n')) break;

            var converted = KeyboardLayoutConverter.Convert(match.Value.ToLowerInvariant(), target);
            if (converted is null || !IsCorrect(converted, target, options)) break;
            replacements[index] = Decision(
                match.Value,
                converted,
                target,
                CorrectionReason.WrongLayout,
                100).Replacement;
            start = match.Index;
        }

        if (start == prefix.Length) return null;
        var original = prefix[start..];
        var replacement = original;
        foreach (var item in replacements.OrderByDescending(item => item.Key))
        {
            var match = matches[item.Key];
            var localStart = match.Index - start;
            replacement = replacement.Remove(localStart, match.Length)
                .Insert(localStart, item.Value);
        }
        return new PhraseCorrection(start, original, replacement);
    }

    private (string Word, double Score)? BestBrokenKeyCandidate(
        string input,
        SupportedLanguage language,
        CorrectionOptions options,
        bool originalCorrect)
    {
        if (input.Length < 2) return null;
        var broken = language == SupportedLanguage.Russian
            ? options.BrokenRussianLetters
            : options.BrokenEnglishLetters;
        if (broken.Count == 0) return null;

        var frequency = Frequency(language);
        var originalFrequency = frequency.Get(input);
        var index = BrokenIndex(language, options);
        var ranked = index.TryGetValue(input, out var indexed)
            ? new[] { indexed }
            : Array.Empty<(string Word, long Count)>();
        var valid = ranked
            .Where(candidate => IsCorrect(candidate.Word, language, options))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Word.Length)
            .ToList();
        if (valid.Count == 0) return null;

        var best = valid[0];
        if (originalCorrect && (best.Count < 5_000 || best.Count < Math.Max(1, originalFrequency) * 50))
        {
            return null;
        }
        var score = Math.Log10(best.Count + 1) * 20 + 50;
        return (best.Word, score);
    }

    private (string Word, double Score)? BestTypoCandidate(
        string input,
        SupportedLanguage language,
        CorrectionOptions options)
    {
        if (input.Length < 4 || input.Any(character => !char.IsLetter(character))) return null;
        var maxDistance = input.Length >= 8 ? 2 : 1;
        var frequency = Frequency(language);
        var candidates = Dictionary(language).Suggest(input)
            .Take(40)
            .Select((word, index) => new
            {
                Word = word.ToLowerInvariant(),
                Index = index,
                Distance = EditDistance.DamerauLevenshtein(input, word.ToLowerInvariant())
            })
            .Where(item => item.Distance <= maxDistance)
            .Where(item => PreservesEdgesOrAddsOne(input, item.Word))
            .OrderBy(item => item.Distance)
            .ThenByDescending(item => frequency.Get(item.Word))
            .ThenBy(item => item.Index)
            .FirstOrDefault();
        if (candidates is null) return null;
        return (candidates.Word, 40 - candidates.Distance * 10 + Math.Log10(frequency.Get(candidates.Word) + 1));
    }

    private static bool PreservesEdgesOrAddsOne(string input, string candidate)
    {
        if (input.Length == 0 || candidate.Length == 0) return false;
        if (input[0] == candidate[0] && input[^1] == candidate[^1]) return true;
        return candidate.Length == input.Length + 1 &&
            (candidate[1..] == input || candidate[..^1] == input);
    }

    private Dictionary<string, (string Word, long Count)> BrokenIndex(
        SupportedLanguage language,
        CorrectionOptions options)
    {
        var broken = language == SupportedLanguage.Russian
            ? options.BrokenRussianLetters
            : options.BrokenEnglishLetters;
        var key = $"{language}|{new string(broken.Order().ToArray())}|{options.MaximumMissingLetters}";
        lock (_indexLock)
        {
            if (_brokenIndexes.TryGetValue(key, out var existing)) return existing;
            var index = new Dictionary<string, (string Word, long Count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Frequency(language).Entries)
            {
                if (entry.Key.Length < 3 || entry.Key.Length > 40) continue;
                AddDeletionVariants(
                    entry.Key,
                    entry.Key,
                    entry.Value,
                    broken,
                    options.MaximumMissingLetters,
                    index
                );
            }
            _brokenIndexes[key] = index;
            return index;
        }
    }

    private static void AddDeletionVariants(
        string current,
        string original,
        long count,
        ISet<char> broken,
        int remaining,
        IDictionary<string, (string Word, long Count)> index,
        int start = 0)
    {
        if (remaining <= 0) return;
        for (var position = start; position < current.Length; position++)
        {
            if (!broken.Contains(current[position])) continue;
            var deleted = current.Remove(position, 1);
            if (deleted.Length >= 2 &&
                (!index.TryGetValue(deleted, out var existing) || count > existing.Count))
            {
                index[deleted] = (original, count);
            }
            AddDeletionVariants(deleted, original, count, broken, remaining - 1, index, position);
        }
    }

    private WordList Dictionary(SupportedLanguage language) =>
        language == SupportedLanguage.Russian ? _russian : _english;

    private FrequencyTable Frequency(SupportedLanguage language) =>
        language == SupportedLanguage.Russian ? _russianFrequency : _englishFrequency;

    private static CorrectionDecision Decision(
        string original,
        string replacement,
        SupportedLanguage language,
        CorrectionReason reason,
        double confidence)
    {
        if (original.All(char.IsUpper)) replacement = replacement.ToUpperInvariant();
        else if (char.IsUpper(original[0])) replacement = char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return new CorrectionDecision(original, replacement, language, reason, confidence);
    }
}
