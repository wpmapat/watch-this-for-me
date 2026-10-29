namespace WatchThisForMe.Core.Observations;

public class Observation
{
    public required string Id { get; set; }

    public required string WatchId { get; set; }

    public required DateTimeOffset CheckedAt { get; set; }

    public required string ContentHash { get; set; }

    public required string NormalizedContent { get; set; }

    public required bool Changed { get; set; }

    public string? DiffSummary { get; set; }

    public bool? Meaningful { get; set; }

    public string? Category { get; set; }

    public double? Confidence { get; set; }

    public string? ClassificationSummary { get; set; }

    // Embedding of ClassificationSummary - lets tomorrow's RAG retrieval find semantically
    // similar past observations instead of only exact lookups by watch id.
    public float[]? Embedding { get; set; }
}
