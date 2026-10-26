using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WatchThisForMe.Core.Observations;

namespace WatchThisForMe.Infrastructure.Ai;

public class OpenAiInvestigationAgent(HttpClient httpClient, OpenAiOptions options, ILogger<OpenAiInvestigationAgent> logger)
    : IInvestigationAgent, IAsyncDisposable
{
    // Caps how many tool calls one investigation can make before being forced to conclude,
    // so a confused agent can't loop on itself forever.
    private const int MaxToolCalls = 5;

    private const string Instructions =
        "You are investigating a detected change to a monitored source before deciding whether it's "
        + "meaningful to the user, based on their stated criteria. You have read-only tools available to "
        + "look up more context (e.g. this watch's own recent history, or the source's GitHub releases/issues) "
        + "if the diff alone doesn't give you enough information. Only call a tool if it would genuinely change "
        + "your answer - don't investigate for the sake of it. When you have enough information, respond with "
        + "your final classification: meaningful (bool), category (short label), confidence (0-1), and summary "
        + "(one sentence, citing what you found).";

    private static readonly JsonElement ResultSchema = JsonDocument.Parse("""
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

    // evidenceIndices forces the model to point at specific entries in the evidence list we
    // supply, rather than writing free-text "evidence" it could invent - the notification's
    // evidence always ends up being our own captured tool output, never a model paraphrase.
    private static readonly JsonElement NotificationSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "whatChanged": { "type": "string" },
            "whyItMatters": { "type": "string" },
            "confidence": { "type": "number" },
            "evidenceIndices": { "type": "array", "items": { "type": "integer" } }
          },
          "required": ["whatChanged", "whyItMatters", "confidence", "evidenceIndices"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private McpClient? _mcpClient;

    public async Task<InvestigationResult> InvestigateAsync(
        string watchId, string diffSummary, string meaningfulChangeCriteria, CancellationToken cancellationToken = default)
    {
        // Seeded with the diff itself - even if the agent calls no tools at all, the
        // notification still has at least one real source to ground its claims in.
        var evidence = new List<EvidenceItem> { new("diff", diffSummary) };

        var mcpClient = await GetMcpClientAsync(cancellationToken);
        var allTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

        // Only expose read-only tools - the investigating agent should look things up,
        // not save data, while it's deciding what's meaningful.
        var readOnlyTools = allTools.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true).ToList();
        var toolDefinitions = readOnlyTools
            .Select(t => (object)new { type = "function", name = t.Name, description = t.Description, parameters = t.JsonSchema })
            .ToList();

        object requestInput = $"""
            Watch id: {watchId}
            User's meaningful-change criteria: {meaningfulChangeCriteria}

            Diff of what changed:
            {diffSummary}
            """;

        string? previousResponseId = null;
        var toolCallCount = 0;

        while (true)
        {
            var allowTools = toolCallCount < MaxToolCalls;

            var requestBody = new Dictionary<string, object?>
            {
                ["model"] = options.Model,
                ["input"] = requestInput,
                ["text"] = new
                {
                    format = new { type = "json_schema", name = "investigation_result", schema = ResultSchema, strict = true }
                }
            };

            if (previousResponseId is null)
            {
                requestBody["instructions"] = Instructions;
            }
            else
            {
                requestBody["previous_response_id"] = previousResponseId;
            }

            if (allowTools)
            {
                requestBody["tools"] = toolDefinitions;
            }

            using var httpResponse = await httpClient.PostAsJsonAsync("v1/responses", requestBody, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            using var responseDocument = await JsonDocument.ParseAsync(
                await httpResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            var root = responseDocument.RootElement;
            previousResponseId = root.GetProperty("id").GetString();

            var outputItems = root.GetProperty("output").EnumerateArray().ToList();
            var functionCalls = outputItems.Where(item => item.GetProperty("type").GetString() == "function_call").ToList();

            if (functionCalls.Count == 0)
            {
                var message = outputItems.First(item => item.GetProperty("type").GetString() == "message");
                var text = message.GetProperty("content")[0].GetProperty("text").GetString()!;

                using var resultDocument = JsonDocument.Parse(text);
                var result = resultDocument.RootElement;

                var classification = new ChangeClassification(
                    Meaningful: result.GetProperty("meaningful").GetBoolean(),
                    Category: result.GetProperty("category").GetString()!,
                    Confidence: result.GetProperty("confidence").GetDouble(),
                    Summary: result.GetProperty("summary").GetString()!);

                // Only worth writing a user-facing notification for changes we've judged meaningful -
                // generating one for everything else would just be wasted model calls.
                var notification = classification.Meaningful
                    ? await GenerateNotificationAsync(previousResponseId, evidence, cancellationToken)
                    : null;

                return new InvestigationResult(classification, notification);
            }

            var functionOutputs = new List<object>();
            foreach (var call in functionCalls)
            {
                toolCallCount++;
                var (functionOutput, evidenceItem) = await ExecuteToolCallAsync(mcpClient, call, watchId, cancellationToken);
                functionOutputs.Add(functionOutput);
                evidence.Add(evidenceItem);
            }

            requestInput = functionOutputs;
        }
    }

    private async Task<(object FunctionOutput, EvidenceItem Evidence)> ExecuteToolCallAsync(
        McpClient mcpClient, JsonElement call, string watchId, CancellationToken cancellationToken)
    {
        var callId = call.GetProperty("call_id").GetString()!;
        var toolName = call.GetProperty("name").GetString()!;
        var argumentsJson = call.GetProperty("arguments").GetString()!;

        logger.LogInformation("Investigation of watch {WatchId} is calling tool {ToolName} with arguments {Arguments}", watchId, toolName, argumentsJson);

        string resultText;
        try
        {
            var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson) ?? [];
            var toolResult = await mcpClient.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
            resultText = string.Join("\n", toolResult.Content.OfType<TextContentBlock>().Select(c => c.Text));
            logger.LogInformation("Tool {ToolName} returned {Length} characters", toolName, resultText.Length);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool {ToolName} failed during investigation of watch {WatchId}", toolName, watchId);
            resultText = $"Tool call failed: {ex.Message}";
        }

        var functionOutput = new { type = "function_call_output", call_id = callId, output = resultText };
        return (functionOutput, new EvidenceItem(toolName, resultText));
    }

    private async Task<Notification> GenerateNotificationAsync(
        string? previousResponseId, List<EvidenceItem> evidence, CancellationToken cancellationToken)
    {
        var evidenceList = string.Join("\n\n", evidence.Select((e, i) => $"[{i}] ({e.Source}):\n{e.Detail}"));

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["previous_response_id"] = previousResponseId,
            ["input"] = $"""
                You judged this change as meaningful. Now write the notification the user will
                actually see. You may only rely on the evidence listed below - cite every index
                you actually relied on in evidenceIndices, and don't claim anything the evidence
                doesn't support.

                Evidence gathered during this investigation:
                {evidenceList}
                """,
            ["text"] = new
            {
                format = new { type = "json_schema", name = "notification", schema = NotificationSchema, strict = true }
            }
        };

        using var httpResponse = await httpClient.PostAsJsonAsync("v1/responses", requestBody, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        using var responseDocument = await JsonDocument.ParseAsync(
            await httpResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var message = responseDocument.RootElement.GetProperty("output").EnumerateArray()
            .First(item => item.GetProperty("type").GetString() == "message");
        var text = message.GetProperty("content")[0].GetProperty("text").GetString()!;

        using var resultDocument = JsonDocument.Parse(text);
        var result = resultDocument.RootElement;

        var citedEvidence = result.GetProperty("evidenceIndices").EnumerateArray()
            .Select(i => i.GetInt32())
            .Where(i => i >= 0 && i < evidence.Count)
            .Select(i => evidence[i])
            .ToList();

        return new Notification(
            WhatChanged: result.GetProperty("whatChanged").GetString()!,
            WhyItMatters: result.GetProperty("whyItMatters").GetString()!,
            Confidence: result.GetProperty("confidence").GetDouble(),
            Evidence: citedEvidence);
    }

    private async Task<McpClient> GetMcpClientAsync(CancellationToken cancellationToken)
    {
        if (_mcpClient is not null)
        {
            return _mcpClient;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_mcpClient is null)
            {
                var serverDirectory = McpServerLocator.FindExecutableDirectory();
                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "WatchThisForMe.McpServer",
                    Command = "dotnet",
                    Arguments = [Path.Combine(serverDirectory, "WatchThisForMe.McpServer.dll")],
                    WorkingDirectory = serverDirectory,
                    InheritEnvironmentVariables = true,
                    EnvironmentVariables = new Dictionary<string, string?> { ["DOTNET_ENVIRONMENT"] = "Development" }
                });

                _mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            }
        }
        finally
        {
            _connectLock.Release();
        }

        return _mcpClient;
    }

    public async ValueTask DisposeAsync()
    {
        if (_mcpClient is not null)
        {
            await _mcpClient.DisposeAsync();
        }
    }
}
