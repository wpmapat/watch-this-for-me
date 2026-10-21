namespace WatchThisForMe.Infrastructure.Ai;

public static class McpServerLocator
{
    // Local-dev convention: McpServer is a sibling project, built to the same configuration.
    // Revisit this once the app is actually deployed rather than run from source.
    public static string FindExecutableDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.Name != "WatchThisForMe")
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root to find the MCP server.");
        }

        return Path.Combine(directory.FullName, "src", "WatchThisForMe.McpServer", "bin", "Debug", "net10.0");
    }
}
