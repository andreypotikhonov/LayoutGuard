using System.Text;

namespace LayoutGuard.Core;

public static class KeyboardLayoutConverter
{
    private const string English = "qwertyuiop[]asdfghjkl;'zxcvbnm,.";
    private const string Russian = "йцукенгшщзхъфывапролджэячсмитьбю";
    private static readonly Dictionary<char, char> EnglishToRussian = BuildMap(English, Russian);
    private static readonly Dictionary<char, char> RussianToEnglish = BuildMap(Russian, English);

    public static string? Convert(string text, SupportedLanguage target)
    {
        var map = target == SupportedLanguage.Russian ? EnglishToRussian : RussianToEnglish;
        var result = new StringBuilder(text.Length);
        var changed = false;
        foreach (var character in text)
        {
            var lower = char.ToLowerInvariant(character);
            if (!map.TryGetValue(lower, out var replacement))
            {
                result.Append(character);
                continue;
            }

            changed = true;
            result.Append(char.IsUpper(character) ? char.ToUpperInvariant(replacement) : replacement);
        }
        return changed ? result.ToString() : null;
    }

    public static SupportedLanguage? DetectLanguage(string text)
    {
        var latin = 0;
        var cyrillic = 0;
        foreach (var character in text)
        {
            if (character is >= 'A' and <= 'z') latin++;
            else if (character is >= '\u0400' and <= '\u04ff') cyrillic++;
        }
        if (latin == 0 && cyrillic == 0) return null;
        return latin >= cyrillic ? SupportedLanguage.English : SupportedLanguage.Russian;
    }

    public static bool NeedsWordBoundary(string left, string right)
    {
        var leftLanguage = DetectLanguage(left);
        var rightLanguage = DetectLanguage(right);
        return leftLanguage is not null && rightLanguage is not null && leftLanguage != rightLanguage;
    }

    private static Dictionary<char, char> BuildMap(string source, string target) =>
        source.Zip(target).ToDictionary(pair => pair.First, pair => pair.Second);
}

