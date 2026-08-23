using System.Text;

namespace LayoutGuard.Core;

public enum LexiconWordClass : byte
{
    None = 0,
    Standard = 1,
    Name = 2,
    Colloquial = 3,
    Technical = 4,
    Custom = 5
}

public sealed record BrokenKeyCandidate(
    string Word,
    int MissingCount,
    LexiconWordClass WordClass);

/// <summary>
/// Compact minimal acyclic automaton containing exact Russian surface forms.
/// Candidate search permits only insertion of configured broken-key letters.
/// </summary>
public sealed class PackedLanguageLexicon
{
    private const uint Version = 2;
    private readonly State[] _states;
    private readonly Transition[] _transitions;
    private readonly uint _root;

    private PackedLanguageLexicon(State[] states, Transition[] transitions, uint root, uint wordCount)
    {
        _states = states;
        _transitions = transitions;
        _root = root;
        WordCount = wordCount;
    }

    public uint WordCount { get; }
    public int StateCount => _states.Length;
    public int TransitionCount => _transitions.Length;

    public static PackedLanguageLexicon? Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (new string(reader.ReadChars(4)) != "LGV2" || reader.ReadUInt32() != Version) return null;
            var stateCount = reader.ReadUInt32();
            var transitionCount = reader.ReadUInt32();
            var root = reader.ReadUInt32();
            var wordCount = reader.ReadUInt32();
            if (stateCount == 0 || stateCount > 10_000_000 || transitionCount > 50_000_000 || root >= stateCount)
                return null;

            var states = new State[checked((int)stateCount)];
            for (var index = 0; index < states.Length; index++)
            {
                var first = reader.ReadUInt32();
                var count = reader.ReadUInt16();
                var wordClass = (LexiconWordClass)reader.ReadByte();
                _ = reader.ReadByte();
                if ((ulong)first + count > transitionCount || wordClass > LexiconWordClass.Custom) return null;
                states[index] = new State(first, count, wordClass);
            }

            var transitions = new Transition[checked((int)transitionCount)];
            for (var index = 0; index < transitions.Length; index++)
            {
                var target = reader.ReadUInt32();
                var character = (char)reader.ReadUInt16();
                _ = reader.ReadUInt16();
                if (target >= stateCount || !char.IsLetter(character)) return null;
                transitions[index] = new Transition(target, character);
            }
            if (stream.Position != stream.Length) return null;
            return new PackedLanguageLexicon(states, transitions, root, wordCount);
        }
        catch
        {
            return null;
        }
    }

    public bool Contains(string word)
    {
        var state = _root;
        foreach (var character in word)
        {
            var next = FindTransition(state, char.ToLowerInvariant(character));
            if (next is null) return false;
            state = next.Value;
        }
        return _states[state].WordClass != LexiconWordClass.None;
    }

    public IReadOnlyList<BrokenKeyCandidate> Generate(
        string observed,
        ISet<char> brokenKeys,
        int maximumMissingLetters)
    {
        if (string.IsNullOrEmpty(observed) || observed.Length > 40 || brokenKeys.Count == 0)
            return Array.Empty<BrokenKeyCandidate>();
        maximumMissingLetters = Math.Clamp(maximumMissingLetters, 1, 8);
        observed = observed.ToLowerInvariant();
        var memo = new Dictionary<SearchState, CandidateSuffix[]>();
        var suffixes = Search(_root, 0, 0);
        return suffixes
            .Select(suffix => new BrokenKeyCandidate(suffix.Text, suffix.MissingCount, suffix.WordClass))
            .DistinctBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate.Word, StringComparer.Ordinal)
            .ToArray();

        CandidateSuffix[] Search(uint stateId, int observedIndex, int inserted)
        {
            var key = new SearchState(stateId, observedIndex, inserted);
            if (memo.TryGetValue(key, out var cached)) return cached;
            var state = _states[stateId];
            var found = new List<CandidateSuffix>();
            if (observedIndex == observed.Length && inserted > 0 && state.WordClass != LexiconWordClass.None)
                found.Add(new CandidateSuffix("", inserted, state.WordClass));

            var end = checked((int)state.FirstTransition + state.TransitionCount);
            for (var edgeIndex = checked((int)state.FirstTransition); edgeIndex < end; edgeIndex++)
            {
                var edge = _transitions[edgeIndex];
                if (observedIndex < observed.Length && edge.Character == observed[observedIndex])
                {
                    foreach (var suffix in Search(edge.Target, observedIndex + 1, inserted))
                        found.Add(suffix with { Text = edge.Character + suffix.Text });
                }
                if (inserted < maximumMissingLetters && brokenKeys.Contains(edge.Character))
                {
                    foreach (var suffix in Search(edge.Target, observedIndex, inserted + 1))
                        found.Add(suffix with { Text = edge.Character + suffix.Text });
                }
            }
            var result = found.ToArray();
            memo[key] = result;
            return result;
        }
    }

    private uint? FindTransition(uint stateId, char character)
    {
        var state = _states[stateId];
        var low = checked((int)state.FirstTransition);
        var high = low + state.TransitionCount - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var edge = _transitions[middle];
            if (edge.Character == character) return edge.Target;
            if (edge.Character < character) low = middle + 1;
            else high = middle - 1;
        }
        return null;
    }

    private readonly record struct State(uint FirstTransition, ushort TransitionCount, LexiconWordClass WordClass);
    private readonly record struct Transition(uint Target, char Character);
    private readonly record struct SearchState(uint State, int ObservedIndex, int Inserted);
    private sealed record CandidateSuffix(string Text, int MissingCount, LexiconWordClass WordClass);
}
