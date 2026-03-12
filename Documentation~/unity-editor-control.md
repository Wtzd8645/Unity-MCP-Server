# Unity Editor Control

Status: Draft v0.3  
Scope: Start/stop and configure Unity MCP Control and Gateway from Unity Editor

## Entry point

- Menu: `Tools/Unity MCP Control`

## What the window does

`UnityMcpControlWindow` manages both Control and Gateway lifecycle:

1. Show control status (`Running` / `Stopped`)
2. Show gateway process status (`Stopped` / `Starting` / `Running` / `Exited` / `Error`)
3. Start control + gateway together
4. Stop control + gateway together
5. Restart gateway
6. Edit control and gateway runtime settings

## Settings shown in window

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

Settings are persisted in `ProjectSettings/UnityMcpGatewaySettings.asset`.

## Troubleshooting

- Control not reachable: verify transport/url/pipe settings on both Gateway and Unity control.
- Control start failed: check Unity Console for port/pipe conflicts and editor exceptions.
- Gateway start failed: verify `dotnet` is available and `Gateway Project Path` points to `Gateway~/UnityMcpGateway.csproj`.
