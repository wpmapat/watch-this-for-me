using System.Net.Http.Json;
using System.Text.Json;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Ai;

public class OpenAiWatchRequestInterpreter(HttpClient httpClient, OpenAiOptions options) : IWatchRequestInterpreter
{
    private const string Instructions =
        "Convert the user's natural-language monitoring request into a structured watch configuration. "
        + "Pick a short, descriptive name. CheckFrequencyMinutes should reflect how urgent the request sounds "
        + "(default to 1440 minutes/daily if unspecified). Leave investigationInstructions null unless the "
        + "request implies deeper investigation is needed.";

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "source": { "type": "string" },
            "sourceType": { "type": "string", "enum": ["WebPage", "GitHub", "Rss"] },
            "checkFrequencyMinutes": { "type": "integer" },
            "meaningfulChangeCriteria": { "type": "string" },
            "investigationInstructions": { "type": ["string", "null"] },
            "notificationPolicy": { "type": "string", "enum": ["Always", "OnlyMeaningfulChanges"] },
            "notificationChannel": { "type": "string", "enum": ["Email"] }
          },
          "required": ["name", "source", "sourceType", "checkFrequencyMinutes", "meaningfulChangeCriteria", "investigationInstructions", "notificationPolicy", "notificationChannel"],
          "additionalProperties": false
        }
        """).RootElement;

    public async Task<WatchDraft> InterpretAsync(string request, CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            model = options.Model,
            instructions = Instructions,
            input = request,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "watch_draft",
                    schema = Schema,
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

        using var draftDocument = JsonDocument.Parse(outputText);
        var root = draftDocument.RootElement;
        var investigationInstructions = root.GetProperty("investigationInstructions");

        return new WatchDraft(
            Name: root.GetProperty("name").GetString()!,
            Source: root.GetProperty("source").GetString()!,
            SourceType: Enum.Parse<SourceType>(root.GetProperty("sourceType").GetString()!),
            CheckFrequency: TimeSpan.FromMinutes(root.GetProperty("checkFrequencyMinutes").GetInt32()),
            MeaningfulChangeCriteria: root.GetProperty("meaningfulChangeCriteria").GetString()!,
            InvestigationInstructions: investigationInstructions.ValueKind == JsonValueKind.Null
                ? null
                : investigationInstructions.GetString(),
            NotificationPolicy: Enum.Parse<NotificationPolicy>(root.GetProperty("notificationPolicy").GetString()!),
            NotificationChannel: Enum.Parse<NotificationChannel>(root.GetProperty("notificationChannel").GetString()!));
    }
}
