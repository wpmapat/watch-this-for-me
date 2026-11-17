using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Observations;

namespace WatchThisForMe.Core.Watches;

// Turns a watch's raw history into one dashboard row. Pure function over data the caller has
// already loaded, so it's testable without a database or web server.
public static class WatchSummaryFactory
{
    // How many recent runs Create needs to see: while a retry is in flight the latest run has
    // no error yet, and the reason the watch needs attention is on the failed run before it.
    public const int RecentExecutionsNeeded = 2;

    // recentExecutions: most recent first. observations: any order.
    public static WatchSummary Create(
        Watch watch,
        IReadOnlyList<WatchExecution> recentExecutions,
        IReadOnlyList<Observation> observations)
    {
        var health = WatchHealthEvaluator.Evaluate(watch.Status, recentExecutions.FirstOrDefault());

        var lastImportant = observations
            .Where(o => o.Meaningful == true)
            .MaxBy(o => o.CheckedAt);

        return new WatchSummary
        {
            Watch = watch,
            Health = health,
            LastError = health is WatchHealth.NeedsAttention or WatchHealth.Stopped
                ? recentExecutions.FirstOrDefault(e => e.Status == WatchExecutionStatus.Failed)?.ErrorMessage
                : null,
            // The very first observation is a baseline with no diff - not a change from anything.
            LastChangeAt = observations.Where(o => o.DiffSummary is not null).Select(o => (DateTimeOffset?)o.CheckedAt).Max(),
            LastImportantChangeAt = lastImportant?.CheckedAt,
            LastImportantChangeSummary = lastImportant?.ClassificationSummary,
            LastImportantChangeCategory = lastImportant?.Category
        };
    }
}
