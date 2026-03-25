# Unity MCP Control Architecture

This document describes the current package architecture and the contract boundary between the Unity editor control side and the MCP gateway.

## Package layout

- `Editor/`: Unity editor control implementation and tool handlers
- `Gateway~/`: MCP gateway process, schema loading, transport hosting
- `Documentation~/`: package documentation

## Control responsibilities

The Unity editor control side is responsible for:

- Exposing a tool-call endpoint to the gateway over HTTP or Named Pipe
- Executing Unity operations on the editor main thread
- Returning MCP-friendly tool results for registered tool names

The gateway side is responsible for:

- Loading tool schemas from `Gateway~/schemas/`
- Enabling modules based on `UNITY_MCP_ENABLED_MODULES`
- Validating tool input against the active schemas
- Serving MCP over streamable HTTP or stdio

## Runtime endpoints and settings

- Control HTTP endpoint: `POST /mcp/tool/call`
- Control pipe name: configured by `Control Pipe Name` and defaults to `unity-mcp-control`
- Gateway streamable HTTP endpoint: defaults to `http://127.0.0.1:38110/mcp`

Primary runtime settings come from:

- `ProjectSettings/UnityMcpGatewaySettings.asset`
- Environment variables such as `UNITY_MCP_ENABLED_MODULES`, `UNITY_MCP_ALLOWED_PATH_PREFIXES`, and `UNITY_MCP_ALLOWED_COMPONENT_TYPES`

## Source of truth

- Active module membership: `Gateway~/schemas/mcp-tool-modules.json`
- Active tool schemas: `Gateway~/schemas/mcp-tools-*.input-schemas.json`
- Active tool routing: `Editor/ControlToolDispatcher.cs`

Current runtime tool names are the canonical names listed in the schema files and routed by the dispatcher. Legacy names are no longer active runtime names.

## Implementation map

- `UnityMcpControlServer.cs`: control transport and request lifecycle
- `ControlToolDispatcher.cs`: tool routing by active runtime name
- `CoreToolHandlers.cs`: `project_read`, `project_write`, build-oriented `project_execute`, and `runtime_*`
- `DiagnosticsToolHandlers.cs`: `editor_read`, `editor_execute`, `editor_write`, and test-oriented `project_execute`
- `SceneReadToolHandlers.cs`: `scene_read`, `scene_execute`, `gameobject_read`, `component_read`
- `SceneWriteToolHandlers.cs`: `scene_write`, `gameobject_write`, `component_write`
- `PrefabReadToolHandlers.cs`: `prefab_read`
- `AssetReadToolHandlers.cs`: `asset_read`
- `AssetWriteToolHandlers.cs`: `asset_write`
- `PrefabWriteToolHandlers.cs`: `prefab_write`

## Related docs

- `Documentation~/mcp-editor-control-window.md`: Unity window usage and troubleshooting
- `Documentation~/mcp-tool-catalog.md`: module catalog and default enablement
- `Documentation~/mcp-tool-schemas-runtime.md`: project, editor, runtime, and scene runtime-facing details
- `Documentation~/mcp-tool-schemas-authoring.md`: scene write, gameobject, component, prefab, and asset details
