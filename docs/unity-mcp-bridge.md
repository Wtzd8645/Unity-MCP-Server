# Unity MCP Bridge

This document describes the Unity Editor bridge side in the split-repo setup.

## Position in architecture

- Main repo: `Unity-MCP-Server`
- Bridge submodule: `Unity-MCP-Bridge`
- Gateway submodule: `Unity-MCP-Gateway`

Bridge responsibilities:

- Expose tool-call endpoint to Gateway (HTTP or Named Pipe)
- Execute Unity operations on editor main thread
- Return MCP-friendly tool results

## Runtime endpoints

- HTTP: `POST /mcp/tool/call`
- Named Pipe: configured by `Bridge Pipe Name` (default `unity-mcp-bridge`)

## Editor control

Use `Tools/Unity MCP Bridge` for:

- Start/stop bridge
- Bridge transport and timeout settings
- Path/component allowlist settings

## Implementation highlights

- `UnityMcpBridgeServer.cs`: bridge transport + lifecycle
- `BridgeToolDispatcher.cs`: tool routing
- `CoreToolHandlers.cs`: core tools
- `DiagnosticsToolHandlers.cs`: diagnostics tools
- `SceneReadToolHandlers.cs`: scene read tools
- `SceneWriteToolHandlers.cs`: scene write tools
- `AssetReadToolHandlers.cs`: asset read tools
- `AssetWriteToolHandlers.cs`: asset write tools
- `PrefabWriteToolHandlers.cs`: prefab write tools

## Notes

- Gateway process startup is not managed by bridge window.
- `unity_run_tests` is implemented via Unity Test Framework reflection APIs (`com.unity.test-framework`).
