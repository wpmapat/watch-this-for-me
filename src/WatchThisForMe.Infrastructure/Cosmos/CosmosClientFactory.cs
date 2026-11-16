using Microsoft.Azure.Cosmos;
using WatchThisForMe.Infrastructure.Security;

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

        if (!options.UsesIdentity)
        {
            return new CosmosClient(options.ConnectionString, clientOptions);
        }

        if (string.IsNullOrWhiteSpace(options.AccountEndpoint))
        {
            throw new InvalidOperationException(
                $"Cosmos DB is not configured: set either {CosmosOptions.SectionName}:ConnectionString (emulator/local) "
                + $"or {CosmosOptions.SectionName}:AccountEndpoint (Azure, managed identity).");
        }

        // No key anywhere: the app proves who it is via its managed identity, and Cosmos checks that
        // identity has been granted a data-plane role on the account.
        return new CosmosClient(options.AccountEndpoint, AzureKeyVaultConfiguration.Credential, clientOptions);
    }
}
