using System.Text.Json.Nodes;

namespace Blanketmen.UnityMcpServer.Host;

public sealed class JsonRpcException : Exception
{
    public int Code { get; }
    public JsonObject? ErrorData { get; }

    public JsonRpcException(int code, string message, JsonObject? data = null)
        : base(message)
    {
        Code = code;
        ErrorData = data;
    }

    public static JsonRpcException InvalidParams(string message, JsonObject? data = null)
        => new(-32602, message, data);

    public static JsonRpcException MethodNotFound(string message)
        => new(-32601, message);
}


