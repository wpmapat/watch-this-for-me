using Microsoft.Azure.Cosmos;
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
            var clientOptions = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            };

            if (options.IsEmulator)
            {
                // The emulator presents a self-signed certificate; trust it only in this emulator-only code path.
                clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                });
                clientOptions.ConnectionMode = ConnectionMode.Gateway;
            }

            return new CosmosClient(options.ConnectionString, clientOptions);
        });

        services.AddSingleton<CosmosDbInitializer>();
        services.AddSingleton<IWatchRepository, CosmosWatchRepository>();

        return services;
    }
}
