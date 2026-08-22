using LayoutGuard.Core;

namespace LayoutGuard.Windows;

public sealed class AppSettings
{
    public const int CurrentSettingsVersion = 2;
    public int SettingsVersion { get; set; } = CurrentSettingsVersion;
    public bool Enabled { get; set; } = true;
    public bool CorrectTypos { get; set; } = false;
    public bool CorrectMissingSpaces { get; set; } = false;
    public bool RestoreBrokenKeys { get; set; } = true;
    public int MaximumMissingLetters { get; set; } = 3;
    public string BrokenRussianLetters { get; set; } = "при";
    // These are the same physical keys as Russian “при” on a US layout.
    public string BrokenEnglishLetters { get; set; } = "ghb";
    public bool StartWithWindows { get; set; }
    public List<string> CustomWords { get; set; } = [];
    public List<string> ExcludedProcesses { get; set; } =
        ["1Password", "Bitwarden", "KeePass", "KeePassXC", "CredentialUIBroker"];

    public CorrectionOptions ToCorrectionOptions() => new()
    {
        CorrectTypos = CorrectTypos,
        CorrectMissingSpaces = CorrectMissingSpaces,
        RestoreBrokenKeys = RestoreBrokenKeys,
        MaximumMissingLetters = Math.Clamp(MaximumMissingLetters, 1, 3),
        BrokenRussianLetters = BrokenRussianLetters.ToLowerInvariant().ToHashSet(),
        BrokenEnglishLetters = BrokenEnglishLetters.ToLowerInvariant().ToHashSet(),
        CustomWords = CustomWords.Select(word => word.Trim().ToLowerInvariant())
            .Where(word => word.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
    };
}
