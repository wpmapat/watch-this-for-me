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
        + "what changed and why it does or doesn't matter. You may be shown similar past changes on this same "
        + "watch and how they were judged - use them to stay consistent (e.g. don't call this one meaningful "
        + "if a near-identical past change was judged not meaningful, and vice versa), but still judge this "
        + "diff on its own merits against the user's criteria rather than copying a past verdict blindly. "
        + "You may also be given the user's general interests, separate from this watch's own criteria - use "
        + "them only as color (e.g. to sharpen the summary or lean the confidence), never to override the "
        + "watch's own stated criteria for what counts as meaningful here.";

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
        IReadOnlyList<SimilarObservation> similarPastObservations,
        string userInterests,
        CancellationToken cancellationToken = default)
    {
        var pastContext = similarPastObservations.Count == 0
            ? "None."
            : string.Join("\n", similarPastObservations.Select(s =>
                $"- {s.CheckedAt:u} (judged meaningful: {s.Meaningful}, category: {s.Category}): {s.Summary}"));

        var input = $"""
            User's meaningful-change criteria: {meaningfulChangeCriteria}

            User's general interests (context only, doesn't override the criteria above): {(string.IsNullOrWhiteSpace(userInterests) ? "None stated." : userInterests)}

            Similar past changes on this watch:
            {pastContext}

            Diff of what changed:
            {diffSummary}
            """;

        var root = await client.CreateStructuredResponseAsync(Instructions, input, "change_classification", Schema, cancellationToken);

        return new ChangeClassification(
            Meaningful: root.GetProperty("meaningful").GetBoolean(),
            Category: root.GetProperty("category").GetString()!,
            Confidence: root.GetProperty("confidence").GetDouble(),
            Summary: root.GetProperty("summary").GetString()!);
    }
}
