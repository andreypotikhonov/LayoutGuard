using System.Text.Json;
using System.Text.Json.Serialization;

namespace LayoutGuard.Core;

internal sealed class BrokenKeyCandidateRanker
{
    private readonly RankerData _data;

    private BrokenKeyCandidateRanker(RankerData data) => _data = data;

    public static BrokenKeyCandidateRanker Load(string path)
    {
        try
        {
            var data = JsonSerializer.Deserialize<RankerData>(File.ReadAllText(path));
            if (data?.SchemaVersion == 1) return new BrokenKeyCandidateRanker(data);
        }
        catch
        {
            // Safe built-in defaults keep correction available if only the
            // tiny ranker configuration is damaged or absent.
        }
        return new BrokenKeyCandidateRanker(new RankerData());
    }

    public (string Word, double Score)? Choose(
        IReadOnlyList<BrokenKeyCandidate> candidates,
        Func<string, long> frequency,
        BrokenKeyLanguageStatistics? statistics,
        CorrectionContext? context,
        string? gapModelWord)
    {
        if (candidates.Count == 0) return null;
        var scored = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = Score(candidate, frequency, statistics, context, gapModelWord)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.MissingCount)
            .ThenBy(item => item.Candidate.Word, StringComparer.Ordinal)
            .ToArray();
        var best = scored[0];
        if (candidates.Count == 1 &&
            best.Candidate.Word.Length >= _data.UniqueCandidateMinimumLength &&
            best.Candidate.MissingCount <= 3)
            return (best.Candidate.Word, best.Score);
        var secondScore = scored.Length > 1 ? scored[1].Score : double.NegativeInfinity;
        return best.Score >= _data.MinimumScore && best.Score - secondScore >= _data.MinimumMargin
            ? (best.Candidate.Word, best.Score)
            : null;
    }

    private double Score(
        BrokenKeyCandidate candidate,
        Func<string, long> frequency,
        BrokenKeyLanguageStatistics? statistics,
        CorrectionContext? context,
        string? gapModelWord)
    {
        var unigram = Math.Max(frequency(candidate.Word), statistics?.Unigram(candidate.Word) ?? 0);
        var score = _data.UnigramWeight * Math.Log(1 + unigram);
        score -= _data.MissingLetterPenalty * candidate.MissingCount;
        score += candidate.WordClass switch
        {
            LexiconWordClass.Standard => _data.ClassBias.Standard,
            LexiconWordClass.Name => _data.ClassBias.Name,
            LexiconWordClass.Colloquial => _data.ClassBias.Colloquial,
            LexiconWordClass.Technical => _data.ClassBias.Tech,
            LexiconWordClass.Custom => _data.ClassBias.Custom,
            _ => 0
        };
        if (gapModelWord?.Equals(candidate.Word, StringComparison.OrdinalIgnoreCase) == true)
            score += _data.GapModelMatchBonus;
        if (statistics is not null && context?.PreviousToken1 is { Length: > 0 } previous)
        {
            score += _data.BigramWeight * Math.Log(1 + statistics.Bigram(previous, candidate.Word));
            if (context.PreviousToken2 is { Length: > 0 } previous2)
                score += _data.TrigramWeight * Math.Log(1 + statistics.Trigram(previous2, previous, candidate.Word));
        }
        return score;
    }

    private sealed class RankerData
    {
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("unigram_weight")] public double UnigramWeight { get; set; } = 1;
        [JsonPropertyName("bigram_weight")] public double BigramWeight { get; set; } = 1.25;
        [JsonPropertyName("trigram_weight")] public double TrigramWeight { get; set; } = 1.75;
        [JsonPropertyName("missing_letter_penalty")] public double MissingLetterPenalty { get; set; } = 1.2;
        [JsonPropertyName("gap_model_match_bonus")] public double GapModelMatchBonus { get; set; } = 1.5;
        [JsonPropertyName("class_bias")] public ClassBiasData ClassBias { get; set; } = new();
        [JsonPropertyName("minimum_score")] public double MinimumScore { get; set; }
        [JsonPropertyName("minimum_margin")] public double MinimumMargin { get; set; } = 0.35;
        [JsonPropertyName("unique_candidate_minimum_length")] public int UniqueCandidateMinimumLength { get; set; } = 4;
    }

    private sealed class ClassBiasData
    {
        [JsonPropertyName("STANDARD")] public double Standard { get; set; } = 0.4;
        [JsonPropertyName("NAME")] public double Name { get; set; } = 0.2;
        [JsonPropertyName("COLLOQUIAL")] public double Colloquial { get; set; } = 0.1;
        [JsonPropertyName("TECH")] public double Tech { get; set; } = 0.1;
        [JsonPropertyName("CUSTOM")] public double Custom { get; set; } = 1;
    }
}
