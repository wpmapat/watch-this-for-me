namespace WatchThisForMe.Infrastructure.Ai;

public class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    public required string ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}
