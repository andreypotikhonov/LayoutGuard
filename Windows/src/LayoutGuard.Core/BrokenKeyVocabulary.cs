namespace LayoutGuard.Core;

internal sealed class BrokenKeyVocabulary
{
    private const ulong FnvPrime = 1099511628211;
    private const ulong FnvSeed1 = 14695981039346656037;
    private const ulong FnvSeed2 = 7809847782465536322;
    private readonly byte[] _bits;
    private readonly uint _bitCount;
    private readonly uint _hashCount;

    private BrokenKeyVocabulary(byte[] bits, uint bitCount, uint hashCount)
    {
        _bits = bits;
        _bitCount = bitCount;
        _hashCount = hashCount;
    }

    public static BrokenKeyVocabulary? Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (new string(reader.ReadChars(4)) != "LGBF") return null;
            if (reader.ReadUInt32() != 1) return null;
            var bitCount = reader.ReadUInt32();
            var hashCount = reader.ReadUInt32();
            if (bitCount == 0 || bitCount % 8 != 0 || hashCount == 0) return null;
            var bits = reader.ReadBytes(checked((int)(bitCount / 8)));
            return bits.Length == bitCount / 8
                ? new BrokenKeyVocabulary(bits, bitCount, hashCount)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public bool Contains(string word)
    {
        var first = Hash(word, FnvSeed1);
        var second = Hash(word, FnvSeed2) | 1;
        for (uint index = 0; index < _hashCount; index++)
        {
            var position = (first + index * second) % _bitCount;
            if ((_bits[checked((int)(position >> 3))] & (1 << (int)(position & 7))) == 0) return false;
        }
        return true;
    }

    private static ulong Hash(string word, ulong seed)
    {
        var value = seed;
        foreach (var character in word)
        {
            value ^= character;
            value *= FnvPrime;
        }
        return value;
    }
}
