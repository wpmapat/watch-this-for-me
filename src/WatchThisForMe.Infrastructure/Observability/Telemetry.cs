using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WatchThisForMe.Infrastructure.Observability;

// Single shared ActivitySource/Meter for the whole app, named so Program.cs can tell
// OpenTelemetry to listen to exactly this source rather than every Activity in the process.
public static class Telemetry
{
    public const string SourceName = "WatchThisForMe";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> OpenAiTokens =
        Meter.CreateCounter<long>("watchthisforme.openai.tokens", unit: "tokens",
            description: "OpenAI tokens consumed, tagged by direction (input/output) and call_kind "
                + "(the schema name for structured calls, or 'embed' for embedding calls).");

    public static readonly Counter<long> CheckFailures =
        Meter.CreateCounter<long>("watchthisforme.check.failures", unit: "{failure}",
            description: "Failed watch check attempts, tagged by watch id.");

    public static readonly Histogram<double> StepDurationMs =
        Meter.CreateHistogram<double>("watchthisforme.check.step.duration", unit: "ms",
            description: "Duration of each pipeline step (fetch/classify/embed/save), tagged by step name.");
}
