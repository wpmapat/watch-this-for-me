using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Cosmos;

public static class CosmosServiceCollectionExtensions
{
    public static IServiceCollection AddCosmosDb(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CosmosOptions>(configuration.GetSection(CosmosOptions.SectionName));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;
            return CosmosClientFactory.Create(options);
        });

        services.AddSingleton<CosmosDbInitializer>();
        services.AddSingleton<IWatchRepository, CosmosWatchRepository>();

        return services;
    }
}
