# Unity MCP Control

This document describes the Unity Editor control side in the Unity MCP Server monorepo setup.

## Position in architecture

- Main repo: `Unity-MCP-Server`
- Control component: `Unity-MCP-Control`
- Gateway component: `Unity-MCP-Gateway`

Control responsibilities:

- Expose tool-call endpoint to Gateway (HTTP or Named Pipe)
- Execute Unity operations on editor main thread
- Return MCP-friendly tool results

## Runtime endpoints

- HTTP: `POST /mcp/tool/call`
- Named Pipe: configured by `Control Pipe Name` (default `unity-mcp-control`)

## Editor control

Use `Tools/Unity MCP Control` for:

- Start/stop control
- Control transport and timeout settings
- Path/component allowlist settings

## Implementation highlights

- `UnityMcpControlServer.cs`: control transport + lifecycle
- `ControlToolDispatcher.cs`: tool routing
- `CoreToolHandlers.cs`: core tools
- `DiagnosticsToolHandlers.cs`: diagnostics tools
- `SceneReadToolHandlers.cs`: scene read tools
- `SceneWriteToolHandlers.cs`: scene write tools
- `AssetReadToolHandlers.cs`: asset read tools
- `AssetWriteToolHandlers.cs`: asset write tools
- `PrefabWriteToolHandlers.cs`: prefab write tools

## Notes

- Gateway process startup is not managed by control window.
- `unity_run_tests` is implemented via Unity Test Framework reflection APIs (`com.unity.test-framework`).
