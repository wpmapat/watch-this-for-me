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
            try
            {
                await RunDueChecksAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // E.g. Cosmos briefly unavailable while listing watches. Without this, the exception
                // escapes ExecuteAsync and .NET's default BackgroundService behavior stops the whole
                // host - the Api and all monitoring - over what may be a seconds-long blip. Log it
                // and try again next round instead.
                logger.LogError(ex, "Scheduler round failed; retrying in {Interval}", interval);
            }

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
            if (watch.Status != WatchStatus.Active || !WatchSchedule.IsDue(watch, DateTimeOffset.UtcNow))
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
}
