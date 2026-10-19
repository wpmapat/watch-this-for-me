using System.Text.Json;
using WatchThisForMe.Core.Observations;

namespace WatchThisForMe.Infrastructure.Ai;

public class OpenAiChangeClassifier(OpenAiResponsesClient client) : IChangeClassifier
{
    private const string Instructions =
        "You are deciding whether a detected change to a monitored web page is actually meaningful to the "
        + "user, based on what they said they care about. A change can be real (the text genuinely differs) "
        + "without being meaningful - e.g. a typo fix, a reworded sentence with the same meaning, or a change "
        + "unrelated to the user's stated criteria. Judge strictly against the user's criteria, not just "
        + "whether anything changed. Pick a short category label (e.g. breaking_change, security_fix, "
        + "pricing_change, minor_wording, unrelated). Confidence is 0-1. Summary is one sentence explaining "
        + "what changed and why it does or doesn't matter.";

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "meaningful": { "type": "boolean" },
            "category": { "type": "string" },
            "confidence": { "type": "number" },
            "summary": { "type": "string" }
          },
          "required": ["meaningful", "category", "confidence", "summary"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public async Task<ChangeClassification> ClassifyAsync(
        string diffSummary,
        string meaningfulChangeCriteria,
        CancellationToken cancellationToken = default)
    {
        var input = $"User's meaningful-change criteria: {meaningfulChangeCriteria}\n\nDiff of what changed:\n{diffSummary}";

        var root = await client.CreateStructuredResponseAsync(Instructions, input, "change_classification", Schema, cancellationToken);

        return new ChangeClassification(
            Meaningful: root.GetProperty("meaningful").GetBoolean(),
            Category: root.GetProperty("category").GetString()!,
            Confidence: root.GetProperty("confidence").GetDouble(),
            Summary: root.GetProperty("summary").GetString()!);
    }
}
