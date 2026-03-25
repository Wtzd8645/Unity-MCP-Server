# Unity MCP Editor Control Window

This document describes the Unity window used to start, stop, and configure the control endpoint and gateway process.

## Entry point

- Menu: `Tools/Unity MCP Control`

## Window responsibilities

`UnityMcpControlWindow` manages both control and gateway lifecycle:

1. Show control status: `Running` or `Stopped`
2. Show gateway process status: `Stopped`, `Starting`, `Running`, `Exited`, or `Error`
3. Start control and gateway together
4. Stop control and gateway together
5. Restart the gateway
6. Edit runtime settings for both components

## Settings shown in the window

- `Dotnet Executable`
- `Gateway Project Path`
- `Enabled Modules`
- `Control Transport`: `http` or `pipe`
- `Control HTTP URL`: default `http://127.0.0.1:38100/`
- `Control Pipe Name`: default `unity-mcp-control`
- `Control Timeout (ms)`
- `Allowed Path Prefixes`
- `Allowed Component Types`
- `Auto Start Control On Load`
- `Auto Start Gateway With Control`

Settings are stored in `ProjectSettings/UnityMcpGatewaySettings.asset`.

## Operational notes

- The window is the editor-facing entry point for both lifecycle control and runtime settings.
- Path and component allowlists affect the write-capable tool families documented in `Documentation~/mcp-tool-schemas-authoring.md`.
- Enabled modules determine which schemas the gateway exposes at runtime.

## Troubleshooting

- Control not reachable: verify the control transport and the matching HTTP URL or pipe name on both sides.
- Control start failed: check Unity Console for editor exceptions and port or pipe conflicts.
- Gateway start failed: verify `dotnet` is available and `Gateway Project Path` points to `Gateway~/UnityMcpGateway.csproj`.
