namespace Blanketmen.UnityMcp.Gateway;

internal enum UnityControlTransport
{
    Http = 0,
    Pipe = 1,
}

public static class UnityControlClientFactory
{
    public static IUnityControlClient CreateFromEnvironment()
    {
        UnityControlTransport transport = ParseTransport(Environment.GetEnvironmentVariable("UNITY_MCP_CONTROL_TRANSPORT"));
        int timeoutMs = ParseInt("UNITY_MCP_CONTROL_TIMEOUT_MS", 5000, min: 500, max: 120000);

        if (transport == UnityControlTransport.Pipe)
        {
            string pipeName = Environment.GetEnvironmentVariable("UNITY_MCP_CONTROL_PIPE_NAME") ?? "unity-mcp-control";
            return new NamedPipeUnityControlClient(pipeName, timeoutMs);
        }

        string rawUrl = Environment.GetEnvironmentVariable("UNITY_MCP_CONTROL_HTTP_URL") ?? "http://127.0.0.1:38100/";
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? baseUri))
        {
            throw new InvalidOperationException(
                $"Invalid UNITY_MCP_CONTROL_HTTP_URL: '{rawUrl}'");
        }

        return new HttpUnityControlClient(baseUri, timeoutMs);
    }

    private static UnityControlTransport ParseTransport(string? value)
    {
        return string.Equals(value, "pipe", StringComparison.OrdinalIgnoreCase)
            ? UnityControlTransport.Pipe
            : UnityControlTransport.Http;
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


