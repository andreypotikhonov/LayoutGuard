using LayoutGuard.Core;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../src/LayoutGuard.Windows/Resources"));
if (!Directory.Exists(root))
{
    root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src/LayoutGuard.Windows/Resources"));
}
if (!Directory.Exists(root))
{
    root = Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(),
        "Windows/src/LayoutGuard.Windows/Resources"));
}
var engine = new CorrectionEngine(root);
var options = new CorrectionOptions();
var typoOptions = new CorrectionOptions { CorrectTypos = true };
var missingSpaceOptions = new CorrectionOptions { CorrectMissingSpaces = true };
var certificateOptions = new CorrectionOptions
{
    CorrectTypos = false,
    CorrectMissingSpaces = true,
    RestoreBrokenKeys = true,
    MaximumMissingLetters = 3,
    BrokenRussianLetters = new HashSet<char>("рт")
};

Check(KeyboardLayoutConverter.Convert("ghbdtn", SupportedLanguage.Russian) == "привет", "layout ru");
Check(KeyboardLayoutConverter.Convert("[jnz", SupportedLanguage.Russian) == "хотя", "punctuation layout key");
Check(engine.Decide("ghbdtn", options)?.Replacement == "привет", "wrong layout");
Check(engine.EarlyLayoutDecision("ghbd", options)?.Replacement == "прив", "early layout ru");
Check(engine.EarlyLayoutDecision("рудд", options)?.Replacement == "hell", "early layout en");
Check(engine.EarlyLayoutDecision("hell", options) is null, "valid source prefix stays unchanged");
Check(engine.Decide("неограненный", options) is null, "valid rare word");
Check(engine.Decide("првиет", typoOptions)?.Replacement == "привет", "transposition");
Check(engine.Decide("ривет", options)?.Replacement == "привет", "missing п");
Check(engine.Decide("пивет", options)?.Replacement == "привет", "missing р");
Check(engine.Decide("првет", options) is null, "unconfigured missing и is not invented");
Check(engine.Decide("ивет", options) is null, "valid dictionary word is never rewritten");
Check(engine.Decide("кзамен", options)?.Replacement == "экзамен", "missing э at the start");
Check(engine.Decide("лектрон", options)?.Replacement == "электрон", "second missing э at the start");
Check(engine.Decide("кран", options) is null, "valid кран is not guessed as экран");
Check(engine.Decide("превет", options) is null, "ordinary typo is not rewritten by broken-key model");
Check(engine.Decide("сетифика", certificateOptions)?.Replacement == "сертификат",
    "two configured broken keys inside and at the end");
Check(engine.Decide("потести", missingSpaceOptions)?.Reason != CorrectionReason.MissingSpace,
    "unknown word is not split into по тест и");
Check(engine.EarlyLayoutDecision("рели", options) is null,
    "Russian prefix рели does not trigger an early layout switch");
Check(engine.EarlyLayoutDecision("релиз", options) is null,
    "Russian prefix релиз does not trigger an early layout switch");
Check(engine.Decide("релизь", options) is null,
    "unknown Russian word релизь stays unchanged by default");
Check(engine.Decide("сетифика", new CorrectionOptions
{
    CorrectTypos = false,
    CorrectMissingSpaces = true,
    RestoreBrokenKeys = false
})?.Reason != CorrectionReason.MissingSpace, "unknown word is not split into short syllables");
Check(engine.Decide("сейчасскаким", missingSpaceOptions)?.Replacement == "сейчас с каким",
    "missing spaces");
Check(KeyboardLayoutConverter.NeedsWordBoundary("xcode", "х"), "mixed script space");
var phrase = engine.PlanTrailingLayoutCorrection("lf z ", SupportedLanguage.Russian, options);
Check(phrase?.Replacement == "да я ", "whole wrong-layout phrase");
Check(engine.PlanTrailingLayoutCorrection("xcode ", SupportedLanguage.Russian, options) is null,
    "valid foreign word is phrase boundary");
Console.WriteLine("All core checks passed.");

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Check failed: {name}");
}
