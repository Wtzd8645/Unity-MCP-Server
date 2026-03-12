namespace Blanketmen.UnityMcp.Gateway;

public static class RepositoryPaths
{
    public static string ResolveMcpRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("UNITY_MCP_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            string resolvedOverride = Path.GetFullPath(overrideRoot);
            string? resolvedMcpRoot = TryResolveMcpRootFromCandidate(resolvedOverride);
            if (resolvedMcpRoot is not null)
            {
                return resolvedMcpRoot;
            }

            throw new DirectoryNotFoundException(
                "UNITY_MCP_ROOT does not contain a valid MCP schemas directory. " +
                "Expected either '<root>/schemas/mcp-tool-modules.json' or " +
                "'<root>/Gateway~/schemas/mcp-tool-modules.json'.");
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
            "Set UNITY_MCP_ROOT to a directory containing schemas/mcp-tool-modules.json " +
            "or Gateway~/schemas/mcp-tool-modules.json.");
    }

    private static string? TryFindMcpRootFrom(string startPath)
    {
        DirectoryInfo? current = new(Path.GetFullPath(startPath));
        while (current is not null)
        {
            string? resolved = TryResolveMcpRootFromCandidate(current.FullName);
            if (resolved is not null)
            {
                return resolved;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? TryResolveMcpRootFromCandidate(string rootPath)
    {
        string directManifestPath = Path.Combine(rootPath, "schemas", "mcp-tool-modules.json");
        if (File.Exists(directManifestPath))
        {
            return rootPath;
        }

        string gatewayChildRoot = Path.Combine(rootPath, "Gateway~");
        string gatewayChildManifestPath = Path.Combine(gatewayChildRoot, "schemas", "mcp-tool-modules.json");
        if (File.Exists(gatewayChildManifestPath))
        {
            return gatewayChildRoot;
        }

        return null;
    }
}


