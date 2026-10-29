using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using WatchThisForMe.Core.Sources;

namespace WatchThisForMe.Infrastructure.Sources;

public static class SourceServiceCollectionExtensions
{
    public static IServiceCollection AddSourceFetchers(this IServiceCollection services)
    {
        services.AddHttpClient<ISourceFetcher, WebPageSourceFetcher>()
            .AddStandardResilienceHandler();

        return services;
    }
}
