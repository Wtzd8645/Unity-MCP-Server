using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcp.Gateway;

public sealed class NamedPipeUnityControlClient : IUnityControlClient
{
    private const int ResponseGraceMs = 1000;
    private const string RunTestsToolName = "unity_project_run_tests";
    private const string GetTestRunStateToolName = "__unity_project_get_test_run_state";
    private readonly string _pipeName;
    private readonly int _timeoutMs;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public NamedPipeUnityControlClient(string pipeName, int timeoutMs)
    {
        _pipeName = pipeName;
        _timeoutMs = timeoutMs;
    }

    public async Task<ControlToolCallResult> CallToolAsync(
        string toolName,
        JsonObject arguments,
        int? timeoutMsOverride,
        CancellationToken cancellationToken)
    {
        int timeoutMs = timeoutMsOverride ?? _timeoutMs;

        var request = new UnityControlToolCallRequest
        {
            Name = toolName,
            ArgumentsJson = arguments.ToJsonString(),
        };

        ControlToolCallResult result = await SendRequestAsync(
            request,
            timeoutMs,
            timeoutMs + ResponseGraceMs,
            cancellationToken);

        if (IsRecoverableRunTestsFailure(toolName, result))
        {
            return await RecoverRunTestsAsync(arguments, timeoutMs, cancellationToken);
        }

        return result;
    }

    private async Task<ControlToolCallResult> SendRequestAsync(
        UnityControlToolCallRequest request,
        int connectTimeoutMs,
        int totalTimeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Math.Max(totalTimeoutMs, 1000));

        try
        {
            using var pipe = new NamedPipeClientStream(
                serverName: ".",
                pipeName: _pipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

            await pipe.ConnectAsync(Math.Max(connectTimeoutMs, 100), timeoutCts.Token);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);

            string serializedRequest = JsonSerializer.Serialize(request, JsonOptions);
            await writer.WriteLineAsync(serializedRequest);

            Task<string?> readTask = reader.ReadLineAsync();
            string? rawResponse = await readTask.WaitAsync(timeoutCts.Token);

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return new ControlToolCallResult(
                    IsError: true,
                    ContentText: "Control pipe returned empty response.",
                    StructuredContent: new JsonObject
                    {
                        ["status"] = "control_invalid_response",
                        ["transport"] = "pipe",
                    });
            }

            UnityControlToolCallResponse? response =
                JsonSerializer.Deserialize<UnityControlToolCallResponse>(rawResponse, JsonOptions);

            if (response is null)
            {
                return new ControlToolCallResult(
                    IsError: true,
                    ContentText: "Control pipe returned invalid JSON response.",
                    StructuredContent: new JsonObject
                    {
                        ["status"] = "control_invalid_response",
                        ["transport"] = "pipe",
                    });
            }

            return new ControlToolCallResult(
                IsError: response.IsError,
                ContentText: string.IsNullOrWhiteSpace(response.ContentText)
                    ? $"Tool '{request.Name}' completed."
                    : response.ContentText!,
                StructuredContent: ParseStructuredContent(response.StructuredContentJson));
        }
        catch (Exception ex)
        {
            return new ControlToolCallResult(
                IsError: true,
                ContentText: $"Control pipe request failed: {ex.Message}",
                StructuredContent: new JsonObject
                {
                    ["status"] = "control_unreachable",
                    ["transport"] = "pipe",
                });
        }
    }

    private async Task<ControlToolCallResult> RecoverRunTestsAsync(
        JsonObject originalArguments,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        string? runId = TryGetString(originalArguments["runId"]);
        string? mode = TryGetString(originalArguments["mode"]) ?? "EditMode";
        if (string.IsNullOrWhiteSpace(runId))
        {
            return BuildRecoveryError(
                "test_run_recovery_failed",
                "Cannot recover test run because runId was not assigned.",
                runId,
                mode,
                null);
        }

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(timeoutMs, 5000));
        JsonObject? lastState = null;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var queryArgs = new JsonObject
            {
                ["runId"] = runId,
            };

            var queryRequest = new UnityControlToolCallRequest
            {
                Name = GetTestRunStateToolName,
                ArgumentsJson = queryArgs.ToJsonString(),
            };

            ControlToolCallResult queryResult = await SendRequestAsync(
                queryRequest,
                connectTimeoutMs: 5000,
                totalTimeoutMs: 7000,
                cancellationToken);

            if (IsRecoverableTransportFailure(queryResult))
            {
                continue;
            }

            if (queryResult.StructuredContent is not JsonObject state)
            {
                continue;
            }

            lastState = state;
            string? status = TryGetString(state["status"]);
            if (string.Equals(status, "completed", StringComparison.Ordinal))
            {
                string? resultJson = TryGetString(state["resultJson"]);
                JsonNode? resultNode = ParseStructuredContent(resultJson);
                if (resultNode is JsonObject resultObject)
                {
                    MarkResultRecovered(resultObject);
                    return new ControlToolCallResult(
                        IsError: false,
                        ContentText: "unity_project_run_tests completed.",
                        StructuredContent: resultObject);
                }

                return BuildRecoveryError(
                    "test_run_recovery_invalid_result",
                    "Recovered test run completed but did not include a valid result payload.",
                    runId,
                    mode,
                    state);
            }

            if (string.Equals(status, "failed", StringComparison.Ordinal))
            {
                return BuildRecoveryError(
                    "test_run_recovery_failed",
                    TryGetString(state["message"]) ?? "Recovered test run failed.",
                    runId,
                    mode,
                    state);
            }

            if (string.Equals(status, "unknown", StringComparison.Ordinal))
            {
                return BuildRecoveryError(
                    "test_run_recovery_unknown",
                    "Recovered test run state was not found.",
                    runId,
                    mode,
                    state);
            }
        }

        return BuildRecoveryError(
            "test_run_recovery_timeout",
            "Timed out while recovering test run after the control pipe disconnected.",
            runId,
            mode,
            lastState);
    }

    private static bool IsRecoverableRunTestsFailure(string toolName, ControlToolCallResult result)
    {
        return string.Equals(toolName, RunTestsToolName, StringComparison.Ordinal) &&
               IsRecoverableTransportFailure(result);
    }

    private static bool IsRecoverableTransportFailure(ControlToolCallResult result)
    {
        if (!result.IsError)
        {
            return false;
        }

        string? status = TryGetStatus(result.StructuredContent);
        return string.Equals(status, "control_invalid_response", StringComparison.Ordinal) ||
               string.Equals(status, "control_unreachable", StringComparison.Ordinal);
    }

    private static ControlToolCallResult BuildRecoveryError(
        string status,
        string message,
        string? runId,
        string? mode,
        JsonObject? state)
    {
        var payload = new JsonObject
        {
            ["status"] = status,
            ["message"] = message,
            ["runId"] = runId,
            ["mode"] = mode,
        };

        CopyIfPresent(state, payload, "statePath");
        CopyIfPresent(state, payload, "xmlReportPath");
        CopyIfPresent(state, payload, "isRecovered");

        return new ControlToolCallResult(
            IsError: true,
            ContentText: message,
            StructuredContent: payload);
    }

    private static void MarkResultRecovered(JsonObject resultObject)
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
    }

    private static void CopyIfPresent(JsonObject? source, JsonObject target, string propertyName)
    {
        if (source is null || !source.TryGetPropertyValue(propertyName, out JsonNode? value))
        {
            return;
        }

        target[propertyName] = value?.DeepClone();
    }

    private static string? TryGetStatus(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            return TryGetString(obj["status"]);
        }

        return null;
    }

    private static string? TryGetString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out string? result))
        {
            return result;
        }

        return null;
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
}



