using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcp.Gateway;

public sealed class McpServer
{
    private const string ProtocolVersion = "2025-06-18";
    private const string ServerName = "unity-mcp-server";
    private const string ServerVersion = "0.1.0";

    private readonly ToolRegistry _toolRegistry;
    private readonly IUnityControlClient _controlClient;

    private McpServer(ToolRegistry toolRegistry, IUnityControlClient controlClient)
    {
        _toolRegistry = toolRegistry;
        _controlClient = controlClient;
    }

    public static McpServer CreateDefault()
    {
        string mcpRoot = RepositoryPaths.ResolveMcpRoot();
        string? enabledModulesEnv = Environment.GetEnvironmentVariable("UNITY_MCP_ENABLED_MODULES");
        IReadOnlyCollection<string>? enabledModules = ParseEnabledModules(enabledModulesEnv);
        ToolRegistry registry = ToolRegistry.LoadFromSchemas(mcpRoot, enabledModules);
        IUnityControlClient controlClient = UnityControlClientFactory.CreateFromEnvironment();
        return new McpServer(registry, controlClient);
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

            JsonObject? response = await ProcessRequestAsync(message, log, cancellationToken);
            if (response is not null)
            {
                await transport.WriteMessageAsync(response, cancellationToken);
            }
        }
    }

    public async Task<JsonObject?> ProcessRequestAsync(
        JsonObject request,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        string? method = request["method"]?.GetValue<string>();
        bool hasId = request.TryGetPropertyValue("id", out JsonNode? rawId);
        JsonNode? id = hasId ? rawId?.DeepClone() : null;
        bool isNotification = !hasId;

        if (string.IsNullOrWhiteSpace(method))
        {
            return CreateErrorEnvelope(id, -32600, "Invalid Request");
        }

        if (method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return null;
        }

        if (isNotification)
        {
            return null;
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

            return CreateResultEnvelope(id, result);
        }
        catch (JsonRpcException ex)
        {
            return CreateErrorEnvelope(id, ex.Code, ex.Message, ex.ErrorData);
        }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] Unhandled error: {ex}");
            await log.FlushAsync();
            return CreateErrorEnvelope(id, -32603, "Internal error");
        }
    }

    public static JsonObject CreateErrorEnvelope(
        JsonNode? id,
        int code,
        string message,
        JsonObject? data = null)
    {
        var error = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (data is not null)
        {
            error["data"] = data.DeepClone();
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = error,
        };
    }

    private static JsonObject CreateResultEnvelope(JsonNode? id, JsonObject result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result.DeepClone(),
        };
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

        int? timeoutOverride = ResolveControlTimeoutOverride(tool.Name, arguments);
        ControlToolCallResult controlResult = await _controlClient.CallToolAsync(
            tool.Name,
            arguments,
            timeoutOverride,
            cancellationToken);

        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = controlResult.ContentText,
            },
        };

        var result = new JsonObject
        {
            ["isError"] = controlResult.IsError,
            ["content"] = content,
        };

        if (controlResult.StructuredContent is not null)
        {
            result["structuredContent"] = controlResult.StructuredContent.DeepClone();
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

    private static int? ResolveControlTimeoutOverride(string toolName, JsonObject arguments)
    {
        if (string.Equals(toolName, "unity_project_run_tests", StringComparison.Ordinal))
        {
            int timeoutMs = ReadTimeoutMs(arguments, defaultValue: 600000);
            timeoutMs = Math.Clamp(timeoutMs, 5000, 3600000);
            int controlTimeout = timeoutMs + 30000;
            return Math.Clamp(controlTimeout, 5000, 3900000);
        }

        if (string.Equals(toolName, "unity_runtime_start_playmode", StringComparison.Ordinal) ||
            string.Equals(toolName, "unity_runtime_stop_playmode", StringComparison.Ordinal))
        {
            int timeoutMs = ReadTimeoutMs(arguments, defaultValue: 15000);
            timeoutMs = Math.Clamp(timeoutMs, 1000, 120000);
            int controlTimeout = timeoutMs + 5000;
            return Math.Clamp(controlTimeout, 5000, 125000);
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







