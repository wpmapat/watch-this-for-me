using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WatchThisForMe.Infrastructure.Cosmos;
using WatchThisForMe.Infrastructure.Sources;
using WatchThisForMe.McpServer.Tools;

var builder = Host.CreateApplicationBuilder(args);

// MCP's stdio transport uses stdout exclusively for protocol messages, so all
// logging must go to stderr instead.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddCosmosDb(builder.Configuration);
builder.Services.AddSourceFetchers();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SourceTools>()
    .WithTools<ObservationTools>();

var host = builder.Build();

await host.Services.GetRequiredService<CosmosDbInitializer>().InitializeAsync();

await host.RunAsync();
