namespace WatchThisForMe.Core.Observations;

public interface IChangeClassifier
{
    Task<ChangeClassification> ClassifyAsync(
        string diffSummary,
        string meaningfulChangeCriteria,
        IReadOnlyList<SimilarObservation> similarPastObservations,
        string userInterests,
        CancellationToken cancellationToken = default);
}
