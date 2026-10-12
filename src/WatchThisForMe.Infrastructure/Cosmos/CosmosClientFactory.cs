using Microsoft.Azure.Cosmos;

namespace WatchThisForMe.Infrastructure.Cosmos;

public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosOptions options)
    {
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
    }
}
