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

    // Run records (one per check) expire automatically after this many days via the container's
    // default time-to-live - an hourly watch writes ~8,760 a year, and only recent ones matter.
    // In Azure the container is created by the deployment, which must set the same TTL.
    public int ExecutionRetentionDays { get; set; } = 30;

    public bool IsEmulator { get; set; }
}
