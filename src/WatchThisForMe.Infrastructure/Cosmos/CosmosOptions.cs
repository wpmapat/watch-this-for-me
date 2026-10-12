namespace WatchThisForMe.Infrastructure.Cosmos;

public class CosmosOptions
{
    public const string SectionName = "CosmosDb";

    public required string ConnectionString { get; set; }

    public string DatabaseName { get; set; } = "WatchThisForMe";

    public string WatchesContainerName { get; set; } = "Watches";

    public string ObservationsContainerName { get; set; } = "Observations";

    public bool IsEmulator { get; set; }
}
