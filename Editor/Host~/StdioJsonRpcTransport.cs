using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcpServer.Host;

public sealed class StdioJsonRpcTransport
{
    private readonly Stream _input;
    private readonly Stream _output;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    public StdioJsonRpcTransport(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    public async Task<JsonObject?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        int? contentLength = null;

        while (true)
        {
            string? line = await ReadHeaderLineAsync(cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            int colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
            {
                continue;
            }

            string key = line[..colonIndex].Trim();
            string value = line[(colonIndex + 1)..].Trim();
            if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value, out int parsedLength))
            {
                contentLength = parsedLength;
            }
        }

        if (contentLength is null || contentLength <= 0)
        {
            return null;
        }

        byte[] payload = await ReadExactBytesAsync(contentLength.Value, cancellationToken);
        string json = Encoding.UTF8.GetString(payload);
        JsonNode? parsed = JsonNode.Parse(json);
        return parsed as JsonObject;
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
        byte[] payloadBytes = Encoding.UTF8.GetBytes(json);
        string header = $"Content-Length: {payloadBytes.Length}\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        await _output.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), cancellationToken);
        await _output.WriteAsync(payloadBytes.AsMemory(0, payloadBytes.Length), cancellationToken);
        await _output.FlushAsync(cancellationToken);
    }

    private async Task<byte[]> ReadExactBytesAsync(int contentLength, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[contentLength];
        int offset = 0;

        while (offset < contentLength)
        {
            int read = await _input.ReadAsync(
                buffer.AsMemory(offset, contentLength - offset),
                cancellationToken);

            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading payload.");
            }

            offset += read;
        }

        return buffer;
    }

    private async Task<string?> ReadHeaderLineAsync(CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(64);
        var one = new byte[1];

        while (true)
        {
            int read = await _input.ReadAsync(one.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (one[0] == (byte)'\n')
            {
                break;
            }

            bytes.Add(one[0]);
        }

        if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
        {
            bytes.RemoveAt(bytes.Count - 1);
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }
}


