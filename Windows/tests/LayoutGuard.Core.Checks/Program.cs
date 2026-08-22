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

Check(KeyboardLayoutConverter.Convert("ghbdtn", SupportedLanguage.Russian) == "привет", "layout ru");
Check(KeyboardLayoutConverter.Convert("[jnz", SupportedLanguage.Russian) == "хотя", "punctuation layout key");
Check(engine.Decide("ghbdtn", options)?.Replacement == "привет", "wrong layout");
Check(engine.EarlyLayoutDecision("ghbd", options)?.Replacement == "прив", "early layout ru");
Check(engine.EarlyLayoutDecision("рудд", options)?.Replacement == "hell", "early layout en");
Check(engine.EarlyLayoutDecision("hell", options) is null, "valid source prefix stays unchanged");
Check(engine.Decide("неограненный", options) is null, "valid rare word");
Check(engine.Decide("првиет", options)?.Replacement == "привет", "transposition");
Check(engine.Decide("ривет", options)?.Replacement == "привет", "missing п");
Check(engine.Decide("пивет", options)?.Replacement == "привет", "missing р");
Check(engine.Decide("првет", options)?.Replacement == "привет", "missing и");
Check(engine.Decide("ивет", options)?.Replacement == "привет", "missing пр");
Check(engine.Decide("вет", options)?.Replacement == "привет", "missing при");
Check(engine.Decide("сейчасскаким", options)?.Replacement == "сейчас с каким",
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
