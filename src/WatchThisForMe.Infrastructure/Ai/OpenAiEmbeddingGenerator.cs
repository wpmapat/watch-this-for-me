using System.Net.Http.Json;
using System.Text.Json;
using WatchThisForMe.Core.Observations;

namespace WatchThisForMe.Infrastructure.Ai;

public class OpenAiEmbeddingGenerator(HttpClient httpClient, OpenAiOptions options) : IEmbeddingGenerator
{
    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        var requestBody = new { model = options.EmbeddingModel, input = text };

        using var httpResponse = await httpClient.PostAsJsonAsync("v1/embeddings", requestBody, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        using var responseDocument = await JsonDocument.ParseAsync(
            await httpResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        return responseDocument.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(e => e.GetSingle())
            .ToArray();
    }
}
