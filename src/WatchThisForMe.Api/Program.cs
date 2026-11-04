using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using WatchThisForMe.Infrastructure.Ai;
using WatchThisForMe.Infrastructure.Cosmos;
using WatchThisForMe.Infrastructure.Monitoring;
using WatchThisForMe.Infrastructure.Observability;
using WatchThisForMe.Infrastructure.Sources;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddCosmosDb(builder.Configuration);
builder.Services.AddSourceFetchers();
builder.Services.AddMonitoring(builder.Configuration);
builder.Services.AddAiInterpretation(builder.Configuration);

// Traces every watch-check pipeline step (fetch/diff/classify/embed/save) plus every
// outbound HTTP call (OpenAI, GitHub, the watched page itself) for free via HttpClient
// instrumentation. Exports to the console in Development so it's inspectable without a
// live Azure resource; exports to Application Insights once a connection string exists
// (provisioned alongside the rest of the deployment - see Friday's CI/CD + deploy task).
var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
var otelBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("WatchThisForMe.Api"))
    .WithTracing(tracing =>
    {
        tracing.AddSource(Telemetry.SourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (builder.Environment.IsDevelopment())
        {
            tracing.AddConsoleExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(Telemetry.SourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (builder.Environment.IsDevelopment())
        {
            // Shorter export interval than the 60s default so metrics show up quickly
            // while developing locally, instead of waiting a full minute to see anything.
            metrics.AddConsoleExporter((_, readerOptions) =>
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000);
        }
    });

if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    otelBuilder.UseAzureMonitor(o => o.ConnectionString = appInsightsConnectionString);
}

var app = builder.Build();

await app.Services.GetRequiredService<CosmosDbInitializer>().InitializeAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
