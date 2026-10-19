namespace WatchThisForMe.Core.Observations;

public record ChangeClassification(
    bool Meaningful,
    string Category,
    double Confidence,
    string Summary);
