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
- Active tool routing: `Editor/Control/ControlToolDispatcher.cs`

Current runtime tool names are the canonical names listed in the schema files and routed by the dispatcher. Legacy names are no longer active runtime names.

## Implementation map

- `Editor/Control/`: dispatcher, contracts, support, server, host, settings, log store, and UI/control entrypoints
- `Editor/Modules/Project/`: `project_read`, `project_execute`, `project_write`
- `Editor/Modules/Editor/`: `editor_read`, `editor_execute`, `editor_write`
- `Editor/Modules/Runtime/`: `runtime_read`, `runtime_execute`
- `Editor/Modules/Scene/`: `scene_read`, `scene_execute`, `scene_write`
- `Editor/Modules/GameObject/`: `gameobject_read`, `gameobject_write`
- `Editor/Modules/Component/`: `component_read`, `component_write`
- `Editor/Modules/Prefab/`: `prefab_read`, `prefab_write`
- `Editor/Modules/Asset/`: `asset_read`, `asset_write`

## Related docs

- `Documentation~/mcp-editor-control-window.md`: Unity window usage and troubleshooting
- `Documentation~/mcp-tool-catalog.md`: module catalog and default enablement
- `Documentation~/mcp-tool-schemas-runtime.md`: project, editor, runtime, and scene runtime-facing details
- `Documentation~/mcp-tool-schemas-authoring.md`: scene write, gameobject, component, prefab, and asset details
