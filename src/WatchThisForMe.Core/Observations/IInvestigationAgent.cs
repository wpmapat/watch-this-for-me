namespace WatchThisForMe.Core.Observations;

public interface IInvestigationAgent
{
    Task<InvestigationResult> InvestigateAsync(
        string watchId,
        string diffSummary,
        string meaningfulChangeCriteria,
        CancellationToken cancellationToken = default);
}
