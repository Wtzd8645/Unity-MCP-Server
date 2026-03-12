# Unity Editor Control

Status: Draft v0.2  
Scope: Start/stop and configure Unity MCP Control from Unity Editor

## Entry point

- Menu: `Tools/Unity MCP Control`

## What the window does

`UnityMcpControlWindow` currently manages the Control endpoint lifecycle only:

1. Show control status (`Running` / `Stopped`)
2. Start control
3. Stop control
4. Edit control runtime settings

## Settings shown in window

- `Control Transport`: `http` or `pipe`
- `Control HTTP URL`: default `http://127.0.0.1:38100/`
- `Control Pipe Name`: default `unity-mcp-control`
- `Control Timeout (ms)`
- `Allowed Path Prefixes`
- `Allowed Component Types`
- `Auto Start Control On Load`

Settings are persisted in `ProjectSettings/UnityMcpGatewaySettings.asset`.

## Gateway lifecycle note

The Unity Editor window does not manage Gateway process start/stop. Launch Gateway from external tooling (for example VS Code MCP config) and point it to this Unity control.

## Troubleshooting

- Control not reachable: verify transport/url/pipe settings on both Gateway and Unity control.
- Control start failed: check Unity Console for port/pipe conflicts and editor exceptions.
