# Unity MCP Server

This repository is the Unity package root and contains two components:

- `Editor/`: Unity Editor control package source (HTTP/Named Pipe tool endpoint)
- `Gateway~/`: MCP gateway source process (.NET, streamable HTTP)

## Unity Editor Control

Use the Control package and open:

- `Tools/Unity MCP Control`

The control window manages both:

- Control endpoint lifecycle (start/stop and settings)
- Gateway process lifecycle (start/stop/restart and process monitoring)

The settings panel currently provides:

- Gateway process settings such as `Dotnet Executable` and `Gateway Project Path`
- `Enabled Modules` as a multi-select dropdown sourced from `Gateway~/schemas/mcp-tool-modules.json`
- `Control Transport` with transport-specific fields shown conditionally
- shared control settings such as timeout, path allowlist, component allowlist, and auto-start flags

By default, the gateway serves MCP Streamable HTTP at `http://127.0.0.1:38110/mcp`.

## Runtime Contract

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

Current runtime tool names are defined by the active schemas in `Gateway~/schemas/` and routed by `Editor/Control/ControlToolDispatcher.cs`.

## Module Taxonomy

The runtime taxonomy is target-first and operation-second:

- `project_read`, `project_execute`, `project_write`
- `editor_read`, `editor_execute`, `editor_write`
- `runtime_read`, `runtime_execute`
- `scene_read`, `scene_execute`, `scene_write`
- `gameobject_read`, `gameobject_write`
- `prefab_read`, `prefab_write`
- `asset_read`, `asset_write`

Current authoring boundaries are:

- `scene_*`: scene container and scene asset lifecycle only
- `gameobject_*`: scene live object inspection and mutation, including scene component operations
- `prefab_*`: prefab module boundary, with explicit `prefab_asset_*` and `prefab_instance_*` tool names under it
- `asset_*`: generic project asset search, inspection, import, creation, and file-level mutation

AI temporary scene workflows should prefer additive scene loading and closing over replacing the user's current loaded scene setup.

Scene context-changing tools use `dirtyEditorContextPolicy` so dirty loaded scenes and dirty Prefab Stage sessions are handled without interactive save prompts.

The current foundation includes:

- project read surfaces plus build-target switch, player build, and test execution tools
- editor selection and console tools
- runtime playmode status and start/stop tools
- scene open/close/create/save tools
- scene live GameObject and component read/write tools
- prefab asset inspection and prefab instance lifecycle/override tools
- asset search/reference tools plus import, copy/move, rename, delete-to-trash, and native asset creation tools

## Additional Docs

- [Control architecture](Documentation~/mcp-control-architecture.md)
- [Editor control window](Documentation~/mcp-editor-control-window.md)
- [Tool catalog](Documentation~/mcp-tool-catalog.md)
- [Runtime tool schemas](Documentation~/mcp-tool-schemas-runtime.md)
- [Authoring tool schemas](Documentation~/mcp-tool-schemas-authoring.md)
