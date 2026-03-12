using Blanketmen.UnityMcp.Gateway;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var server = McpServer.CreateDefault();
string transport = ResolveGatewayTransport(Environment.GetEnvironmentVariable("UNITY_MCP_GATEWAY_TRANSPORT"));

if (string.Equals(transport, "stdio", StringComparison.Ordinal))
{
    await server.RunAsync(
        Console.OpenStandardInput(),
        Console.OpenStandardOutput(),
        Console.Error,
        cancellation.Token);
}
else
{
    using var gateway = StreamableHttpGateway.CreateFromEnvironment(server);
    await gateway.RunAsync(Console.Error, cancellation.Token);
}

static string ResolveGatewayTransport(string? transportEnv)
{
    if (string.IsNullOrWhiteSpace(transportEnv))
    {
        return "streamable-http";
    }

    return string.Equals(transportEnv, "stdio", StringComparison.OrdinalIgnoreCase)
        ? "stdio"
        : "streamable-http";
}



