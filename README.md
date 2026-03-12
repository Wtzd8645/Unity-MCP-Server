# Unity MCP Server (Main Repo)

This repository contains two components:

- `Unity-MCP-Gateway`: MCP gateway process (.NET, streamable HTTP)
- `Unity-MCP-Control`: Unity Editor control package (HTTP/Named Pipe tool endpoint)

## Clone

```bash
git clone <this-repo-url>
```

## Repository Layout

- `Unity-MCP-Gateway/`: Gateway component
- `Unity-MCP-Control/`: Control component
- `docs/`: cross-repo documentation in this main repo

## Run Gateway

```bash
DOTNET_CLI_HOME=/tmp dotnet run --project Unity-MCP-Gateway/UnityMcpGateway.csproj
```

By default, Gateway serves MCP Streamable HTTP at `http://127.0.0.1:38110/mcp`.

Common environment variables:

- `UNITY_MCP_ROOT` (default should point to `Unity-MCP-Gateway`)
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

- [Control overview](docs/unity-mcp-control.md)
- [Editor control](docs/unity-editor-control.md)
- [Tool modules](docs/mcp-tool-modules.md)
