namespace WatchThisForMe.Core.Observations;

public interface IEmbeddingGenerator
{
    Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default);
}
