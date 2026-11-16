using System.Net.Http.Json;
using System.Text.Json;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Infrastructure.Observability;

namespace WatchThisForMe.Infrastructure.Ai;

public class OpenAiEmbeddingGenerator(HttpClient httpClient, OpenAiOptions options) : IEmbeddingGenerator
{
    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("openai.embeddings");

        var requestBody = new { model = options.EmbeddingModel, input = text };

        using var httpResponse = await httpClient.PostAsJsonAsync("v1/embeddings", requestBody, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        using var responseDocument = await JsonDocument.ParseAsync(
            await httpResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        if (responseDocument.RootElement.TryGetProperty("usage", out var usage)
            && usage.TryGetProperty("prompt_tokens", out var promptTokens))
        {
            activity?.SetTag("openai.input_tokens", promptTokens.GetInt64());
            Telemetry.OpenAiTokens.Add(promptTokens.GetInt64(), new("direction", "input"), new("call_kind", "embed"));
        }

        return responseDocument.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(e => e.GetSingle())
            .ToArray();
    }
}
