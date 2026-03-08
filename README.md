# Unity MCP Server Skeleton (.NET 10)

This repository is organized as one main repo with two sub-repo directories:

- `Unity-MCP-Gateway` (Gateway, previously Host)
- `Unity-MCP-Bridge` (Unity bridge runtime/editor integration)

Current code layout:

- Gateway code: `Unity-MCP-Gateway`
- Bridge code: `Unity-MCP-Bridge/Editor`
- Framework: `.NET 10` (`net10.0`)
- Transport: `stdio` JSON-RPC with `Content-Length` framing
- Implemented MCP methods:
  - `initialize`
  - `tools/list`
  - `tools/call`
- Bridge transports:
  - `http` (`localhost`)
  - `pipe` (`Named Pipe`)

Tool metadata is loaded from:

- `Unity-MCP-Gateway/schemas/mcp-tool-modules.json`
- `Unity-MCP-Gateway/schemas/mcp-tools-*.input-schemas.json` (per enabled module)

## Run

```bash
DOTNET_CLI_HOME=/tmp dotnet run --project Unity-MCP-Gateway/UnityMcpGateway.csproj
```

Optional environment variables:

- `UNITY_MCP_ROOT`: override MCP root path lookup (directory containing `schemas/mcp-tool-modules.json`).
- `UNITY_MCP_ENABLED_MODULES`: comma-separated module names.
  Example: `core,diagnostics,scene_read`
- `UNITY_MCP_BRIDGE_TRANSPORT`: `http` (default) or `pipe`
- `UNITY_MCP_BRIDGE_HTTP_URL`: default `http://127.0.0.1:38100/`
- `UNITY_MCP_BRIDGE_PIPE_NAME`: default `unity-mcp-bridge`
- `UNITY_MCP_BRIDGE_TIMEOUT_MS`: request timeout in milliseconds (default `5000`)
- `UNITY_MCP_ALLOWED_PATH_PREFIXES`: comma-separated path allowlist for write operations in Unity bridge (default `Assets/`).
- `UNITY_MCP_ALLOWED_COMPONENT_TYPES`: comma-separated component allowlist patterns for mutation tools (default `*`; supports prefix wildcard like `MyGame.Components.*`).
  - `unity.run_tests` will auto-extend bridge timeout based on `timeoutMs` argument.

If `UNITY_MCP_ENABLED_MODULES` is not set, modules with `enabledByDefault=true` are loaded.

## Run from Unity Editor

Unity package now supports a one-click "full server" flow:

- Menu: `Tools/Unity MCP Bridge/Start Full Server`
- Menu: `Tools/Unity MCP Bridge/Stop Full Server`
- Window: `Tools/Unity MCP Bridge/Server Control`

In `Server Control`, configure:

- `Dotnet Executable` (default: `dotnet`)
- `Host Project Path` (default: `Unity-MCP-Gateway/UnityMcpGateway.csproj`)
- Bridge transport / timeout and write allowlists

`Host Project Path` supports relative or absolute paths. For relative paths, the bridge resolves candidates against:
- `Package Root Override` (if set)
- resolved package root (`PackageInfo`)
- Unity project root
- `Packages/com.blanketmen.mcp.bridge`
- `Library/PackageCache/com.blanketmen.mcp.bridge*`

Then click `Start Full Server` to:

1. Start Unity bridge (`UnityMcpBridgeServer`)
2. Start host process (`dotnet run --project ...`)
3. Run startup probe (`initialize` + `tools/list`)

Important limitation:

- `UnityMcpGateway` currently uses stdio MCP transport only.
- A host process started by Unity is suitable for Unity-side supervision and health checks, but external MCP clients normally still need to launch the host process themselves so they can own stdio.

## Current behavior

- `tools/call` now forwards to Unity bridge over HTTP or Named Pipe.
- `tools/call` now validates `arguments` against each tool's `inputSchema` before forwarding.
- Unity bridge currently implements:
  - `unity.ping`
  - `unity.project_info`
  - `unity.playmode_status`
  - `unity.playmode_start`
  - `unity.playmode_stop`
  - `unity.list_scenes`
  - `unity.open_scene`
  - `unity.get_console_logs`
  - `unity.clear_console`
  - `unity.go_find`
  - `unity.component_get_fields`
  - `unity.go_create`
  - `unity.go_delete`
  - `unity.go_duplicate`
  - `unity.go_reparent`
  - `unity.go_rename`
  - `unity.go_set_active`
  - `unity.component_add`
  - `unity.component_remove`
  - `unity.component_set_fields`
  - `unity.asset_search`
  - `unity.asset_get`
  - `unity.asset_refs`
  - `unity.prefab_create`
  - `unity.prefab_instantiate`
  - `unity.prefab_apply_overrides`
  - `unity.prefab_revert_overrides`
  - `unity.prefab_unpack`
  - `unity.prefab_create_variant`
  - `unity.asset_move`
  - `unity.asset_rename`
  - `unity.asset_delete_to_trash`
  - `unity.asset_reimport`
  - `unity.asset_set_labels`
- `unity.run_tests` is implemented via Unity Test Framework reflection APIs (requires `com.unity.test-framework`).
  - when `includeXmlReportPath=true`, bridge writes `Library/McpReports/latest-test-results.xml`.
- Selective include flags for `prefab_apply_overrides` / `prefab_revert_overrides` are currently `unsupported` (all include flags must be `true`).










