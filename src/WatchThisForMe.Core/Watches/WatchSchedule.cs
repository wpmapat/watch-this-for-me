namespace WatchThisForMe.Core.Watches;

public static class WatchSchedule
{
    // Due once a full CheckFrequency has passed since the last *attempt*. Basing this on
    // attempts (not successes) matters: otherwise a failing watch looks overdue on every
    // scheduler tick, burns through its retry budget in seconds, and gets dead-lettered by
    // something as brief as a network blip.
    public static bool IsDue(Watch watch, DateTimeOffset now)
    {
        // Watches saved before LastAttemptAt existed fall back to their last success.
        var lastRun = watch.LastAttemptAt ?? watch.LastCheckedAt;
        return lastRun is null || now - lastRun >= watch.CheckFrequency;
    }
}
