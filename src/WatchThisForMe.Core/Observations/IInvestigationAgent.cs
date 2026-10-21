namespace WatchThisForMe.Core.Observations;

public interface IInvestigationAgent
{
    Task<ChangeClassification> InvestigateAsync(
        string watchId,
        string diffSummary,
        string meaningfulChangeCriteria,
        CancellationToken cancellationToken = default);
}
