using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcpServer.Host;

public sealed class McpServer
{
    private const string ProtocolVersion = "2025-06-18";
    private const string ServerName = "unity-mcp-server";
    private const string ServerVersion = "0.1.0";

    private readonly ToolRegistry _toolRegistry;
    private readonly IUnityBridgeClient _bridgeClient;

    private McpServer(ToolRegistry toolRegistry, IUnityBridgeClient bridgeClient)
    {
        _toolRegistry = toolRegistry;
        _bridgeClient = bridgeClient;
    }

    public static McpServer CreateDefault()
    {
        string mcpRoot = RepositoryPaths.ResolveMcpRoot();
        string? enabledModulesEnv = Environment.GetEnvironmentVariable("UNITY_MCP_ENABLED_MODULES");
        IReadOnlyCollection<string>? enabledModules = ParseEnabledModules(enabledModulesEnv);
        ToolRegistry registry = ToolRegistry.LoadFromSchemas(mcpRoot, enabledModules);
        IUnityBridgeClient bridgeClient = UnityBridgeClientFactory.CreateFromEnvironment();
        return new McpServer(registry, bridgeClient);
    }

    public async Task RunAsync(
        Stream stdin,
        Stream stdout,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        var transport = new StdioJsonRpcTransport(stdin, stdout);
        await log.WriteLineAsync(
            $"[{DateTimeOffset.UtcNow:O}] {ServerName} starting. " +
            $"Enabled modules: {string.Join(", ", _toolRegistry.EnabledModules)}");
        await log.FlushAsync();

        while (!cancellationToken.IsCancellationRequested)
        {
            JsonObject? message = await transport.ReadMessageAsync(cancellationToken);
            if (message is null)
            {
                break;
            }

            await HandleMessageAsync(message, transport, log, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(
        JsonObject request,
        StdioJsonRpcTransport transport,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        string? method = request["method"]?.GetValue<string>();
        JsonNode? id = request["id"]?.DeepClone();
        bool isNotification = id is null;

        if (string.IsNullOrWhiteSpace(method))
        {
            if (!isNotification)
            {
                await transport.WriteErrorAsync(id, -32600, "Invalid Request", cancellationToken);
            }

            return;
        }

        if (method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return;
        }

        if (isNotification)
        {
            return;
        }

        try
        {
            JsonObject result;
            if (method == "initialize")
            {
                result = HandleInitialize();
            }
            else if (method == "ping")
            {
                result = new JsonObject();
            }
            else if (method == "tools/list")
            {
                result = HandleToolsList();
            }
            else if (method == "tools/call")
            {
                result = await HandleToolsCallAsync(
                    request["params"] as JsonObject,
                    cancellationToken);
            }
            else
            {
                throw JsonRpcException.MethodNotFound($"Unsupported method '{method}'.");
            }

            await transport.WriteResultAsync(id, result, cancellationToken);
        }
        catch (JsonRpcException ex)
        {
            await transport.WriteErrorAsync(id, ex.Code, ex.Message, cancellationToken, ex.ErrorData);
        }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] Unhandled error: {ex}");
            await log.FlushAsync();
            await transport.WriteErrorAsync(id, -32603, "Internal error", cancellationToken);
        }
    }

    private JsonObject HandleInitialize()
    {
        return new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
            },
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false,
                },
            },
        };
    }

    private JsonObject HandleToolsList()
    {
        var tools = new JsonArray();
        foreach (ToolDefinition tool in _toolRegistry.Tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            tools.Add(
                new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["inputSchema"] = tool.InputSchema.DeepClone(),
                });
        }

        return new JsonObject
        {
            ["tools"] = tools,
        };
    }

    private async Task<JsonObject> HandleToolsCallAsync(
        JsonObject? parameters,
        CancellationToken cancellationToken)
    {
        if (parameters is null)
        {
            throw JsonRpcException.InvalidParams("tools/call requires object params.");
        }

        string? toolName = parameters["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw JsonRpcException.InvalidParams("tools/call params.name is required.");
        }

        if (!_toolRegistry.TryGetTool(toolName, out ToolDefinition tool))
        {
            throw JsonRpcException.InvalidParams($"Unknown tool '{toolName}'.");
        }

        JsonObject arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
        if (!InputSchemaValidator.TryValidate(tool.InputSchema, arguments, out string validationError))
        {
            throw JsonRpcException.InvalidParams(
                $"Invalid arguments for '{tool.Name}': {validationError}");
        }

        int? timeoutOverride = ResolveBridgeTimeoutOverride(tool.Name, arguments);
        BridgeToolCallResult bridgeResult = await _bridgeClient.CallToolAsync(
            tool.Name,
            arguments,
            timeoutOverride,
            cancellationToken);

        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = bridgeResult.ContentText,
            },
        };

        var result = new JsonObject
        {
            ["isError"] = bridgeResult.IsError,
            ["content"] = content,
        };

        if (bridgeResult.StructuredContent is not null)
        {
            result["structuredContent"] = bridgeResult.StructuredContent.DeepClone();
        }

        return result;
    }

    private static IReadOnlyCollection<string>? ParseEnabledModules(string? enabledModulesEnv)
    {
        if (string.IsNullOrWhiteSpace(enabledModulesEnv))
        {
            return null;
        }

        var modules = enabledModulesEnv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return modules.Length == 0 ? null : modules;
    }

    private static int? ResolveBridgeTimeoutOverride(string toolName, JsonObject arguments)
    {
        if (string.Equals(toolName, "unity_run_tests", StringComparison.Ordinal))
        {
            int timeoutMs = ReadTimeoutMs(arguments, defaultValue: 600000);
            timeoutMs = Math.Clamp(timeoutMs, 5000, 3600000);
            int bridgeTimeout = timeoutMs + 30000;
            return Math.Clamp(bridgeTimeout, 5000, 3900000);
        }

        if (string.Equals(toolName, "unity_playmode_start", StringComparison.Ordinal) ||
            string.Equals(toolName, "unity_playmode_stop", StringComparison.Ordinal))
        {
            int timeoutMs = ReadTimeoutMs(arguments, defaultValue: 15000);
            timeoutMs = Math.Clamp(timeoutMs, 1000, 120000);
            int bridgeTimeout = timeoutMs + 5000;
            return Math.Clamp(bridgeTimeout, 5000, 125000);
        }

        return null;
    }

    private static int ReadTimeoutMs(JsonObject arguments, int defaultValue)
    {
        int timeoutMs = defaultValue;
        JsonNode? rawTimeout = arguments["timeoutMs"];
        if (rawTimeout is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out int parsedInt))
            {
                timeoutMs = parsedInt;
            }
            else if (jsonValue.TryGetValue<long>(out long parsedLong))
            {
                timeoutMs = (int)Math.Clamp(parsedLong, int.MinValue, int.MaxValue);
            }
        }

        return timeoutMs;
    }
}






