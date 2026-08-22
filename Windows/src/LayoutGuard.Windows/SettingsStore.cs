using System.Text.Json;

namespace LayoutGuard.Windows;

internal static class SettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LayoutGuard");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                using var document = JsonDocument.Parse(json);
                var version = document.RootElement.TryGetProperty(nameof(AppSettings.SettingsVersion), out var value)
                    ? value.GetInt32()
                    : 0;
                if (version < AppSettings.CurrentSettingsVersion)
                {
                    // Earlier builds enabled speculative word segmentation
                    // and general spelling replacement, which could turn an
                    // unknown word into unrelated dictionary fragments/words.
                    settings.CorrectTypos = false;
                    settings.CorrectMissingSpaces = false;
                    settings.SettingsVersion = AppSettings.CurrentSettingsVersion;
                    Save(settings);
                }
                return settings;
            }
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
