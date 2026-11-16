using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Infrastructure.Ai;

var apiProjectDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "WatchThisForMe.Api");

var configuration = new ConfigurationBuilder()
    .SetBasePath(Path.GetFullPath(apiProjectDir))
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets("c35fe433-fe0b-43df-b722-e69b06709803")
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddAiInterpretation(configuration);
await using var provider = services.BuildServiceProvider();

var classifier = provider.GetRequiredService<IChangeClassifier>();

var datasetPath = Path.Combine(AppContext.BaseDirectory, "dataset.json");
var dataset = JsonSerializer.Deserialize<List<EvaluationExample>>(
    File.ReadAllText(datasetPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

Console.WriteLine($"Loaded {dataset.Count} examples. Running classifier against each (concurrency 6)...\n");

var results = new List<EvaluationResult>(dataset.Count);
var resultsLock = new object();
var completed = 0;

await Parallel.ForEachAsync(dataset, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (example, ct) =>
{
    ChangeClassification actual;
    try
    {
        actual = await classifier.ClassifyAsync(
            example.DiffSummary, example.MeaningfulChangeCriteria, similarPastObservations: [], userInterests: "", ct);
    }
    catch (Exception ex)
    {
        actual = new ChangeClassification(false, "error", 0, $"Classifier threw: {ex.Message}");
    }

    var result = new EvaluationResult(example, actual);
    lock (resultsLock)
    {
        results.Add(result);
        completed++;
        Console.WriteLine($"[{completed}/{dataset.Count}] {example.Id}: expected={example.ExpectedMeaningful} actual={actual.Meaningful} {(result.Correct ? "OK" : "MISMATCH")}");
    }
});

results.Sort((a, b) => string.CompareOrdinal(a.Example.Id, b.Example.Id));

var truePositives = results.Count(r => r.Example.ExpectedMeaningful && r.Actual.Meaningful);
var falsePositives = results.Count(r => !r.Example.ExpectedMeaningful && r.Actual.Meaningful);
var falseNegatives = results.Count(r => r.Example.ExpectedMeaningful && !r.Actual.Meaningful);
var trueNegatives = results.Count(r => !r.Example.ExpectedMeaningful && !r.Actual.Meaningful);

var precision = truePositives + falsePositives == 0 ? 0 : (double)truePositives / (truePositives + falsePositives);
var recall = truePositives + falseNegatives == 0 ? 0 : (double)truePositives / (truePositives + falseNegatives);
var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
var accuracy = (double)(truePositives + trueNegatives) / results.Count;

Console.WriteLine("\n=== SUMMARY ===");
Console.WriteLine($"Total examples: {results.Count}");
Console.WriteLine($"True positives:  {truePositives}");
Console.WriteLine($"True negatives:  {trueNegatives}");
Console.WriteLine($"False positives: {falsePositives}  (said meaningful, actually wasn't)");
Console.WriteLine($"False negatives: {falseNegatives}  (said not meaningful, actually was)");
Console.WriteLine($"Accuracy:  {accuracy:P1}");
Console.WriteLine($"Precision: {precision:P1}");
Console.WriteLine($"Recall:    {recall:P1}");
Console.WriteLine($"F1:        {f1:P1}");

Console.WriteLine("\n=== BY DOMAIN ===");
foreach (var group in results.GroupBy(r => r.Example.Domain).OrderBy(g => g.Key))
{
    var correct = group.Count(r => r.Correct);
    Console.WriteLine($"  {group.Key}: {correct}/{group.Count()} correct");
}

var mismatches = results.Where(r => !r.Correct).ToList();
Console.WriteLine($"\n=== MISCLASSIFIED ({mismatches.Count}) ===");
foreach (var m in mismatches)
{
    Console.WriteLine($"  {m.Example.Id} | expected={m.Example.ExpectedMeaningful}/{m.Example.ExpectedCategory} actual={m.Actual.Meaningful}/{m.Actual.Category}");
    Console.WriteLine($"    diff: {m.Example.DiffSummary.Replace("\n", " \\n ")}");
    Console.WriteLine($"    classifier said: {m.Actual.Summary}");
}

var outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "results.json");
var summary = new
{
    totalExamples = results.Count,
    truePositives,
    trueNegatives,
    falsePositives,
    falseNegatives,
    accuracy,
    precision,
    recall,
    f1,
    mismatches = mismatches.Select(m => new
    {
        id = m.Example.Id,
        domain = m.Example.Domain,
        expectedMeaningful = m.Example.ExpectedMeaningful,
        expectedCategory = m.Example.ExpectedCategory,
        actualMeaningful = m.Actual.Meaningful,
        actualCategory = m.Actual.Category,
        actualSummary = m.Actual.Summary
    })
};
File.WriteAllText(Path.GetFullPath(outputPath), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"\nFull results written to {Path.GetFullPath(outputPath)}");

record EvaluationExample(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("watchContext")] string WatchContext,
    [property: JsonPropertyName("meaningfulChangeCriteria")] string MeaningfulChangeCriteria,
    [property: JsonPropertyName("diffSummary")] string DiffSummary,
    [property: JsonPropertyName("expectedMeaningful")] bool ExpectedMeaningful,
    [property: JsonPropertyName("expectedCategory")] string ExpectedCategory,
    [property: JsonPropertyName("rationale")] string Rationale);

record EvaluationResult(EvaluationExample Example, ChangeClassification Actual)
{
    public bool Correct => Example.ExpectedMeaningful == Actual.Meaningful;
}
