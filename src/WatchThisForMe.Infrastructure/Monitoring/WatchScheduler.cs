using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Monitoring;

public class WatchScheduler(
    IWatchRepository watchRepository,
    WatchCheckService checkService,
    IOptions<WatchSchedulerOptions> options,
    ILogger<WatchScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.PollingInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunDueChecksAsync(stoppingToken);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunDueChecksAsync(CancellationToken cancellationToken)
    {
        var watches = await watchRepository.ListAsync(cancellationToken);

        foreach (var watch in watches)
        {
            if (watch.Status != WatchStatus.Active || !IsDue(watch))
            {
                continue;
            }

            try
            {
                await checkService.CheckAsync(watch.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to check watch {WatchId}", watch.Id);
            }
        }
    }

    private static bool IsDue(Watch watch) =>
        watch.LastCheckedAt is null || DateTimeOffset.UtcNow - watch.LastCheckedAt >= watch.CheckFrequency;
}
