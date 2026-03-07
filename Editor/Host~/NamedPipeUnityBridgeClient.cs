using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcpServer.Host;

public sealed class NamedPipeUnityBridgeClient : IUnityBridgeClient
{
    private readonly string _pipeName;
    private readonly int _timeoutMs;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public NamedPipeUnityBridgeClient(string pipeName, int timeoutMs)
    {
        _pipeName = pipeName;
        _timeoutMs = timeoutMs;
    }

    public async Task<BridgeToolCallResult> CallToolAsync(
        string toolName,
        JsonObject arguments,
        int? timeoutMsOverride,
        CancellationToken cancellationToken)
    {
        int timeoutMs = timeoutMsOverride ?? _timeoutMs;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        var request = new UnityBridgeToolCallRequest
        {
            Name = toolName,
            ArgumentsJson = arguments.ToJsonString(),
        };

        try
        {
            using var pipe = new NamedPipeClientStream(
                serverName: ".",
                pipeName: _pipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

            await pipe.ConnectAsync(timeoutMs, timeoutCts.Token);

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
                return new BridgeToolCallResult(
                    IsError: true,
                    ContentText: "Bridge pipe returned empty response.",
                    StructuredContent: new JsonObject
                    {
                        ["status"] = "bridge_invalid_response",
                        ["transport"] = "pipe",
                    });
            }

            UnityBridgeToolCallResponse? response =
                JsonSerializer.Deserialize<UnityBridgeToolCallResponse>(rawResponse, JsonOptions);

            if (response is null)
            {
                return new BridgeToolCallResult(
                    IsError: true,
                    ContentText: "Bridge pipe returned invalid JSON response.",
                    StructuredContent: new JsonObject
                    {
                        ["status"] = "bridge_invalid_response",
                        ["transport"] = "pipe",
                    });
            }

            return new BridgeToolCallResult(
                IsError: response.IsError,
                ContentText: string.IsNullOrWhiteSpace(response.ContentText)
                    ? $"Tool '{toolName}' completed."
                    : response.ContentText!,
                StructuredContent: ParseStructuredContent(response.StructuredContentJson));
        }
        catch (Exception ex)
        {
            return new BridgeToolCallResult(
                IsError: true,
                ContentText: $"Bridge pipe request failed: {ex.Message}",
                StructuredContent: new JsonObject
                {
                    ["status"] = "bridge_unreachable",
                    ["transport"] = "pipe",
                });
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
}



