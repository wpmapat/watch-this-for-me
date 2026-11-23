namespace WatchThisForMe.Core.Observations;

public interface IInvestigationAgent
{
    Task<InvestigationResult> InvestigateAsync(
        string watchId,
        string source,          // the watched address: the only site the agent may open pages of, or link to
        string diffSummary,
        string meaningfulChangeCriteria,
        IReadOnlyList<SimilarObservation> similarPastObservations,
        string userInterests,
        CancellationToken cancellationToken = default);
}
