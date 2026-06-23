using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Blanketmen.UnityMcp.Gateway
{
    public sealed class HttpUnityControlClient : IUnityControlTransportClient
    {
        private readonly HttpClient _httpClient;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public HttpUnityControlClient(Uri baseUri)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = baseUri,
            };
        }

        public string TransportName => "http";

        public async Task<ControlTransportResult> SendAsync(
            UnityControlToolCallRequest request,
            int connectTimeoutMs,
            int totalTimeoutMs,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(totalTimeoutMs, 1000));

            string phase = "connect";
            try
            {
                phase = "write";
                using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                    "mcp/tool/call",
                    request,
                    JsonOptions,
                    timeoutCts.Token);

                phase = "read";
                if (!response.IsSuccessStatusCode)
                {
                    string message = $"Control HTTP status {(int)response.StatusCode} ({response.StatusCode}).";
                    return TransportFailure("control_http_error", message, phase, request, (int)response.StatusCode);
                }

                phase = "parse";
                UnityControlToolCallResponse? controlResponse =
                    await response.Content.ReadFromJsonAsync<UnityControlToolCallResponse>(
                        JsonOptions,
                        timeoutCts.Token);
                if (controlResponse is null)
                {
                    return TransportFailure(
                        "control_invalid_response",
                        "Control returned empty response.",
                        phase,
                        request);
                }

                return new ControlTransportResult(
                    IsTransportError: false,
                    Response: controlResponse,
                    Status: controlResponse.IsError ? "tool_error" : "ok",
                    Transport: TransportName,
                    TransportPhase: "complete",
                    Message: controlResponse.ContentText ?? string.Empty);
            }
            catch (Exception ex)
            {
                string status = string.Equals(phase, "parse", StringComparison.Ordinal)
                    ? "control_invalid_response"
                    : "control_unreachable";
                return TransportFailure(status, $"Control HTTP request failed: {ex.Message}", phase, request);
            }
        }

        private ControlTransportResult TransportFailure(
            string status,
            string message,
            string phase,
            UnityControlToolCallRequest request,
            int? statusCode = null)
        {
            var payload = new JsonObject
            {
                ["status"] = status,
                ["message"] = message,
                ["transport"] = TransportName,
                ["transportPhase"] = phase,
                ["toolName"] = request.Name,
                ["requestId"] = request.RequestId,
                ["attempt"] = request.Attempt,
            };

            if (statusCode.HasValue)
            {
                payload["statusCode"] = statusCode.Value;
            }

            return new ControlTransportResult(
                IsTransportError: true,
                Response: null,
                Status: status,
                Transport: TransportName,
                TransportPhase: phase,
                Message: message,
                StructuredContent: payload);
        }
    }
}
