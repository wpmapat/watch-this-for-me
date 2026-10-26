using System.ComponentModel;
using ModelContextProtocol.Server;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Infrastructure.Sources;

namespace WatchThisForMe.McpServer.Tools;

[McpServerToolType]
public sealed class ObservationTools(IObservationRepository observationRepository)
{
    [McpServerTool(Name = "get_previous_observation", ReadOnly = true, Destructive = false)]
    [Description("Gets the most recent stored observation for a watch, including whether it changed and how it was classified, if any.")]
    public async Task<string> GetPreviousObservation(
        [Description("The id of the watch to look up.")] string watchId)
    {
        var observation = await observationRepository.GetLatestAsync(watchId);
        if (observation is null)
        {
            return "No previous observation exists for this watch.";
        }

        return $"""
            Checked at: {observation.CheckedAt:u}
            Changed: {observation.Changed}
            Meaningful: {observation.Meaningful?.ToString() ?? "not classified"}
            Category: {observation.Category ?? "n/a"}
            Diff: {observation.DiffSummary ?? "n/a"}
            Classification summary: {observation.ClassificationSummary ?? "n/a"}
            """;
    }

    [McpServerTool(Name = "save_observation", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Saves a new observation snapshot for a watch, recording a content fingerprint for future comparison.")]
    public async Task<string> SaveObservation(
        [Description("The id of the watch this observation belongs to.")] string watchId,
        [Description("The content snapshot to save.")] string content)
    {
        var observation = new Observation
        {
            Id = Guid.NewGuid().ToString(),
            WatchId = watchId,
            CheckedAt = DateTimeOffset.UtcNow,
            ContentHash = ContentHasher.Compute(content),
            NormalizedContent = content,
            Changed = true
        };

        await observationRepository.CreateAsync(observation);
        return $"Saved observation {observation.Id} for watch {watchId}.";
    }
}
