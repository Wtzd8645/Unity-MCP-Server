# Unity Editor Bridge Control

Status: Draft v0.2  
Scope: Start/stop and configure Unity MCP Bridge from Unity Editor

## Entry point

- Menu: `Tools/Unity MCP Bridge`

## What the window does

`UnityMcpServerWindow` currently controls bridge lifecycle only:

1. Show bridge status (`Running` / `Stopped`)
2. Start bridge
3. Stop bridge
4. Edit bridge runtime settings

## Settings shown in window

- `Bridge Transport`: `http` or `pipe`
- `Bridge HTTP URL`: default `http://127.0.0.1:38100/`
- `Bridge Pipe Name`: default `unity-mcp-bridge`
- `Bridge Timeout (ms)`
- `Allowed Path Prefixes`
- `Allowed Component Types`
- `Auto Start Bridge On Load`

Settings are persisted in `ProjectSettings/UnityMcpHostSettings.asset`.

## Host lifecycle note

The Unity Editor window does not manage Gateway process start/stop. Launch Gateway from external tooling (for example VS Code MCP config) and point it to this Unity bridge.

## Troubleshooting

- Bridge not reachable: verify transport/url/pipe settings on both Gateway and Unity bridge.
- Bridge start failed: check Unity Console for port/pipe conflicts and editor exceptions.
