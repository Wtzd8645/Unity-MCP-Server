using System;

namespace Blanketmen.UnityMcp.Gateway;

public sealed record GatewayHttpEndpoint(string ListenUrl, string RoutePattern, string Host)
{
    private const string DefaultEndpoint = "http://127.0.0.1:38100/mcp";

    public static GatewayHttpEndpoint CreateFromEnvironment()
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

        string routePattern = NormalizeRoutePattern(endpoint.AbsolutePath);
        var listenUri = new UriBuilder(endpoint)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return new GatewayHttpEndpoint(
            listenUri.Uri.GetLeftPart(UriPartial.Authority),
            routePattern,
            endpoint.Host);
    }

    public bool IsAllowedRequestHost(string? requestHost)
    {
        if (string.IsNullOrWhiteSpace(requestHost))
        {
            return false;
        }

        string normalizedRequestHost = NormalizeHost(requestHost);
        string normalizedConfiguredHost = NormalizeHost(Host);
        if (IsWildcardHost(normalizedConfiguredHost))
        {
            return true;
        }

        if (string.Equals(normalizedRequestHost, normalizedConfiguredHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsLoopbackHost(normalizedConfiguredHost) && IsLoopbackHost(normalizedRequestHost);
    }

    private static string NormalizeRoutePattern(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "/", StringComparison.Ordinal))
        {
            return "/mcp";
        }

        string trimmed = path.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = "/" + trimmed;
        }

        trimmed = trimmed.TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "/mcp" : trimmed;
    }

    private static string NormalizeHost(string host)
    {
        string normalized = host.Trim();
        if (normalized.StartsWith("[", StringComparison.Ordinal) &&
            normalized.EndsWith("]", StringComparison.Ordinal) &&
            normalized.Length > 2)
        {
            normalized = normalized.Substring(1, normalized.Length - 2);
        }

        return normalized;
    }

    private static bool IsLoopbackHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWildcardHost(string host)
    {
        return string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::", StringComparison.OrdinalIgnoreCase);
    }
}
