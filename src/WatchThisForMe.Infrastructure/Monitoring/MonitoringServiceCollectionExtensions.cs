using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WatchThisForMe.Infrastructure.Monitoring;

public static class MonitoringServiceCollectionExtensions
{
    public static IServiceCollection AddMonitoring(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WatchSchedulerOptions>(configuration.GetSection(WatchSchedulerOptions.SectionName));
        services.AddSingleton<WatchCheckService>();
        services.AddHostedService<WatchScheduler>();

        return services;
    }
}
