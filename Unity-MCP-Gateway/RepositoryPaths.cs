namespace Blanketmen.UnityMcp.Gateway;

public static class RepositoryPaths
{
    public static string ResolveMcpRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("UNITY_MCP_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            string resolvedOverride = Path.GetFullPath(overrideRoot);
            EnsureLooksLikeMcpRoot(resolvedOverride);
            return resolvedOverride;
        }

        string? fromCurrentDirectory = TryFindMcpRootFrom(Directory.GetCurrentDirectory());
        if (fromCurrentDirectory is not null)
        {
            return fromCurrentDirectory;
        }

        string? fromBinaryDirectory = TryFindMcpRootFrom(AppContext.BaseDirectory);
        if (fromBinaryDirectory is not null)
        {
            return fromBinaryDirectory;
        }

        throw new DirectoryNotFoundException(
            "Could not locate MCP root. " +
            "Set UNITY_MCP_ROOT to a directory containing schemas/mcp-tool-modules.json.");
    }

    private static void EnsureLooksLikeMcpRoot(string rootPath)
    {
        string manifestPath = Path.Combine(rootPath, "schemas", "mcp-tool-modules.json");
        if (!File.Exists(manifestPath))
        {
            throw new DirectoryNotFoundException(
                $"UNITY_MCP_ROOT does not contain '{manifestPath}'.");
        }
    }

    private static string? TryFindMcpRootFrom(string startPath)
    {
        DirectoryInfo? current = new(Path.GetFullPath(startPath));
        while (current is not null)
        {
            string manifestPath = Path.Combine(current.FullName, "schemas", "mcp-tool-modules.json");
            if (File.Exists(manifestPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}




