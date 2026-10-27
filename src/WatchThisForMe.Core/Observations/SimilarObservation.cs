namespace WatchThisForMe.Core.Observations;

public record SimilarObservation(
    DateTimeOffset CheckedAt,
    string Summary,
    bool Meaningful,
    string Category,
    double SimilarityScore);
