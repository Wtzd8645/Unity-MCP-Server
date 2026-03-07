using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Blanketmen.UnityMcpServer.Host;

public sealed class HttpUnityBridgeClient : IUnityBridgeClient
{
    private readonly HttpClient httpClient;
    private readonly int timeoutMs;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public HttpUnityBridgeClient(Uri baseUri, int timeoutMs)
    {
        this.timeoutMs = timeoutMs;
        httpClient = new HttpClient
        {
            BaseAddress = baseUri,
        };
    }

    public async Task<BridgeToolCallResult> CallToolAsync(
        string toolName,
        JsonObject arguments,
        int? timeoutMsOverride,
        CancellationToken cancellationToken)
    {
        var request = new UnityBridgeToolCallRequest
        {
            Name = toolName,
            ArgumentsJson = arguments.ToJsonString(),
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMsOverride ?? timeoutMs);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                "mcp/tool/call",
                request,
                JsonOptions,
                timeoutCts.Token);
        }
        catch (Exception ex)
        {
            return new BridgeToolCallResult(
                IsError: true,
                ContentText: $"Bridge HTTP request failed: {ex.Message}",
                StructuredContent: new JsonObject
                {
                    ["status"] = "bridge_unreachable",
                    ["transport"] = "http",
                });
        }

        if (!response.IsSuccessStatusCode)
        {
            return new BridgeToolCallResult(
                IsError: true,
                ContentText: $"Bridge HTTP status {(int)response.StatusCode} ({response.StatusCode}).",
                StructuredContent: new JsonObject
                {
                    ["status"] = "bridge_http_error",
                    ["transport"] = "http",
                    ["statusCode"] = (int)response.StatusCode,
                });
        }

        UnityBridgeToolCallResponse? bridgeResponse =
            await response.Content.ReadFromJsonAsync<UnityBridgeToolCallResponse>(
                JsonOptions,
                timeoutCts.Token);

        if (bridgeResponse is null)
        {
            return new BridgeToolCallResult(
                IsError: true,
                ContentText: "Bridge returned empty response.",
                StructuredContent: new JsonObject
                {
                    ["status"] = "bridge_invalid_response",
                    ["transport"] = "http",
                });
        }

        return new BridgeToolCallResult(
            IsError: bridgeResponse.IsError,
            ContentText: string.IsNullOrWhiteSpace(bridgeResponse.ContentText)
                ? $"Tool '{toolName}' completed."
                : bridgeResponse.ContentText,
            StructuredContent: ParseStructuredContent(bridgeResponse.StructuredContentJson));
    }

    private static JsonNode? ParseStructuredContent(string? rawJson)
    {
        try
        {
            return string.IsNullOrWhiteSpace(rawJson) ? null : JsonNode.Parse(rawJson);
        }
        catch
        {
            return new JsonObject { ["raw"] = rawJson, };
        }
    }
}

public sealed class UnityBridgeToolCallRequest
{
    public string Name { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
}

public sealed class UnityBridgeToolCallResponse
{
    public bool IsError { get; set; }
    public string? ContentText { get; set; }
    public string? StructuredContentJson { get; set; }
}
