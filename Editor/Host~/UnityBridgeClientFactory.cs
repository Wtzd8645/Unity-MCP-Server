namespace Blanketmen.UnityMcpServer.Host;

public static class UnityBridgeClientFactory
{
    public static IUnityBridgeClient CreateFromEnvironment()
    {
        string transport = Environment.GetEnvironmentVariable("UNITY_MCP_BRIDGE_TRANSPORT") ?? "http";
        int timeoutMs = ParseInt("UNITY_MCP_BRIDGE_TIMEOUT_MS", 5000, min: 500, max: 120000);

        if (transport.Equals("pipe", StringComparison.OrdinalIgnoreCase))
        {
            string pipeName = Environment.GetEnvironmentVariable("UNITY_MCP_BRIDGE_PIPE_NAME") ?? "unity-mcp-bridge";
            return new NamedPipeUnityBridgeClient(pipeName, timeoutMs);
        }

        string rawUrl = Environment.GetEnvironmentVariable("UNITY_MCP_BRIDGE_HTTP_URL") ?? "http://127.0.0.1:38100/";
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? baseUri))
        {
            throw new InvalidOperationException(
                $"Invalid UNITY_MCP_BRIDGE_HTTP_URL: '{rawUrl}'");
        }

        return new HttpUnityBridgeClient(baseUri, timeoutMs);
    }

    private static int ParseInt(string envName, int defaultValue, int min, int max)
    {
        string? raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out int value))
        {
            return defaultValue;
        }

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}


