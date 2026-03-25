# Unity MCP Server

This repository is the Unity package root and contains two components:

- `Editor/`: Unity Editor control package source (HTTP/Named Pipe tool endpoint)
- `Gateway~/`: MCP gateway source process (.NET, streamable HTTP)

## Unity Editor Side

Use the Control package and open:

- `Tools/Unity MCP Control`

Current Unity editor tooling manages both:

- Control endpoint lifecycle (start/stop and settings)
- Gateway process lifecycle (start/stop/restart and process monitoring)

By default, Gateway serves MCP Streamable HTTP at `http://127.0.0.1:38110/mcp`.

Common environment variables:

- `UNITY_MCP_ROOT` (default should point to `Gateway~/`)
- `UNITY_MCP_ENABLED_MODULES` (comma-separated module names such as `project_read,scene_read,prefab_read,asset_read`)
- `UNITY_MCP_GATEWAY_TRANSPORT` (`streamable-http` default, or `stdio`)
- `UNITY_MCP_STREAMABLE_HTTP_URL` (default `http://127.0.0.1:38110/mcp`)
- `UNITY_MCP_CONTROL_TRANSPORT` (`http` or `pipe`)
- `UNITY_MCP_CONTROL_HTTP_URL`
- `UNITY_MCP_CONTROL_PIPE_NAME`
- `UNITY_MCP_CONTROL_TIMEOUT_MS`
- `UNITY_MCP_ALLOWED_PATH_PREFIXES`
- `UNITY_MCP_ALLOWED_COMPONENT_TYPES`

## Additional Docs

- [Control architecture](Documentation~/mcp-control-architecture.md)
- [Editor control window](Documentation~/mcp-editor-control-window.md)
- [Tool catalog](Documentation~/mcp-tool-catalog.md)
- [Runtime tool schemas](Documentation~/mcp-tool-schemas-runtime.md)
- [Authoring tool schemas](Documentation~/mcp-tool-schemas-authoring.md)
- [Migration reference](Documentation~/mcp-tool-migration-reference.md)

Current module taxonomy is target-first and operation-second, for example:

- `project_read`, `project_execute`, `project_write`
- `editor_read`, `editor_execute`, `editor_write`
- `runtime_read`, `runtime_execute`
- `scene_read`, `scene_execute`, `scene_write`
- `gameobject_read`, `gameobject_write`
- `prefab_read`, `prefab_write`, `asset_read`, `asset_write`

Current runtime tool names are canonical names listed in `Gateway~/schemas/*.json` and routed by `Editor/Control/ControlToolDispatcher.cs`. Legacy names are historical only and are no longer part of the active runtime contract.
The current runtime foundation also includes editor selection tools, project build settings and player/project settings read surfaces, and a thin `project_execute` build pipeline for switching active build target and building players.
The current `gameobject_*` foundation includes scene live object operations plus scene component inspection and mutation surfaces.
The current `prefab_read` and `prefab_write` foundations keep a single external family while separating prefab asset content operations from prefab instance summary and override flows.
The current `asset_write` foundation includes import/reimport, text creation, native asset creation, and copy/move style authoring surfaces under the same module.
