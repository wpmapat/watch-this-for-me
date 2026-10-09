namespace WatchThisForMe.Infrastructure.Monitoring;

public class WatchSchedulerOptions
{
    public const string SectionName = "WatchScheduler";

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(1);
}
