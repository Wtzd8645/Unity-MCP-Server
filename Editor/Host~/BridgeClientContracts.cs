using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcpServer.Host;

public interface IUnityBridgeClient
{
    Task<BridgeToolCallResult> CallToolAsync(
        string toolName,
        JsonObject arguments,
        int? timeoutMsOverride,
        CancellationToken cancellationToken);
}

public sealed record BridgeToolCallResult(
    bool IsError,
    string ContentText,
    JsonNode? StructuredContent);


