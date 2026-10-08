using Microsoft.Extensions.DependencyInjection;

namespace WatchThisForMe.Infrastructure.Monitoring;

public static class MonitoringServiceCollectionExtensions
{
    public static IServiceCollection AddMonitoring(this IServiceCollection services)
    {
        services.AddSingleton<WatchCheckService>();

        return services;
    }
}
