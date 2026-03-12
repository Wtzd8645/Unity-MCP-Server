using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Blanketmen.UnityMcp.Gateway;

public sealed class HttpUnityControlClient : IUnityControlClient
{
    private readonly HttpClient httpClient;
    private readonly int timeoutMs;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public HttpUnityControlClient(Uri baseUri, int timeoutMs)
    {
        this.timeoutMs = timeoutMs;
        httpClient = new HttpClient
        {
            BaseAddress = baseUri,
        };
    }

    public async Task<ControlToolCallResult> CallToolAsync(
        string toolName,
        JsonObject arguments,
        int? timeoutMsOverride,
        CancellationToken cancellationToken)
    {
        var request = new UnityControlToolCallRequest
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
            return new ControlToolCallResult(
                IsError: true,
                ContentText: $"Control HTTP request failed: {ex.Message}",
                StructuredContent: new JsonObject
                {
                    ["status"] = "control_unreachable",
                    ["transport"] = "http",
                });
        }

        if (!response.IsSuccessStatusCode)
        {
            return new ControlToolCallResult(
                IsError: true,
                ContentText: $"Control HTTP status {(int)response.StatusCode} ({response.StatusCode}).",
                StructuredContent: new JsonObject
                {
                    ["status"] = "control_http_error",
                    ["transport"] = "http",
                    ["statusCode"] = (int)response.StatusCode,
                });
        }

        UnityControlToolCallResponse? controlResponse =
            await response.Content.ReadFromJsonAsync<UnityControlToolCallResponse>(
                JsonOptions,
                timeoutCts.Token);

        if (controlResponse is null)
        {
            return new ControlToolCallResult(
                IsError: true,
                ContentText: "Control returned empty response.",
                StructuredContent: new JsonObject
                {
                    ["status"] = "control_invalid_response",
                    ["transport"] = "http",
                });
        }

        return new ControlToolCallResult(
            IsError: controlResponse.IsError,
            ContentText: string.IsNullOrWhiteSpace(controlResponse.ContentText)
                ? $"Tool '{toolName}' completed."
                : controlResponse.ContentText,
            StructuredContent: ParseStructuredContent(controlResponse.StructuredContentJson));
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

public sealed class UnityControlToolCallRequest
{
    public string Name { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
}

public sealed class UnityControlToolCallResponse
{
    public bool IsError { get; set; }
    public string? ContentText { get; set; }
    public string? StructuredContentJson { get; set; }
}


