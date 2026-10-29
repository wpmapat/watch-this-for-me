namespace WatchThisForMe.Core.Observations;

public interface IInvestigationAgent
{
    Task<InvestigationResult> InvestigateAsync(
        string watchId,
        string diffSummary,
        string meaningfulChangeCriteria,
        IReadOnlyList<SimilarObservation> similarPastObservations,
        string userInterests,
        CancellationToken cancellationToken = default);
}
