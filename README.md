# Unity MCP Server (Package Root)

This repository is the Unity package root and contains two components:

- `Editor/`: Unity Editor control package source (HTTP/Named Pipe tool endpoint)
- `Gateway~/`: MCP gateway source process (.NET, streamable HTTP)

## Clone

```bash
git clone <this-repo-url>
```

## Repository Layout

- `Editor/`: Control component source
- `Gateway~/`: Gateway component source
- `Documentation~/`: package documentation

## Run Gateway

```bash
DOTNET_CLI_HOME=/tmp dotnet run --project Gateway~/UnityMcpGateway.csproj
```

By default, Gateway serves MCP Streamable HTTP at `http://127.0.0.1:38110/mcp`.

Common environment variables:

- `UNITY_MCP_ROOT` (default should point to `Gateway~/`)
- `UNITY_MCP_ENABLED_MODULES`
- `UNITY_MCP_GATEWAY_TRANSPORT` (`streamable-http` default, or `stdio`)
- `UNITY_MCP_STREAMABLE_HTTP_URL` (default `http://127.0.0.1:38110/mcp`)
- `UNITY_MCP_CONTROL_TRANSPORT` (`http` or `pipe`)
- `UNITY_MCP_CONTROL_HTTP_URL`
- `UNITY_MCP_CONTROL_PIPE_NAME`
- `UNITY_MCP_CONTROL_TIMEOUT_MS`
- `UNITY_MCP_ALLOWED_PATH_PREFIXES`
- `UNITY_MCP_ALLOWED_COMPONENT_TYPES`

## Unity Editor Side

Use the Control package and open:

- `Tools/Unity MCP Control`

Current Unity editor tooling manages only the Control endpoint lifecycle (start/stop and settings). Gateway process lifecycle is expected to be managed by external tools (for example IDE MCP launch config).

## Additional Docs

- [Control overview](Documentation~/unity-mcp-control.md)
- [Editor control](Documentation~/unity-editor-control.md)
- [Tool modules](Documentation~/mcp-tool-modules.md)
