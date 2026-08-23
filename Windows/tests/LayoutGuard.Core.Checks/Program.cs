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
var packedLexicon = PackedLanguageLexicon.Load(Path.Combine(root, "Models", "ru_broken_lexicon.bin"));
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
Check(packedLexicon is { WordCount: > 3_000_000 }, "V2 surface-form lexicon loaded");
Check(packedLexicon?.Contains("андрей") == true, "name Андрей is in V2 lexicon");
Check(packedLexicon?.Contains("андрея") == true, "name form Андрея is in V2 lexicon");
Check(packedLexicon?.Contains("андреем") == true, "name form Андреем is in V2 lexicon");
var malformedLexicon = Path.Combine(Path.GetTempPath(), $"layoutguard-invalid-{Guid.NewGuid():N}.bin");
try
{
    File.WriteAllBytes(malformedLexicon, "not-a-layoutguard-resource"u8.ToArray());
    Check(PackedLanguageLexicon.Load(malformedLexicon) is null, "malformed V2 resource fails closed");
}
finally
{
    File.Delete(malformedLexicon);
}
Check(KeyboardLayoutConverter.Convert("[jnz", SupportedLanguage.Russian) == "хотя", "punctuation layout key");
Check(engine.Decide("ghbdtn", options)?.Replacement == "привет", "wrong layout");
Check(engine.EarlyLayoutDecision("ghbd", options)?.Replacement == "прив", "early layout ru");
Check(engine.EarlyLayoutDecision("рудд", options)?.Replacement == "hell", "early layout en");
Check(engine.EarlyLayoutDecision("hell", options) is null, "valid source prefix stays unchanged");
Check(engine.Decide("неограненный", options) is null, "valid rare word");
Check(engine.Decide("првиет", typoOptions)?.Replacement == "привет", "transposition");
Check(engine.Decide("ривет", options)?.Replacement == "привет", "missing п");
Check(engine.Decide("пивет", options)?.Replacement == "привет", "missing р");
Check(engine.Decide("ивет", options)?.Replacement == "привет", "missing п and р in привет");
Check(engine.Decide("Ивет", options)?.Replacement == "Привет", "capitalization after missing п and р");
Check(engine.Decide("првет", options) is null, "unconfigured missing и is not invented");
Check(engine.Decide("кзамен", options)?.Replacement == "экзамен", "missing э at the start");
Check(engine.Decide("лектрон", options)?.Replacement == "электрон", "second missing э at the start");
Check(engine.Decide("погамма", options)?.Replacement == "программа", "two missing р in программа");
Check(engine.Decide("огамма", options)?.Replacement == "программа", "missing п and two р in программа");
Check(engine.Decide("погамме", options)?.Replacement == "программе", "morphology: программе");
Check(engine.Decide("погаммой", options)?.Replacement == "программой", "morphology: программой");
Check(engine.Decide("погаммами", options)?.Replacement == "программами", "morphology: программами");
Check(engine.Decide("погаммного", options)?.Replacement == "программного", "morphology: программного");
Check(engine.Decide("дмитий", options)?.Replacement == "дмитрий", "name recovery: Дмитрий");
Check(engine.Decide("дмитию", options)?.Replacement == "дмитрию", "name morphology: Дмитрию");
Check(engine.Decide("андрей", options) is null, "valid name Андрей is preserved");
Check(engine.Decide("андрея", options) is null, "valid name form Андрея is preserved");
Check(engine.Decide("андей", options) is null, "real-word name collision Андей/Андрей is preserved");
Check(engine.Decide("евое", options)?.Replacement == "первое",
    "unigram ranker chooses the common word without context");
Check(engine.Decide("евое", options, new CorrectionContext(PreviousToken1: "в"))?.Replacement == "европе",
    "bigram context resolves в Европе");
var customOptions = new CorrectionOptions
{
    CustomWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "прэюксе", "моёкастом", "ивет" }
};
Check(engine.Decide("моёкастом", customOptions) is null, "custom observed word has absolute preservation priority");
Check(engine.Decide("ивет", customOptions) is null, "custom word disables the strict collision override");
Check(engine.Decide("юксе", customOptions)?.Replacement == "прэюксе",
    "custom word can be an exact broken-key candidate");
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

var propertyWords = new[]
{
    "привет", "экзамен", "электрон", "программа", "программе", "программами",
    "программного", "дмитрий", "дмитрию", "андрей", "андрея"
};
foreach (var expected in propertyWords)
{
    var broken = new HashSet<char>("прэ");
    var positions = expected
        .Select((character, index) => (character, index))
        .Where(item => broken.Contains(item.character))
        .Select(item => item.index)
        .Take(3)
        .ToArray();
    for (var count = 1; count <= positions.Length; count++)
    {
        var removed = positions.Take(count).ToHashSet();
        var observed = string.Concat(expected.Where((_, index) => !removed.Contains(index)));
        var generated = packedLexicon!.Generate(observed, broken, 3);
        Check(generated.Any(candidate => candidate.Word == expected),
            $"candidate completeness: {observed} contains {expected}");
        Check(generated.All(candidate => ExactBrokenChannel(candidate.Word, observed, broken)),
            $"candidate validity: {observed}");
    }
}

var torturePath = Path.GetFullPath(Path.Combine(root, "../../../../ml/data/ru_torture.tsv"));
if (!File.Exists(torturePath))
    torturePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Windows/ml/data/ru_torture.tsv"));
Check(File.Exists(torturePath), "human-readable torture suite exists");
foreach (var line in File.ReadLines(torturePath).Skip(1))
{
    var columns = line.Split('\t');
    if (columns.Length < 3) continue;
    var observed = columns[1];
    var expected = columns[2];
    var decision = engine.Decide(observed, options);
    Check(
        observed == expected ? decision is null : decision?.Replacement == expected,
        $"torture {columns[0]}: {observed} → {expected}");
}
Console.WriteLine("All core checks passed.");

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Check failed: {name}");
}

static bool ExactBrokenChannel(string candidate, string observed, ISet<char> broken)
{
    var observedIndex = 0;
    foreach (var character in candidate)
    {
        if (observedIndex < observed.Length && character == observed[observedIndex]) observedIndex++;
        else if (!broken.Contains(character)) return false;
    }
    return observedIndex == observed.Length;
}
