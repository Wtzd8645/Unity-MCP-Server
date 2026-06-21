using Blanketmen.UnityMcp.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

string transport = ResolveGatewayTransport(Environment.GetEnvironmentVariable("UNITY_MCP_GATEWAY_TRANSPORT"));
if (string.Equals(transport, "stdio", StringComparison.Ordinal))
{
    await RunStdioAsync(args, cancellation.Token);
}
else
{
    await RunHttpAsync(args, cancellation.Token);
}

static async Task RunStdioAsync(string[] args, CancellationToken cancellationToken)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
    ConfigureLogging(builder.Logging);
    ConfigureMcpServer(builder.Services)
        .WithStdioServerTransport();

    using IHost host = builder.Build();
    await host.RunAsync(cancellationToken);
}

static async Task RunHttpAsync(string[] args, CancellationToken cancellationToken)
{
    GatewayHttpEndpoint endpoint = GatewayHttpEndpoint.CreateFromEnvironment();
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls(endpoint.ListenUrl);
    ConfigureLogging(builder.Logging);
    ConfigureMcpServer(builder.Services)
        .WithHttpTransport(options =>
        {
            options.Stateless = true;
        });

    WebApplication app = builder.Build();
    app.Use(async (context, next) =>
    {
        if (!endpoint.IsAllowedRequestHost(context.Request.Host.Host))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await next(context);
    });
    app.MapMcp(endpoint.RoutePattern);

    await Console.Error.WriteLineAsync(
        $"[{DateTimeOffset.UtcNow:O}] streamable-http listening on {endpoint.ListenUrl}{endpoint.RoutePattern}");
    await app.RunAsync(cancellationToken);
}

static Microsoft.Extensions.DependencyInjection.IMcpServerBuilder ConfigureMcpServer(IServiceCollection services)
{
    UnityMcpToolBridge bridge = UnityMcpToolBridge.CreateDefault();
    services.AddSingleton(bridge);

    return services.AddMcpServer(options =>
        {
            UnityMcpToolBridge.ConfigureServerOptions(options);
        })
        .WithListToolsHandler(bridge.HandleListToolsAsync)
        .WithCallToolHandler(bridge.HandleCallToolAsync);
}

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging.ClearProviders();
    logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
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
