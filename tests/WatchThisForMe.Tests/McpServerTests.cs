using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace WatchThisForMe.Tests;

// Requires the Cosmos DB Emulator to be running locally (https://localhost:8081),
// and spawns the built WatchThisForMe.McpServer as a real subprocess over stdio.
public class McpServerTests
{
    private static string McpServerDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && directory.Name != "WatchThisForMe")
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
            }

            return Path.Combine(directory.FullName, "src", "WatchThisForMe.McpServer", "bin", "Debug", "net10.0");
        }
    }

    private static async Task<McpClient> ConnectAsync()
    {
        var serverDirectory = McpServerDirectory;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "WatchThisForMe.McpServer",
            Command = "dotnet",
            Arguments = [Path.Combine(serverDirectory, "WatchThisForMe.McpServer.dll")],
            WorkingDirectory = serverDirectory,
            InheritEnvironmentVariables = true,
            EnvironmentVariables = new Dictionary<string, string?> { ["DOTNET_ENVIRONMENT"] = "Development" }
        });

        return await McpClient.CreateAsync(transport);
    }

    [Fact]
    public async Task Server_ExposesExpectedTools()
    {
        var client = await ConnectAsync();
        var tools = await client.ListToolsAsync();

        var toolNames = tools.Select(t => t.Name).ToList();
        Assert.Contains("get_web_page", toolNames);
        Assert.Contains("get_previous_observation", toolNames);
        Assert.Contains("save_observation", toolNames);
    }

    [Fact]
    public async Task GetWebPage_ReturnsNormalizedContent()
    {
        var client = await ConnectAsync();

        var result = await client.CallToolAsync(
            "get_web_page",
            new Dictionary<string, object?> { ["url"] = "https://example.com" });

        var text = result.Content.OfType<TextContentBlock>().First().Text;
        Assert.Contains("Example Domain", text);
    }

    [Fact]
    public async Task SaveObservation_ThenGetPreviousObservation_RoundTrips()
    {
        var client = await ConnectAsync();
        var watchId = Guid.NewGuid().ToString();

        try
        {
            var saveResult = await client.CallToolAsync(
                "save_observation",
                new Dictionary<string, object?> { ["watchId"] = watchId, ["content"] = "Some fetched page content." });

            var saveText = saveResult.Content.OfType<TextContentBlock>().First().Text;
            Assert.Contains(watchId, saveText);

            var getResult = await client.CallToolAsync(
                "get_previous_observation",
                new Dictionary<string, object?> { ["watchId"] = watchId });

            var getText = getResult.Content.OfType<TextContentBlock>().First().Text;
            Assert.Contains("Changed: True", getText);
        }
        finally
        {
            // No delete tool yet - this leaves one throwaway observation in the emulator,
            // which is harmless and will be pruned by the normal 10-per-watch cap over time.
        }
    }
}
