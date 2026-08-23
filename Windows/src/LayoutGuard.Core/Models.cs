namespace LayoutGuard.Core;

public enum SupportedLanguage
{
    English,
    Russian
}

public enum CorrectionReason
{
    WrongLayout,
    Typo,
    MissingBrokenKey,
    MissingSpace
}

public sealed record CorrectionDecision(
    string Original,
    string Replacement,
    SupportedLanguage Language,
    CorrectionReason Reason,
    double Confidence = 1);

public sealed record PhraseCorrection(
    int Start,
    string Original,
    string Replacement);

public sealed record CorrectionContext(
    string? PreviousToken1 = null,
    string? PreviousToken2 = null,
    string? NextToken = null,
    bool SentenceBoundary = false,
    bool OriginalWasCapitalized = false);

public sealed class CorrectionOptions
{
    public bool CorrectTypos { get; init; } = false;
    public bool CorrectMissingSpaces { get; init; } = false;
    public bool RestoreBrokenKeys { get; init; } = true;
    public int MaximumMissingLetters { get; init; } = 3;
    public ISet<char> BrokenRussianLetters { get; init; } = new HashSet<char>("прэ");
    public ISet<char> BrokenEnglishLetters { get; init; } = new HashSet<char>("gh'");
    public ISet<string> CustomWords { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
