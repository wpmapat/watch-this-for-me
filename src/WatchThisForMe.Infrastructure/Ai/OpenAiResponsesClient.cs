using System.Net.Http.Json;
using System.Text.Json;

namespace WatchThisForMe.Infrastructure.Ai;

public class OpenAiResponsesClient(HttpClient httpClient, OpenAiOptions options)
{
    public async Task<JsonElement> CreateStructuredResponseAsync(
        string instructions,
        string input,
        string schemaName,
        JsonElement schema,
        CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            model = options.Model,
            instructions,
            input,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = schemaName,
                    schema,
                    strict = true
                }
            }
        };

        using var httpResponse = await httpClient.PostAsJsonAsync("v1/responses", requestBody, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        using var responseDocument = await JsonDocument.ParseAsync(
            await httpResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var outputText = responseDocument.RootElement
            .GetProperty("output")[0]
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;

        using var resultDocument = JsonDocument.Parse(outputText);
        return resultDocument.RootElement.Clone();
    }
}
