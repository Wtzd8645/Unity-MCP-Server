using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcp.Gateway;

public interface IUnityControlClient
{
    Task<ControlToolCallResult> CallToolAsync(
        string toolName,
        JsonObject arguments,
        int? timeoutMsOverride,
        CancellationToken cancellationToken);
}

public sealed record ControlToolCallResult(
    bool IsError,
    string ContentText,
    JsonNode? StructuredContent);


