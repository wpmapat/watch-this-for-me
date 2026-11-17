namespace WatchThisForMe.Core.Watches;

// One dashboard row: the watch itself plus what its execution and observation history say
// about it, assembled server-side so the page doesn't need extra API calls per watch.
public class WatchSummary
{
    public required Watch Watch { get; set; }

    public required WatchHealth Health { get; set; }

    // Error from the most recent run, when that run failed - shown so "needs attention" is actionable.
    public string? LastError { get; set; }

    // When content last changed at all (meaningful or not).
    public DateTimeOffset? LastChangeAt { get; set; }

    // The most recent change the classifier judged meaningful, among the retained history
    // (observations are pruned to the last 10 changes per watch).
    public DateTimeOffset? LastImportantChangeAt { get; set; }

    public string? LastImportantChangeSummary { get; set; }

    public string? LastImportantChangeCategory { get; set; }
}
