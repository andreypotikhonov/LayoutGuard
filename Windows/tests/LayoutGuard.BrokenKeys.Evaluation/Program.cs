using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using LayoutGuard.Core;

var repository = FindRepository();
var resources = Path.Combine(repository, "Windows", "src", "LayoutGuard.Windows", "Resources");
var dataset = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(repository, "Windows", "ml", "artifacts", "v2", "broken_keys_v2.tsv.gz");
var output = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(repository, "Windows", "ml", "artifacts", "v2", "runtime_evaluation.json");
if (!File.Exists(dataset)) throw new FileNotFoundException("Build the V2 dataset first", dataset);

var beforeLoad = GC.GetTotalMemory(true);
var engine = new CorrectionEngine(resources);
var afterLoad = GC.GetTotalMemory(true);
var options = new CorrectionOptions();
var latencies = new List<double>();
var counters = new Dictionary<string, long>();
using var file = File.OpenRead(dataset);
using var compressed = new GZipStream(file, CompressionMode.Decompress);
using var reader = new StreamReader(compressed);
var header = (await reader.ReadLineAsync())!.Split('\t');
var columns = header.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index);
while (await reader.ReadLineAsync() is { } line)
{
    var row = line.Split('\t');
    if (row[columns["split"]] != "test") continue;
    var observed = row[columns["observed"]];
    var expected = row[columns["expected"]];
    var missing = int.Parse(row[columns["missing_count"]]);
    var collision = row[columns["collision_class"]];
    var started = Stopwatch.GetTimestamp();
    var decision = engine.Decide(observed, options);
    latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    if (missing == 0)
    {
        Increment("clean_total");
        if (decision is null) Increment("clean_correct");
        continue;
    }

    Increment("positive_total");
    var prediction = decision?.Replacement ?? observed;
    var correct = prediction == expected;
    if (correct) Increment("positive_correct");
    Increment($"missing_{missing}_total");
    if (correct) Increment($"missing_{missing}_correct");
    if (collision != "REAL_WORD_COLLISION")
    {
        Increment("noncollision_total");
        if (correct) Increment("noncollision_correct");
    }
    else
    {
        Increment("collision_total");
        if (decision is null) Increment("collision_preserved");
    }
    if (row[columns["expected_class"]] == "NAME")
    {
        Increment("name_total");
        if (correct) Increment("name_correct");
    }
}

latencies.Sort();
double Ratio(string correct, string total) => Get(correct) / Math.Max(1.0, Get(total));
double Percentile(double percentile) => latencies[(int)Math.Round((latencies.Count - 1) * percentile)];
var result = new
{
    schema_version = 2,
    positive_exact_recovery = Ratio("positive_correct", "positive_total"),
    noncollision_positive_recovery = Ratio("noncollision_correct", "noncollision_total"),
    clean_preservation = Ratio("clean_correct", "clean_total"),
    false_positive_rate = 1 - Ratio("clean_correct", "clean_total"),
    collision_preservation = Ratio("collision_preserved", "collision_total"),
    name_recovery = Ratio("name_correct", "name_total"),
    buckets = new
    {
        missing_1 = Ratio("missing_1_correct", "missing_1_total"),
        missing_2 = Ratio("missing_2_correct", "missing_2_total"),
        missing_3 = Ratio("missing_3_correct", "missing_3_total")
    },
    rows = counters,
    latency_ms = new
    {
        median = Percentile(0.50),
        p95 = Percentile(0.95),
        p99 = Percentile(0.99),
        worst = latencies[^1]
    },
    managed_model_bytes = afterLoad - beforeLoad
};
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

void Increment(string key) => counters[key] = Get(key) + 1;
long Get(string key) => counters.GetValueOrDefault(key, 0);

static string FindRepository()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "Windows", "src"))) return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("LayoutGuard repository root was not found");
}
