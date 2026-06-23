using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Blanketmen.UnityMcp.Gateway
{
    public sealed class ControlCallCoordinator : IUnityControlClient
    {
        private const int MinTimeoutMs = 500;
        private const int MaxTimeoutMs = 7_230_000;
        private readonly IUnityControlTransportClient _transport;
        private readonly int _defaultTimeoutMs;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public ControlCallCoordinator(IUnityControlTransportClient transport, int defaultTimeoutMs)
        {
            _transport = transport;
            _defaultTimeoutMs = Math.Clamp(defaultTimeoutMs, MinTimeoutMs, MaxTimeoutMs);
        }

        public async Task<ControlToolCallResult> CallToolAsync(
            string toolName,
            JsonObject arguments,
            int? timeoutMsOverride,
            CancellationToken cancellationToken)
        {
            ToolExecutionPolicy policy = ToolExecutionPolicyRegistry.Resolve(toolName);
            int timeoutMs = Math.Clamp(timeoutMsOverride ?? _defaultTimeoutMs, MinTimeoutMs, MaxTimeoutMs);
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
            var stopwatch = Stopwatch.StartNew();

            var request = new UnityControlToolCallRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Name = toolName,
                ArgumentsJson = arguments.ToJsonString(),
                DeadlineUtc = deadline.UtcDateTime.ToString("O"),
                ProtocolVersion = ControlProtocol.Version,
                Durable = policy.Durable,
            };

            ControlTransportResult? lastFailure = null;
            while (!cancellationToken.IsCancellationRequested && RemainingMs(deadline) > 0)
            {
                request.Attempt++;
                ControlTransportResult sendResult = await _transport.SendAsync(
                    request,
                    connectTimeoutMs: Math.Min(5000, RemainingMs(deadline)),
                    totalTimeoutMs: RemainingMs(deadline),
                    cancellationToken);

                if (!sendResult.IsTransportError && sendResult.Response is not null)
                {
                    return BuildCallResult(sendResult.Response, request, policy, stopwatch);
                }

                lastFailure = sendResult;
                if (IsPreSendFailure(sendResult))
                {
                    await DelayBeforeRetryAsync(request.Attempt, deadline, cancellationToken);
                    continue;
                }

                ControlToolCallResult? recovered = await RecoverAfterUncertainSendAsync(
                    request,
                    arguments,
                    policy,
                    deadline,
                    stopwatch,
                    sendResult,
                    cancellationToken);

                if (recovered is not null)
                {
                    return recovered;
                }

                await DelayBeforeRetryAsync(request.Attempt, deadline, cancellationToken);
            }

            return BuildErrorResult(
                "tool_recovery_timeout",
                "Timed out while waiting for Unity Control to complete or recover the tool request.",
                request,
                policy,
                stopwatch,
                lastFailure);
        }

        private async Task<ControlToolCallResult?> RecoverAfterUncertainSendAsync(
            UnityControlToolCallRequest originalRequest,
            JsonObject originalArguments,
            ToolExecutionPolicy policy,
            DateTimeOffset deadline,
            Stopwatch stopwatch,
            ControlTransportResult failure,
            CancellationToken cancellationToken)
        {
            if (!policy.QueryRequestLedger)
            {
                return policy.CanReplayAfterUnknown ? null : BuildErrorResult(
                    failure.Status,
                    failure.Message,
                    originalRequest,
                    policy,
                    stopwatch,
                    failure);
            }

            JsonObject? lastState = null;
            while (!cancellationToken.IsCancellationRequested && RemainingMs(deadline) > 0)
            {
                await DelayBeforeRetryAsync(originalRequest.Attempt, deadline, cancellationToken);

                JsonObject? state = await QueryRequestStateAsync(
                    originalRequest.RequestId,
                    deadline,
                    cancellationToken);

                if (state is null)
                {
                    continue;
                }

                lastState = state;
                string requestStatus = TryGetString(state["requestStatus"]) ??
                                       TryGetString(state["status"]) ??
                                       "unknown";

                if (string.Equals(requestStatus, "completed", StringComparison.Ordinal) ||
                    string.Equals(requestStatus, "failed", StringComparison.Ordinal))
                {
                    string? responseJson = TryGetString(state["responseJson"]);
                    UnityControlToolCallResponse? response = ParseResponse(responseJson);
                    if (response is not null)
                    {
                        return BuildCallResult(response, originalRequest, policy, stopwatch, recovered: true);
                    }

                    return BuildErrorResult(
                        "tool_recovery_invalid_response",
                        "Unity Control recorded a terminal request state without a valid response payload.",
                        originalRequest,
                        policy,
                        stopwatch,
                        failure,
                        lastState);
                }

                if (string.Equals(requestStatus, "received", StringComparison.Ordinal) ||
                    string.Equals(requestStatus, "queued_main_thread", StringComparison.Ordinal) ||
                    string.Equals(requestStatus, "executing", StringComparison.Ordinal))
                {
                    continue;
                }

                if (policy.Strategy == ToolRecoveryStrategy.StatefulLongRunning)
                {
                    ControlToolCallResult? testStateResult = await TryRecoverTestRunStateAsync(
                        originalRequest,
                        originalArguments,
                        policy,
                        deadline,
                        stopwatch,
                        failure,
                        cancellationToken);
                    if (testStateResult is not null)
                    {
                        return testStateResult;
                    }
                }

                if (policy.CanReplayAfterUnknown)
                {
                    return null;
                }

                return BuildErrorResult(
                    "tool_result_unknown_after_disconnect",
                    "Unity Control lost the response for a non-replayable tool after the request may have executed.",
                    originalRequest,
                    policy,
                    stopwatch,
                    failure,
                    lastState);
            }

            return BuildErrorResult(
                "tool_recovery_timeout",
                "Timed out while querying Unity Control request state after the transport disconnected.",
                originalRequest,
                policy,
                stopwatch,
                failure,
                lastState);
        }

        private async Task<ControlToolCallResult?> TryRecoverTestRunStateAsync(
            UnityControlToolCallRequest originalRequest,
            JsonObject originalArguments,
            ToolExecutionPolicy policy,
            DateTimeOffset deadline,
            Stopwatch stopwatch,
            ControlTransportResult failure,
            CancellationToken cancellationToken)
        {
            string? runId = TryGetString(originalArguments["runId"]);
            if (string.IsNullOrWhiteSpace(runId))
            {
                return BuildErrorResult(
                    "test_run_recovery_failed",
                    "Cannot recover test run because runId was not assigned.",
                    originalRequest,
                    policy,
                    stopwatch,
                    failure);
            }

            var queryArgs = new JsonObject
            {
                ["runId"] = runId,
            };

            UnityControlToolCallResponse? response = await SendInternalRequestAsync(
                ControlProtocol.GetTestRunStateToolName,
                queryArgs,
                deadline,
                cancellationToken);

            JsonObject? state = ParseStructuredContent(response?.StructuredContentJson) as JsonObject;
            if (state is null)
            {
                return null;
            }

            string status = TryGetString(state["status"]) ?? "unknown";
            if (string.Equals(status, "completed", StringComparison.Ordinal))
            {
                string? resultJson = TryGetString(state["resultJson"]);
                JsonNode? resultNode = ParseStructuredContent(resultJson);
                if (resultNode is JsonObject resultObject)
                {
                    if (resultObject["artifacts"] is JsonObject artifacts)
                    {
                        artifacts["isRecovered"] = true;
                    }
                    else
                    {
                        resultObject["artifacts"] = new JsonObject
                        {
                            ["isRecovered"] = true,
                        };
                    }

                    return new ControlToolCallResult(
                        IsError: false,
                        ContentText: "unity_project_run_tests completed.",
                        StructuredContent: resultObject);
                }
            }

            if (string.Equals(status, "failed", StringComparison.Ordinal) ||
                string.Equals(status, "expired", StringComparison.Ordinal))
            {
                return BuildErrorResult(
                    "test_run_recovery_failed",
                    TryGetString(state["message"]) ?? "Recovered test run failed.",
                    originalRequest,
                    policy,
                    stopwatch,
                    failure,
                    state);
            }

            return null;
        }

        private async Task<JsonObject?> QueryRequestStateAsync(
            string requestId,
            DateTimeOffset deadline,
            CancellationToken cancellationToken)
        {
            var queryArgs = new JsonObject
            {
                ["requestId"] = requestId,
            };

            UnityControlToolCallResponse? response = await SendInternalRequestAsync(
                ControlProtocol.GetRequestStateToolName,
                queryArgs,
                deadline,
                cancellationToken);

            return ParseStructuredContent(response?.StructuredContentJson) as JsonObject;
        }

        private async Task<UnityControlToolCallResponse?> SendInternalRequestAsync(
            string toolName,
            JsonObject arguments,
            DateTimeOffset deadline,
            CancellationToken cancellationToken)
        {
            if (RemainingMs(deadline) <= 0)
            {
                return null;
            }

            var request = new UnityControlToolCallRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Name = toolName,
                ArgumentsJson = arguments.ToJsonString(),
                DeadlineUtc = deadline.UtcDateTime.ToString("O"),
                ProtocolVersion = ControlProtocol.Version,
                Durable = false,
                Attempt = 1,
            };

            ControlTransportResult result = await _transport.SendAsync(
                request,
                connectTimeoutMs: Math.Min(5000, RemainingMs(deadline)),
                totalTimeoutMs: Math.Min(7000, RemainingMs(deadline)),
                cancellationToken);

            return result.IsTransportError ? null : result.Response;
        }

        private ControlToolCallResult BuildCallResult(
            UnityControlToolCallResponse response,
            UnityControlToolCallRequest request,
            ToolExecutionPolicy policy,
            Stopwatch stopwatch,
            bool recovered = false)
        {
            if (!string.Equals(response.ProtocolVersion, ControlProtocol.Version, StringComparison.Ordinal))
            {
                return BuildErrorResult(
                    "control_protocol_mismatch",
                    "Unity Control protocol is not compatible with this Gateway. Restart Unity and Gateway so both sides use the same package version.",
                    request,
                    policy,
                    stopwatch,
                    null);
            }

            JsonNode? structured = ParseStructuredContent(response.StructuredContentJson);
            if (structured is JsonObject obj)
            {
                obj["requestId"] = response.RequestId ?? request.RequestId;
                obj["requestStatus"] = response.RequestStatus;
                obj["controlEpoch"] = response.ControlEpoch;
                if (recovered)
                {
                    obj["isRecovered"] = true;
                }
            }

            return new ControlToolCallResult(
                IsError: response.IsError,
                ContentText: string.IsNullOrWhiteSpace(response.ContentText)
                    ? $"Tool '{request.Name}' completed."
                    : response.ContentText!,
                StructuredContent: structured);
        }

        private ControlToolCallResult BuildErrorResult(
            string status,
            string message,
            UnityControlToolCallRequest request,
            ToolExecutionPolicy policy,
            Stopwatch stopwatch,
            ControlTransportResult? failure,
            JsonObject? state = null)
        {
            var payload = new JsonObject
            {
                ["status"] = status,
                ["message"] = message,
                ["toolName"] = request.Name,
                ["requestId"] = request.RequestId,
                ["transport"] = failure?.Transport ?? _transport.TransportName,
                ["transportPhase"] = failure?.TransportPhase,
                ["attempt"] = request.Attempt,
                ["recoveryPolicy"] = policy.Strategy.ToString(),
                ["elapsedMs"] = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
            };

            if (state is not null)
            {
                payload["requestState"] = state.DeepClone();
            }

            return new ControlToolCallResult(
                IsError: true,
                ContentText: message,
                StructuredContent: payload);
        }

        private static bool IsPreSendFailure(ControlTransportResult failure)
        {
            return string.Equals(failure.TransportPhase, "connect", StringComparison.Ordinal);
        }

        private static int RemainingMs(DateTimeOffset deadline)
        {
            double remaining = (deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
            if (remaining <= 0)
            {
                return 0;
            }

            return (int)Math.Min(int.MaxValue, remaining);
        }

        private static async Task DelayBeforeRetryAsync(
            int attempt,
            DateTimeOffset deadline,
            CancellationToken cancellationToken)
        {
            int delayMs = Math.Min(1000, 150 + attempt * 100);
            delayMs = Math.Min(delayMs, RemainingMs(deadline));
            if (delayMs <= 0)
            {
                return;
            }

            try
            {
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static UnityControlToolCallResponse? ParseResponse(string? rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<UnityControlToolCallResponse>(rawJson, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static JsonNode? ParseStructuredContent(string? rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(rawJson);
            }
            catch
            {
                return new JsonObject
                {
                    ["raw"] = rawJson,
                };
            }
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
