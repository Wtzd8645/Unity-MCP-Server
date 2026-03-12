using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcp.Gateway;

public sealed class StreamableHttpGateway : IDisposable
{
    private const string DefaultEndpoint = "http://127.0.0.1:38110/mcp";

    private readonly McpServer _mcpServer;
    private readonly HttpListener _listener;
    private readonly string _endpointPath;
    private readonly string _listenerPrefix;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private StreamableHttpGateway(
        McpServer mcpServer,
        HttpListener listener,
        string endpointPath,
        string listenerPrefix)
    {
        _mcpServer = mcpServer;
        _listener = listener;
        _endpointPath = endpointPath;
        _listenerPrefix = listenerPrefix;
    }

    public static StreamableHttpGateway CreateFromEnvironment(McpServer mcpServer)
    {
        string rawEndpoint = Environment.GetEnvironmentVariable("UNITY_MCP_STREAMABLE_HTTP_URL") ?? DefaultEndpoint;
        if (!Uri.TryCreate(rawEndpoint, UriKind.Absolute, out Uri? endpoint))
        {
            throw new InvalidOperationException(
                $"Invalid UNITY_MCP_STREAMABLE_HTTP_URL: '{rawEndpoint}'.");
        }

        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "UNITY_MCP_STREAMABLE_HTTP_URL must use http or https.");
        }

        string endpointPath = NormalizePath(endpoint.AbsolutePath);
        string listenerPrefix = $"{endpoint.GetLeftPart(UriPartial.Authority)}{endpointPath}/";

        var listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        return new StreamableHttpGateway(mcpServer, listener, endpointPath, listenerPrefix);
    }

    public async Task RunAsync(TextWriter log, CancellationToken cancellationToken)
    {
        _listener.Start();
        await log.WriteLineAsync(
            $"[{DateTimeOffset.UtcNow:O}] streamable-http listening on {_listenerPrefix} " +
            $"(endpoint: {_endpointPath})");
        await log.FlushAsync();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
                // Best effort shutdown.
            }
        });

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = HandleContextSafeAsync(context, log, cancellationToken);
        }
    }

    public void Dispose()
    {
        _listener.Close();
    }

    private async Task HandleContextSafeAsync(
        HttpListenerContext context,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleContextAsync(context, log, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Response.Abort();
        }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] HTTP handler error: {ex}");
            await log.FlushAsync();

            if (context.Response.OutputStream.CanWrite)
            {
                JsonObject error = McpServer.CreateErrorEnvelope(
                    id: null,
                    code: -32603,
                    message: "Internal error");
                await WriteJsonAsync(context.Response, statusCode: 500, error, cancellationToken);
            }
            else
            {
                context.Response.Abort();
            }
        }
    }

    private async Task HandleContextAsync(
        HttpListenerContext context,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (!IsEndpointMatch(request.Url?.AbsolutePath))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        if (string.Equals(request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 204;
            response.Headers["Allow"] = "POST, OPTIONS";
            response.Close();
            return;
        }

        if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 405;
            response.Headers["Allow"] = "POST, OPTIONS";
            response.Close();
            return;
        }

        string rawBody;
        using (var reader = new StreamReader(
                   request.InputStream,
                   request.ContentEncoding ?? Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: true,
                   bufferSize: 1024,
                   leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync().WaitAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            JsonObject parseError = McpServer.CreateErrorEnvelope(
                id: null,
                code: -32700,
                message: "Parse error");
            await WriteJsonAsync(response, statusCode: 400, parseError, cancellationToken);
            return;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(rawBody);
        }
        catch (JsonException)
        {
            JsonObject parseError = McpServer.CreateErrorEnvelope(
                id: null,
                code: -32700,
                message: "Parse error");
            await WriteJsonAsync(response, statusCode: 400, parseError, cancellationToken);
            return;
        }

        bool useEventStream = AcceptsEventStream(request);
        if (parsed is JsonObject singleRequest)
        {
            JsonObject? singleResponse = await _mcpServer.ProcessRequestAsync(singleRequest, log, cancellationToken);
            if (singleResponse is null)
            {
                response.StatusCode = 202;
                response.Close();
                return;
            }

            if (useEventStream)
            {
                await WriteEventStreamAsync(response, new[] { singleResponse }, cancellationToken);
                return;
            }

            await WriteJsonAsync(response, statusCode: 200, singleResponse, cancellationToken);
            return;
        }

        if (parsed is JsonArray batch)
        {
            List<JsonObject> batchResponses = await ProcessBatchAsync(batch, log, cancellationToken);
            if (batchResponses.Count == 0)
            {
                response.StatusCode = 202;
                response.Close();
                return;
            }

            if (useEventStream)
            {
                await WriteEventStreamAsync(response, batchResponses, cancellationToken);
                return;
            }

            var payload = new JsonArray();
            foreach (JsonObject item in batchResponses)
            {
                payload.Add(item);
            }

            await WriteJsonAsync(response, statusCode: 200, payload, cancellationToken);
            return;
        }

        JsonObject invalidRequest = McpServer.CreateErrorEnvelope(
            id: null,
            code: -32600,
            message: "Invalid Request");
        await WriteJsonAsync(response, statusCode: 400, invalidRequest, cancellationToken);
    }

    private async Task<List<JsonObject>> ProcessBatchAsync(
        JsonArray batch,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        var responses = new List<JsonObject>();
        if (batch.Count == 0)
        {
            responses.Add(
                McpServer.CreateErrorEnvelope(
                    id: null,
                    code: -32600,
                    message: "Invalid Request"));
            return responses;
        }

        foreach (JsonNode? item in batch)
        {
            if (item is not JsonObject requestObject)
            {
                responses.Add(
                    McpServer.CreateErrorEnvelope(
                        id: null,
                        code: -32600,
                        message: "Invalid Request"));
                continue;
            }

            JsonObject? response = await _mcpServer.ProcessRequestAsync(
                requestObject,
                log,
                cancellationToken);
            if (response is not null)
            {
                responses.Add(response);
            }
        }

        return responses;
    }

    private static async Task WriteJsonAsync(
        HttpListenerResponse response,
        int statusCode,
        JsonNode payload,
        CancellationToken cancellationToken)
    {
        string json = payload.ToJsonString(SerializerOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
        response.Close();
    }

    private static async Task WriteEventStreamAsync(
        HttpListenerResponse response,
        IEnumerable<JsonObject> messages,
        CancellationToken cancellationToken)
    {
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.ContentEncoding = Encoding.UTF8;
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";

        using var writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false), 1024, leaveOpen: true);
        foreach (JsonObject message in messages)
        {
            string serialized = message.ToJsonString(SerializerOptions);
            await writer.WriteLineAsync("event: message");
            await writer.WriteLineAsync($"data: {serialized}");
            await writer.WriteLineAsync();
            await writer.FlushAsync();
        }

        await writer.FlushAsync().WaitAsync(cancellationToken);
        response.Close();
    }

    private bool IsEndpointMatch(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        string path = NormalizePath(rawPath);
        return string.Equals(path, _endpointPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AcceptsEventStream(HttpListenerRequest request)
    {
        string? accept = request.Headers["Accept"];
        return accept?.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/mcp";
        }

        string trimmed = path.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed.TrimEnd('/');
    }
}
