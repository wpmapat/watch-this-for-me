using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace WatchThisForMe.Infrastructure.Security;

public static class AzureKeyVaultConfiguration
{
    // One credential for every Azure resource this app talks to (Key Vault, Cosmos). Locally it
    // resolves to your Visual Studio / Azure CLI sign-in; in Azure, the deployment sets
    // AZURE_TOKEN_CREDENTIALS=ManagedIdentityCredential so it uses the app's managed identity only,
    // rather than probing every developer credential type first.
    public static TokenCredential Credential { get; } = new DefaultAzureCredential();

    // Adds Key Vault as the highest-priority configuration source when KeyVault:Uri is set, so
    // existing code reading e.g. OpenAi:ApiKey gets the vault's "OpenAi--ApiKey" secret unchanged
    // ("--" is Key Vault's stand-in for ":", which secret names can't contain). Without the setting
    // (local dev), nothing changes and user-secrets/appsettings keep supplying values.
    public static IConfigurationManager AddKeyVaultIfConfigured(this IConfigurationManager configuration)
    {
        var vaultUri = configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            configuration.AddAzureKeyVault(new Uri(vaultUri), Credential);
        }

        return configuration;
    }
}
