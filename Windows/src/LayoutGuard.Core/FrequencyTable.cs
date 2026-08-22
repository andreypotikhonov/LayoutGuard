namespace LayoutGuard.Core;

public sealed class FrequencyTable
{
    private const int MinimumIndexedPrefixLength = 3;
    private const int MaximumIndexedPrefixLength = 12;
    private readonly Dictionary<string, long> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _prefixCounts = new(StringComparer.OrdinalIgnoreCase);

    public static FrequencyTable Load(string path)
    {
        var result = new FrequencyTable();
        if (!File.Exists(path)) return result;
        foreach (var line in File.ReadLines(path))
        {
            var split = line.LastIndexOf(' ');
            if (split <= 0 || !long.TryParse(line[(split + 1)..], out var count)) continue;
            var word = line[..split].Trim();
            result._counts[word] = count;
            var maximumLength = Math.Min(word.Length, MaximumIndexedPrefixLength);
            for (var length = MinimumIndexedPrefixLength; length <= maximumLength; length++)
            {
                var prefix = word[..length];
                if (!result._prefixCounts.TryGetValue(prefix, out var existing) || count > existing)
                {
                    result._prefixCounts[prefix] = count;
                }
            }
        }
        return result;
    }

    public long Get(string word) => _counts.GetValueOrDefault(word, 0);

    public long GetPrefixPopularity(string prefix) =>
        prefix.Length is >= MinimumIndexedPrefixLength and <= MaximumIndexedPrefixLength
            ? _prefixCounts.GetValueOrDefault(prefix, 0)
            : 0;

    public IEnumerable<KeyValuePair<string, long>> Entries => _counts;
}
