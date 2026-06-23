using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Blanketmen.UnityMcp.Gateway
{
    public static class ControlProtocol
    {
        public const string Version = "2";
        public const string GetStatusToolName = "__unity_control_get_status";
        public const string GetRequestStateToolName = "__unity_control_get_request_state";
        public const string GetTestRunStateToolName = "__unity_project_get_test_run_state";
    }

    public interface IUnityControlClient
    {
        Task<ControlToolCallResult> CallToolAsync(
            string toolName,
            JsonObject arguments,
            int? timeoutMsOverride,
            CancellationToken cancellationToken);
    }

    public interface IUnityControlTransportClient
    {
        string TransportName { get; }

        Task<ControlTransportResult> SendAsync(
            UnityControlToolCallRequest request,
            int connectTimeoutMs,
            int totalTimeoutMs,
            CancellationToken cancellationToken);
    }

    public sealed record ControlToolCallResult(
        bool IsError,
        string ContentText,
        JsonNode? StructuredContent);

    public sealed record ControlTransportResult(
        bool IsTransportError,
        UnityControlToolCallResponse? Response,
        string Status,
        string Transport,
        string TransportPhase,
        string Message,
        JsonNode? StructuredContent = null);

    public sealed class UnityControlToolCallRequest
    {
        public string RequestId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ArgumentsJson { get; set; } = "{}";
        public string DeadlineUtc { get; set; } = string.Empty;
        public int Attempt { get; set; }
        public string ProtocolVersion { get; set; } = ControlProtocol.Version;
        public bool Durable { get; set; }
    }

    public sealed class UnityControlToolCallResponse
    {
        public bool IsError { get; set; }
        public string? ContentText { get; set; }
        public string? StructuredContentJson { get; set; }
        public string? RequestId { get; set; }
        public string? ControlEpoch { get; set; }
        public string? RequestStatus { get; set; }
        public string? ProtocolVersion { get; set; }
    }
}
