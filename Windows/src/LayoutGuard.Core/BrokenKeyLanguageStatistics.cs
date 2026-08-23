using System.Text;

namespace LayoutGuard.Core;

internal sealed class BrokenKeyLanguageStatistics
{
    private const uint Version = 1;
    private readonly Entry[] _unigrams;
    private readonly Entry[] _bigrams;
    private readonly Entry[] _trigrams;

    private BrokenKeyLanguageStatistics(Entry[] unigrams, Entry[] bigrams, Entry[] trigrams)
    {
        _unigrams = unigrams;
        _bigrams = bigrams;
        _trigrams = trigrams;
    }

    public static BrokenKeyLanguageStatistics? Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (new string(reader.ReadChars(4)) != "LGST" || reader.ReadUInt32() != Version) return null;
            var counts = new[] { reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32() };
            if (counts.Any(count => count > 5_000_000)) return null;
            var sections = counts.Select(count => ReadEntries(reader, count)).ToArray();
            if (stream.Position != stream.Length) return null;
            return new BrokenKeyLanguageStatistics(sections[0], sections[1], sections[2]);
        }
        catch
        {
            return null;
        }
    }

    public long Unigram(string word) => Find(_unigrams, Hash(word));
    public long Bigram(string previous, string word) => Find(_bigrams, Hash(previous + "\0" + word));
    public long Trigram(string previous2, string previous1, string word) =>
        Find(_trigrams, Hash(previous2 + "\0" + previous1 + "\0" + word));

    private static Entry[] ReadEntries(BinaryReader reader, uint count)
    {
        var entries = new Entry[checked((int)count)];
        ulong previous = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var hash = reader.ReadUInt64();
            var value = reader.ReadUInt32();
            if (index > 0 && hash < previous) throw new InvalidDataException("statistics are not sorted");
            entries[index] = new Entry(hash, value);
            previous = hash;
        }
        return entries;
    }

    private static uint Find(Entry[] entries, ulong hash)
    {
        var low = 0;
        var high = entries.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var entry = entries[middle];
            if (entry.Hash == hash) return entry.Count;
            if (entry.Hash < hash) low = middle + 1;
            else high = middle - 1;
        }
        return 0;
    }

    private static ulong Hash(string text)
    {
        const ulong prime = 1099511628211;
        var value = 14695981039346656037UL;
        foreach (var item in Encoding.UTF8.GetBytes(text))
        {
            value ^= item;
            value *= prime;
        }
        return value;
    }

    private readonly record struct Entry(ulong Hash, uint Count);
}
