using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcpServer.Host;

// MCP stdio transport uses newline-delimited JSON (NDJSON): one JSON object per line.
// See: https://spec.modelcontextprotocol.io/specification/basic/transports/#stdio
public sealed class StdioJsonRpcTransport
{
    private readonly StreamReader _reader;
    private readonly Stream _output;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    public StdioJsonRpcTransport(Stream input, Stream output)
    {
        _reader = new StreamReader(input, Encoding.UTF8);
        _output = output;
    }

    public async Task<JsonObject?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line = await _reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonNode? parsed = JsonNode.Parse(line);
            return parsed as JsonObject;
        }
    }

    public Task WriteResultAsync(JsonNode? id, JsonObject result, CancellationToken cancellationToken)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        };

        return WriteMessageAsync(envelope, cancellationToken);
    }

    public Task WriteErrorAsync(
        JsonNode? id,
        int code,
        string message,
        CancellationToken cancellationToken,
        JsonObject? data = null)
    {
        var error = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (data is not null)
        {
            error["data"] = data;
        }

        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = error,
        };

        return WriteMessageAsync(envelope, cancellationToken);
    }

    private async Task WriteMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        string json = message.ToJsonString(SerializerOptions);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(json + "\n");

        await _output.WriteAsync(payloadBytes.AsMemory(0, payloadBytes.Length), cancellationToken);
        await _output.FlushAsync(cancellationToken);
    }
}


