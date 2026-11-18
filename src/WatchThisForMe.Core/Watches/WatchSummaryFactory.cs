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
        var latest = recentExecutions.FirstOrDefault();
        var sourceProblem = DescribeSourceProblem(watch);
        var health = WatchHealthEvaluator.Evaluate(watch.Status, latest, sourceProblem is not null);

        var lastImportant = observations
            .Where(o => o.Meaningful == true)
            .MaxBy(o => o.CheckedAt);

        // The latest run succeeding means any older failure is history - the current problem
        // (if any) is the page itself.
        var lastError = latest?.Status is WatchExecutionStatus.Succeeded or WatchExecutionStatus.Started
            ? sourceProblem
            : recentExecutions.FirstOrDefault(e => e.Status == WatchExecutionStatus.Failed)?.ErrorMessage ?? sourceProblem;

        return new WatchSummary
        {
            Watch = watch,
            Health = health,
            LastError = health is WatchHealth.NeedsAttention or WatchHealth.Stopped ? lastError : null,
            // The very first observation is a baseline with no diff - not a change from anything.
            LastChangeAt = observations.Where(o => o.DiffSummary is not null).Select(o => (DateTimeOffset?)o.CheckedAt).Max(),
            LastImportantChangeAt = lastImportant?.CheckedAt,
            LastImportantChangeSummary = lastImportant?.ClassificationSummary,
            LastImportantChangeCategory = lastImportant?.Category
        };
    }

    // Checks succeed but on the wrong content - say what's wrong and what to do about it.
    public static string? DescribeSourceProblem(Watch watch) =>
        watch.PageUnavailableReason is { } reason
            ? $"The page doesn't look like the real page any more: {reason}"
            : watch.RedirectedTo is { } target
                ? $"This page now redirects to {target}. If it has moved, edit the watch's address."
                : null;
}
