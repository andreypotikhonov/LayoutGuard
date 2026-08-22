using System.Text.Json;
using System.Text.Json.Serialization;

namespace LayoutGuard.Core;

/// <summary>
/// Tiny character-gap classifier trained by Windows/ml/train_gap_model.py.
/// The runtime is plain C#: no Python, ONNX, native ML library, or network call.
/// </summary>
internal sealed class BrokenKeyGapModel
{
    private const string Padding = "<pad>";
    private const string Beginning = "<bos>";
    private const string End = "<eos>";
    private const int BeamSize = 16;
    private const int TopLabelsPerGap = 2;
    private readonly ModelData _data;
    private readonly BrokenKeyVocabulary _vocabulary;
    private readonly Dictionary<string, int> _characterIds;
    private readonly HashSet<char> _modelLetters;

    private BrokenKeyGapModel(ModelData data, BrokenKeyVocabulary vocabulary)
    {
        _data = data;
        _vocabulary = vocabulary;
        _characterIds = data.Characters
            .Select((character, index) => (character, index))
            .ToDictionary(item => item.character, item => item.index, StringComparer.Ordinal);
        _modelLetters = data.Letters.ToHashSet();
    }

    public static BrokenKeyGapModel? Load(string path, string vocabularyPath)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var data = JsonSerializer.Deserialize<ModelData>(File.ReadAllText(path));
            var vocabulary = BrokenKeyVocabulary.Load(vocabularyPath);
            return data is null || data.SchemaVersion != 1 || vocabulary is null
                ? null
                : new BrokenKeyGapModel(data, vocabulary);
        }
        catch
        {
            return null;
        }
    }

    public bool Supports(ISet<char> brokenLetters) =>
        brokenLetters.Count > 0 && brokenLetters.All(_modelLetters.Contains);

    public (string Word, double Score)? Predict(
        string input,
        ISet<char> brokenLetters,
        int maximumMissing,
        Func<string, bool> isValidWord,
        Func<string, long> frequency)
    {
        if (!Supports(brokenLetters) || input.Length is < 2 or > 40) return null;
        maximumMissing = Math.Clamp(maximumMissing, 1, _data.MaximumMissing);
        var beams = new List<Beam> { new("", 0, 0) };

        for (var gap = 0; gap <= input.Length; gap++)
        {
            var logits = EvaluateGap(input, gap);
            var emptyLogit = logits[0];
            var options = _data.Labels
                .Select((label, index) => new LabelOption(label, index, logits[index] - emptyLogit))
                .Where(option => option.Index > 0 &&
                    option.Label.Length <= maximumMissing &&
                    option.Label.All(brokenLetters.Contains) &&
                    option.Gain >= -8)
                .OrderByDescending(option => option.Gain)
                .Take(TopLabelsPerGap)
                .Prepend(new LabelOption("", 0, 0))
                .ToArray();

            var suffix = gap < input.Length ? input[gap].ToString() : "";
            beams = beams
                .SelectMany(beam => options
                    .Where(option => beam.Inserted + option.Label.Length <= maximumMissing)
                    .Select(option => new Beam(
                        beam.Word + option.Label + suffix,
                        beam.Inserted + option.Label.Length,
                        beam.Score + option.Gain)))
                .OrderByDescending(beam => beam.Score)
                .Take(BeamSize)
                .ToList();
        }

        var originalFrequency = frequency(input);
        var best = beams
            .Where(beam => beam.Inserted > 0 && !beam.Word.Equals(input, StringComparison.OrdinalIgnoreCase))
            .GroupBy(beam => beam.Word, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(beam => beam.Score).First())
            .Where(beam => _vocabulary.Contains(beam.Word) && isValidWord(beam.Word))
            .Select(beam => (
                beam.Word,
                Score: beam.Score + _data.FrequencyWeight *
                    (Math.Log(1 + frequency(beam.Word)) - Math.Log(1 + originalFrequency))))
            .Where(candidate => candidate.Score >= _data.CorrectionScoreThreshold)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Word.Length)
            .ThenBy(candidate => candidate.Word, StringComparer.Ordinal)
            .FirstOrDefault();
        return best.Word is null ? null : best;
    }

    private float[] EvaluateGap(string word, int gap)
    {
        var context = GapContext(word, gap);
        var flattened = new float[context.Length * _data.Embedding[0].Length];
        for (var position = 0; position < context.Length; position++)
        {
            var embedding = _data.Embedding[context[position]];
            Array.Copy(embedding, 0, flattened, position * embedding.Length, embedding.Length);
        }

        var hidden = new float[_data.HiddenBias.Length];
        for (var row = 0; row < hidden.Length; row++)
        {
            var value = _data.HiddenBias[row];
            var weights = _data.HiddenWeight[row];
            for (var column = 0; column < flattened.Length; column++)
                value += weights[column] * flattened[column];
            hidden[row] = Math.Max(0, value);
        }

        var logits = new float[_data.OutputBias.Length];
        for (var row = 0; row < logits.Length; row++)
        {
            var value = _data.OutputBias[row];
            var weights = _data.OutputWeight[row];
            for (var column = 0; column < hidden.Length; column++)
                value += weights[column] * hidden[column];
            logits[row] = value;
        }
        return logits;
    }

    private int[] GapContext(string word, int gap)
    {
        var context = new int[_data.Window * 2];
        for (var offset = 0; offset < _data.Window; offset++)
        {
            var leftIndex = gap - _data.Window + offset;
            context[offset] = leftIndex < 0
                ? CharacterId(Beginning)
                : CharacterId(word[leftIndex].ToString());
            var rightIndex = gap + offset;
            context[_data.Window + offset] = rightIndex >= word.Length
                ? CharacterId(End)
                : CharacterId(word[rightIndex].ToString());
        }
        return context;
    }

    private int CharacterId(string character) =>
        _characterIds.GetValueOrDefault(character, _characterIds[Padding]);

    private sealed record Beam(string Word, int Inserted, double Score);
    private sealed record LabelOption(string Label, int Index, double Gain);

    private sealed class ModelData
    {
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
        [JsonPropertyName("letters")] public string Letters { get; set; } = "";
        [JsonPropertyName("maximum_missing")] public int MaximumMissing { get; set; } = 3;
        [JsonPropertyName("window")] public int Window { get; set; }
        [JsonPropertyName("characters")] public string[] Characters { get; set; } = [];
        [JsonPropertyName("labels")] public string[] Labels { get; set; } = [];
        [JsonPropertyName("frequency_weight")] public double FrequencyWeight { get; set; }
        [JsonPropertyName("correction_score_threshold")] public double CorrectionScoreThreshold { get; set; }
        [JsonPropertyName("embedding")] public float[][] Embedding { get; set; } = [];
        [JsonPropertyName("hidden_weight")] public float[][] HiddenWeight { get; set; } = [];
        [JsonPropertyName("hidden_bias")] public float[] HiddenBias { get; set; } = [];
        [JsonPropertyName("output_weight")] public float[][] OutputWeight { get; set; } = [];
        [JsonPropertyName("output_bias")] public float[] OutputBias { get; set; } = [];
    }
}
