using Microsoft.Extensions.DependencyInjection;
using WatchThisForMe.Core.Sources;

namespace WatchThisForMe.Infrastructure.Sources;

public static class SourceServiceCollectionExtensions
{
    public static IServiceCollection AddSourceFetchers(this IServiceCollection services)
    {
        services.AddHttpClient<ISourceFetcher, WebPageSourceFetcher>();

        return services;
    }
}
