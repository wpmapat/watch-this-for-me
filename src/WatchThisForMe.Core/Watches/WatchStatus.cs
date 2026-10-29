namespace WatchThisForMe.Core.Watches;

public enum WatchStatus
{
    Active,
    Paused,

    // Set automatically after too many consecutive failed check runs, so a permanently
    // broken watch (dead source, revoked credentials) stops being silently retried forever.
    // The scheduler already skips anything that isn't Active, so this reuses that same
    // mechanism. A user can flip it back to Active once the underlying issue is fixed.
    DeadLettered
}
