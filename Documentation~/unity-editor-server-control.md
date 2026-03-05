# Unity Editor Server Control (Full Server)

Status: Draft v0.1  
Scope: Start/stop and supervise Unity MCP bridge + host process from Unity Editor

## Entry points

- Menu: `Tools/Unity MCP Bridge/Start Full Server`
- Menu: `Tools/Unity MCP Bridge/Stop Full Server`
- Window: `Tools/Unity MCP Bridge/Server Control`

## What Start Full Server does

`UnityMcpHostSupervisor.StartFullServer()` performs:

1. Load `UnityMcpHostSettings`.
2. Apply current process environment values (module set, bridge transport, allowlists).
3. Start Unity bridge server when `Auto Start Bridge With Host=true`.
4. Launch host process with:
   - executable: `Dotnet Executable` (default `dotnet`)
   - command: `run --project <Host Project Path>`
5. Run startup probe (`initialize` then `tools/list`) within `Startup Probe Timeout (ms)`.

If probe fails, host process is stopped and status becomes failed.

## Server Control actions

- `Start Full Server`: start bridge + host using current settings.
- `Stop Full Server`: stop host process, then stop bridge.
- `Start Bridge Only` / `Stop Bridge Only`: control bridge only.
- `Start Host Only` / `Stop Host Only`: control host process only.

## Host settings (ProjectSettings/UnityMcpHostSettings.asset)

- `Package Root Override`: optional absolute/relative override for package root.
- `Dotnet Executable`: dotnet CLI executable path/name.
- `Host Project Path`: default `Editor/Host~/UnityMcpServer.Host.csproj`.
- `Enabled Modules`: comma-separated module list; empty = schema defaults.
- `Bridge Transport`: `http` or `pipe`.
- `Bridge HTTP URL`: default `http://127.0.0.1:38100/`.
- `Bridge Pipe Name`: default `unity-mcp-bridge`.
- `Bridge Timeout (ms)`: clamped to `500..120000`.
- `Startup Probe Timeout (ms)`: clamped to `1000..120000`.
- `Allowed Path Prefixes`: default `Assets/`.
- `Allowed Component Types`: default `*`.
- `Auto Start Bridge With Host`: default `true`.
- `Auto Start Full Server On Load`: start full server automatically after editor load.

## Runtime status and logs

- Status panel reports bridge/host running state and last supervisor status.
- Host stderr and supervisor messages are buffered in the window (`Host Logs`).
- `Clear Logs` clears only buffered logs in the UI.

## Current limitation

- Host uses MCP stdio transport.
- A host process started from Unity is useful for Unity-side supervision and health checks.
- External MCP clients usually still launch host themselves so they own stdio.

## Troubleshooting

- `Host project not found`: verify `Package Root Override` and `Host Project Path`.
- `startup probe failed`: ensure bridge transport/url/pipe settings match Unity bridge runtime.
- Host not starting: verify `Dotnet Executable` and local .NET SDK installation.



