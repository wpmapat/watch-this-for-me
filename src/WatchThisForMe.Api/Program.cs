using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using WatchThisForMe.Infrastructure.Ai;
using WatchThisForMe.Infrastructure.Cosmos;
using WatchThisForMe.Infrastructure.Monitoring;
using WatchThisForMe.Infrastructure.Observability;
using WatchThisForMe.Infrastructure.Security;
using WatchThisForMe.Infrastructure.Sources;

var builder = WebApplication.CreateBuilder(args);

// In Azure, secrets (OpenAI key, etc.) come from Key Vault; locally, from user-secrets as before.
builder.Configuration.AddKeyVaultIfConfigured();

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddCosmosDb(builder.Configuration);
builder.Services.AddSourceFetchers();
builder.Services.AddMonitoring(builder.Configuration);
builder.Services.AddAiInterpretation(builder.Configuration);

// Liveness only: "is this process up and serving requests?" Deliberately no Cosmos/OpenAI
// checks - Azure restarts an instance whose health check fails, and restarting a healthy app
// because a dependency blipped would cause restart loops without fixing the dependency.
builder.Services.AddHealthChecks();

// Every endpoint requires a caller signed in to our Entra tenant whose token carries this Api's
// "access_as_user" scope. A fallback policy (rather than [Authorize] per controller) means a
// controller added later is locked down by default instead of open by omission. The only way to
// run without it is local Development with no Entra app configured; anywhere else, missing config
// stops startup rather than silently serving anonymous callers.
var entraConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"]);
if (entraConfigured)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireScope("access_as_user")
            .Build());
}
else
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "AzureAd:ClientId is not configured. Refusing to start outside Development without authentication.");
    }

    // The auth middleware below still runs, so its services must exist - with no schemes registered,
    // every request is simply anonymous.
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization();
}

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
            // Health probes arrive every minute from Azure; tracing them would bury real traffic.
            .AddAspNetCoreInstrumentation(o => o.Filter = context => context.Request.Path != "/health")
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

if (!entraConfigured)
{
    app.Logger.LogWarning("AzureAd is not configured - the Api is accepting ANONYMOUS requests (Development only).");
}

await app.Services.GetRequiredService<CosmosDbInitializer>().InitializeAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Exempt from the sign-in fallback policy: Azure's health probe can't sign in, and the
// response ("Healthy"/"Unhealthy") reveals nothing.
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();
