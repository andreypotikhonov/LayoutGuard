namespace LayoutGuard.Core;

public sealed class FrequencyTable
{
    private readonly Dictionary<string, long> _counts = new(StringComparer.OrdinalIgnoreCase);

    public static FrequencyTable Load(string path)
    {
        var result = new FrequencyTable();
        if (!File.Exists(path)) return result;
        foreach (var line in File.ReadLines(path))
        {
            var split = line.LastIndexOf(' ');
            if (split <= 0 || !long.TryParse(line[(split + 1)..], out var count)) continue;
            result._counts[line[..split].Trim()] = count;
        }
        return result;
    }

    public long Get(string word) => _counts.GetValueOrDefault(word, 0);

    public IEnumerable<KeyValuePair<string, long>> Entries => _counts;
}
