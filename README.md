# Unity MCP Server (Main Repo)

This repository is the superproject for two Git submodules:

- `Unity-MCP-Gateway`: MCP gateway process (.NET, stdio)
- `Unity-MCP-Bridge`: Unity Editor bridge package (HTTP/Named Pipe tool endpoint)

## Clone

```bash
git clone --recurse-submodules <this-repo-url>
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

## Repository Layout

- `Unity-MCP-Gateway/`: Gateway submodule
- `Unity-MCP-Bridge/`: Bridge submodule
- `docs/`: cross-repo documentation in this main repo

## Run Gateway

```bash
DOTNET_CLI_HOME=/tmp dotnet run --project Unity-MCP-Gateway/UnityMcpGateway.csproj
```

Common environment variables:

- `UNITY_MCP_ROOT` (default should point to `Unity-MCP-Gateway`)
- `UNITY_MCP_ENABLED_MODULES`
- `UNITY_MCP_BRIDGE_TRANSPORT` (`http` or `pipe`)
- `UNITY_MCP_BRIDGE_HTTP_URL`
- `UNITY_MCP_BRIDGE_PIPE_NAME`
- `UNITY_MCP_BRIDGE_TIMEOUT_MS`
- `UNITY_MCP_ALLOWED_PATH_PREFIXES`
- `UNITY_MCP_ALLOWED_COMPONENT_TYPES`

## Unity Editor Side

Use the Bridge submodule package and open:

- `Tools/Unity MCP Bridge`

Current Editor control is bridge-only (start/stop bridge and bridge settings). Host process lifecycle is expected to be managed by external tools (for example IDE MCP launch config).

## Additional Docs

- [Bridge overview](docs/unity-mcp-bridge.md)
- [Editor bridge control](docs/unity-editor-server-control.md)
- [Tool modules](docs/mcp-tool-modules.md)
