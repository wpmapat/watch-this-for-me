using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using WatchThisForMe.Infrastructure.Observability;

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
        using var activity = Telemetry.ActivitySource.StartActivity("openai.responses");
        activity?.SetTag("openai.schema", schemaName);

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

        RecordTokenUsage(responseDocument.RootElement, activity, callKind: schemaName);

        var outputText = responseDocument.RootElement
            .GetProperty("output")[0]
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;

        using var resultDocument = JsonDocument.Parse(outputText);
        return resultDocument.RootElement.Clone();
    }

    // The usage block isn't present in every response shape (e.g. errors), so this is
    // deliberately best-effort - telemetry should never be the reason a real call fails.
    private static void RecordTokenUsage(JsonElement root, Activity? activity, string callKind)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return;
        }

        var inputTokens = usage.TryGetProperty("input_tokens", out var i) ? i.GetInt64() : 0;
        var outputTokens = usage.TryGetProperty("output_tokens", out var o) ? o.GetInt64() : 0;

        activity?.SetTag("openai.input_tokens", inputTokens);
        activity?.SetTag("openai.output_tokens", outputTokens);

        Telemetry.OpenAiTokens.Add(inputTokens, new("direction", "input"), new("call_kind", callKind));
        Telemetry.OpenAiTokens.Add(outputTokens, new("direction", "output"), new("call_kind", callKind));
    }
}
