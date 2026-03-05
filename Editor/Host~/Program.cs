using Blanketmen.UnityMcpServer.Host;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var server = McpServer.CreateDefault();
await server.RunAsync(
    Console.OpenStandardInput(),
    Console.OpenStandardOutput(),
    Console.Error,
    cancellation.Token);


