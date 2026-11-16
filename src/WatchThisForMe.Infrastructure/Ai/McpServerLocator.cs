using ModelContextProtocol.Client;

namespace WatchThisForMe.Infrastructure.Ai;

// Finds the McpServer build to launch as a stdio subprocess, and describes how to launch it. Shared by
// the investigation agent and the tests so both start the server the same way.
public static class McpServerLocator
{
    public const string ServerDllName = "WatchThisForMe.McpServer.dll";

    // Published layout: the Api's publish step puts the MCP server in this subfolder of its own output.
    public const string PublishedSubfolder = "mcp";

    private const string SolutionFileName = "WatchThisForMe.slnx";

    public static string FindExecutableDirectory(string? configuredDirectory = null)
    {
        // 1. Explicit override.
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return RequireServerIn(configuredDirectory, "the configured Mcp:ServerDirectory");
        }

        // 2. Deployed: published next to the running app.
        var published = Path.Combine(AppContext.BaseDirectory, PublishedSubfolder);
        if (File.Exists(Path.Combine(published, ServerDllName)))
        {
            return published;
        }

        // 3. Running from source: the McpServer project's own build output, for the same configuration
        //    (Debug/Release) as this app. Found via the solution file, not the repo folder's name, which
        //    differs between machines (e.g. CI checks it out as "watch-this-for-me").
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = baseDirectory.Name;               // .../bin/{Configuration}/{TargetFramework}/
        var configuration = baseDirectory.Parent?.Name ?? "Debug";

        var repoRoot = baseDirectory;
        while (repoRoot is not null && !File.Exists(Path.Combine(repoRoot.FullName, SolutionFileName)))
        {
            repoRoot = repoRoot.Parent;
        }

        if (repoRoot is null)
        {
            throw new InvalidOperationException(
                $"Could not find the MCP server: no '{PublishedSubfolder}' folder next to the app, and not running "
                + $"from source (no {SolutionFileName} above {AppContext.BaseDirectory}).");
        }

        var fromSource = Path.Combine(repoRoot.FullName, "src", "WatchThisForMe.McpServer", "bin", configuration, targetFramework);
        return RequireServerIn(fromSource, $"the McpServer project's {configuration} build output (build the solution first)");
    }

    public static StdioClientTransportOptions CreateTransportOptions(string environmentName, string? configuredDirectory = null)
    {
        var serverDirectory = FindExecutableDirectory(configuredDirectory);
        return new StdioClientTransportOptions
        {
            Name = "WatchThisForMe.McpServer",
            Command = "dotnet",
            Arguments = [Path.Combine(serverDirectory, ServerDllName)],
            WorkingDirectory = serverDirectory,
            // Inherits the parent's settings (e.g. CosmosDb__AccountEndpoint, KeyVault__Uri in Azure), and runs in
            // the parent's environment so it picks the matching appsettings.{Environment}.json.
            InheritEnvironmentVariables = true,
            EnvironmentVariables = new Dictionary<string, string?> { ["DOTNET_ENVIRONMENT"] = environmentName }
        };
    }

    private static string RequireServerIn(string directory, string description)
    {
        if (!File.Exists(Path.Combine(directory, ServerDllName)))
        {
            throw new InvalidOperationException($"MCP server not found in {description}: {directory}");
        }

        return directory;
    }
}
