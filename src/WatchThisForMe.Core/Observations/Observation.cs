namespace WatchThisForMe.Core.Observations;

public class Observation
{
    public required string Id { get; set; }

    public required string WatchId { get; set; }

    public required DateTimeOffset CheckedAt { get; set; }

    public required string ContentHash { get; set; }

    public required string NormalizedContent { get; set; }

    // Which version of the page cleanup produced NormalizedContent. Null = saved before versions
    // existed (Nov 2026), when pages were flattened to a single line.
    public int? ContentFormatVersion { get; set; }

    public required bool Changed { get; set; }

    public string? DiffSummary { get; set; }

    public bool? Meaningful { get; set; }

    public string? Category { get; set; }

    public double? Confidence { get; set; }

    public string? ClassificationSummary { get; set; }

    // Embedding of ClassificationSummary - lets tomorrow's RAG retrieval find semantically
    // similar past observations instead of only exact lookups by watch id.
    public float[]? Embedding { get; set; }

    // True while a notice about this change still has to be made (set when the change is saved,
    // per the watch's NotificationPolicy; cleared once the notice exists or is ruled out). Stays
    // true if making it fails, so the next check retries. Null on changes saved before
    // notifications existed (Nov 2026) - those are never notified.
    public bool? NotificationPending { get; set; }

    // What happened to the notice, in plain words ("Sent", or why none was sent).
    public string? NotificationOutcome { get; set; }
}
