using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Blanketmen.UnityMcp.Gateway
{
    public sealed class UnityMcpToolBridge
    {
        private const string ServerName = "unity-mcp-server";
        private const string ServerVersion = "0.1.0";
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly ToolRegistry _toolRegistry;
        private readonly IUnityControlClient _controlClient;

        private UnityMcpToolBridge(ToolRegistry toolRegistry, IUnityControlClient controlClient)
        {
            _toolRegistry = toolRegistry;
            _controlClient = controlClient;
        }

        public static UnityMcpToolBridge CreateDefault()
        {
            string mcpRoot = RepositoryPaths.ResolveMcpRoot();
            string? enabledModulesEnv = Environment.GetEnvironmentVariable("UNITY_MCP_ENABLED_MODULES");
            IReadOnlyCollection<string>? enabledModules = ParseEnabledModules(enabledModulesEnv);
            ToolRegistry registry = ToolRegistry.LoadFromSchemas(mcpRoot, enabledModules);
            IUnityControlClient controlClient = UnityControlClientFactory.CreateFromEnvironment();
            return new UnityMcpToolBridge(registry, controlClient);
        }

        public static void ConfigureServerOptions(McpServerOptions options)
        {
            options.ServerInfo = new Implementation
            {
                Name = ServerName,
                Version = ServerVersion,
            };
            options.Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability
                {
                    ListChanged = false,
                },
            };
        }

        public ValueTask<ListToolsResult> HandleListToolsAsync(
            RequestContext<ListToolsRequestParams> _,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tools = _toolRegistry.Tools
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(tool => new Tool
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = ToJsonElement(tool.InputSchema),
                })
                .ToArray();

            return ValueTask.FromResult(new ListToolsResult
            {
                Tools = tools,
            });
        }

        public async ValueTask<CallToolResult> HandleCallToolAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken)
        {
            CallToolRequestParams parameters = request.Params ??
                throw InvalidParams("tools/call requires object params.");

            string? toolName = parameters.Name;
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw InvalidParams("tools/call params.name is required.");
            }

            if (!_toolRegistry.TryGetTool(toolName, out ToolDefinition tool))
            {
                throw InvalidParams($"Unknown tool '{toolName}'.");
            }

            JsonObject arguments = ToJsonObject(parameters.Arguments);
            if (!InputSchemaValidator.TryValidate(tool.InputSchema, arguments, out string validationError))
            {
                throw InvalidParams($"Invalid arguments for '{tool.Name}': {validationError}");
            }

            EnsureInternalRunTestsId(tool.Name, arguments);

            int? timeoutOverride = ResolveControlTimeoutOverride(tool.Name, arguments);
            ControlToolCallResult controlResult = await _controlClient.CallToolAsync(
                tool.Name,
                arguments,
                timeoutOverride,
                cancellationToken);

            return new CallToolResult
            {
                IsError = controlResult.IsError,
                Content =
                [
                    new TextContentBlock
                    {
                        Text = controlResult.ContentText,
                    },
                ],
                StructuredContent = controlResult.StructuredContent is null
                    ? null
                    : ToJsonElement(controlResult.StructuredContent),
            };
        }

        private static McpProtocolException InvalidParams(string message)
        {
            return new McpProtocolException(message, McpErrorCode.InvalidParams);
        }

        private static IReadOnlyCollection<string>? ParseEnabledModules(string? enabledModulesEnv)
        {
            if (string.IsNullOrWhiteSpace(enabledModulesEnv))
            {
                return null;
            }

            string[] modules = enabledModulesEnv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return modules.Length == 0 ? null : modules;
        }

        private static JsonObject ToJsonObject(IDictionary<string, JsonElement>? arguments)
        {
            var result = new JsonObject();
            if (arguments is null)
            {
                return result;
            }

            foreach ((string key, JsonElement value) in arguments)
            {
                result[key] = JsonNode.Parse(value.GetRawText());
            }

            return result;
        }

        private static JsonElement ToJsonElement(JsonNode node)
        {
            return JsonSerializer.SerializeToElement(node, SerializerOptions);
        }

        private static void EnsureInternalRunTestsId(string toolName, JsonObject arguments)
        {
            if (!string.Equals(toolName, "unity_project_run_tests", StringComparison.Ordinal))
            {
                return;
            }

            string? runId = TryGetString(arguments["runId"]);
            if (string.IsNullOrWhiteSpace(runId))
            {
                arguments["runId"] = Guid.NewGuid().ToString("N");
            }
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

            if (string.Equals(toolName, "unity_project_switch_build_target", StringComparison.Ordinal))
            {
                int timeoutMs = ReadTimeoutMs(arguments, defaultValue: 600000);
                timeoutMs = Math.Clamp(timeoutMs, 5000, 3600000);
                int controlTimeout = timeoutMs + 30000;
                return Math.Clamp(controlTimeout, 5000, 3900000);
            }

            if (string.Equals(toolName, "unity_project_build_player", StringComparison.Ordinal))
            {
                int timeoutMs = ReadTimeoutMs(arguments, defaultValue: 1800000);
                timeoutMs = Math.Clamp(timeoutMs, 10000, 7200000);
                int controlTimeout = timeoutMs + 30000;
                return Math.Clamp(controlTimeout, 10000, 7230000);
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

        private static string? TryGetString(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out string? result))
            {
                return result;
            }

            return null;
        }
    }
}
