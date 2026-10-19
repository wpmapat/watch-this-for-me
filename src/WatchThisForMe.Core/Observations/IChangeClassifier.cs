namespace WatchThisForMe.Core.Observations;

public interface IChangeClassifier
{
    Task<ChangeClassification> ClassifyAsync(
        string diffSummary,
        string meaningfulChangeCriteria,
        CancellationToken cancellationToken = default);
}
