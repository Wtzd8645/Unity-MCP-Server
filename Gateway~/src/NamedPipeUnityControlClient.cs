using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Blanketmen.UnityMcp.Gateway
{
    public sealed class NamedPipeUnityControlClient : IUnityControlTransportClient
    {
        private readonly string _pipeName;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public NamedPipeUnityControlClient(string pipeName)
        {
            _pipeName = pipeName;
        }

        public string TransportName => "pipe";

        public async Task<ControlTransportResult> SendAsync(
            UnityControlToolCallRequest request,
            int connectTimeoutMs,
            int totalTimeoutMs,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(totalTimeoutMs, 1000));

            NamedPipeClientStream? pipe = null;
            string phase = "connect";
            try
            {
                pipe = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: _pipeName,
                    direction: PipeDirection.InOut,
                    options: PipeOptions.Asynchronous);

                await pipe.ConnectAsync(Math.Max(connectTimeoutMs, 100), timeoutCts.Token);

                phase = "write";
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
                {
                    AutoFlush = true,
                };
                string serializedRequest = JsonSerializer.Serialize(request, JsonOptions);
                await writer.WriteLineAsync(serializedRequest);

                phase = "read";
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
                string? rawResponse = await reader.ReadLineAsync().WaitAsync(timeoutCts.Token);
                if (string.IsNullOrWhiteSpace(rawResponse))
                {
                    return TransportFailure(
                        "control_invalid_response",
                        "Control pipe returned empty response.",
                        phase,
                        request);
                }

                phase = "parse";
                UnityControlToolCallResponse? response =
                    JsonSerializer.Deserialize<UnityControlToolCallResponse>(rawResponse, JsonOptions);
                if (response is null)
                {
                    return TransportFailure(
                        "control_invalid_response",
                        "Control pipe returned invalid JSON response.",
                        phase,
                        request);
                }

                return new ControlTransportResult(
                    IsTransportError: false,
                    Response: response,
                    Status: response.IsError ? "tool_error" : "ok",
                    Transport: TransportName,
                    TransportPhase: "complete",
                    Message: response.ContentText ?? string.Empty);
            }
            catch (Exception ex)
            {
                string status = string.Equals(phase, "parse", StringComparison.Ordinal)
                    ? "control_invalid_response"
                    : "control_unreachable";
                return TransportFailure(status, $"Control pipe request failed: {ex.Message}", phase, request);
            }
            finally
            {
                try { pipe?.Dispose(); } catch { }
            }
        }

        private ControlTransportResult TransportFailure(
            string status,
            string message,
            string phase,
            UnityControlToolCallRequest request)
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
