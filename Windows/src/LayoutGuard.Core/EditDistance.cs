namespace LayoutGuard.Core;

public static class EditDistance
{
    public static int DamerauLevenshtein(string source, string target)
    {
        var distances = new int[source.Length + 1, target.Length + 1];
        for (var i = 0; i <= source.Length; i++) distances[i, 0] = i;
        for (var j = 0; j <= target.Length; j++) distances[0, j] = j;

        for (var i = 1; i <= source.Length; i++)
        {
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                {
                    distances[i, j] = Math.Min(distances[i, j], distances[i - 2, j - 2] + 1);
                }
            }
        }
        return distances[source.Length, target.Length];
    }
}

