namespace WatchThisForMe.Infrastructure.Cosmos;

public class CosmosOptions
{
    public const string SectionName = "CosmosDb";

    // Key-based access - used locally against the emulator. Leave empty in Azure.
    public string? ConnectionString { get; set; }

    // Identity-based access - used in Azure, where the app authenticates with its managed identity
    // instead of a key. Only consulted when ConnectionString is empty.
    public string? AccountEndpoint { get; set; }

    public bool UsesIdentity => string.IsNullOrWhiteSpace(ConnectionString);

    public string DatabaseName { get; set; } = "WatchThisForMe";

    public string WatchesContainerName { get; set; } = "Watches";

    public string ObservationsContainerName { get; set; } = "Observations";

    public string InterestProfileContainerName { get; set; } = "InterestProfile";

    public string ExecutionsContainerName { get; set; } = "Executions";

    public bool IsEmulator { get; set; }
}
