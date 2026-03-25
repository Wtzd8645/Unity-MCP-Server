# Unity MCP Runtime Tool Schemas

This document summarizes the current schema families for project, editor, runtime, and scene read-execute tools.

## Source of truth

- Validation schemas live under `Gateway~/schemas/`
- Active module membership is defined by `Gateway~/schemas/mcp-tool-modules.json`

This document is a summary. When there is any mismatch, the schema files and dispatcher names are authoritative.

## Module coverage

- `project_read`: `unity_project_ping`, `unity_project_get_info`
- `project_execute`: `unity_project_run_tests`
- `editor_read`: `unity_editor_get_console_logs`
- `editor_write`: `unity_editor_clear_console`
- `runtime_read`: `unity_runtime_get_playmode_status`
- `runtime_execute`: `unity_runtime_start_playmode`, `unity_runtime_stop_playmode`
- `scene_read`: `unity_scene_list`, `unity_scene_list_loaded`, `unity_scene_get_active`, `unity_gameobject_find`
- `scene_execute`: `unity_scene_open`, `unity_scene_set_active`, `unity_scene_close`

## Tool groups

### Project and editor

- `unity_project_ping`: control connectivity and editor heartbeat
- `unity_project_get_info`: project and editor summary, with optional build target data
- `unity_editor_get_console_logs`: incremental console polling
- `unity_editor_clear_console`: editor console reset
- `unity_project_run_tests`: Unity Test Framework execution with timeout handling

### Runtime

- `unity_runtime_get_playmode_status`: current play mode state
- `unity_runtime_start_playmode`: enter play mode with optional wait
- `unity_runtime_stop_playmode`: exit play mode with optional wait

### Scene read and execute

- `unity_scene_list`: scenes from Build Settings and or project assets
- `unity_scene_list_loaded`: currently loaded scenes in editor order
- `unity_scene_get_active`: active loaded scene summary
- `unity_gameobject_find`: GameObject search within loaded scenes
- `unity_scene_open`: open a scene asset into the editor
- `unity_scene_set_active`: switch the active scene among loaded scenes
- `unity_scene_close`: close a loaded scene, optionally saving first

## Shared rules

- Scene paths live under `Assets/` and end with `.unity`
- `execute` tools do not use `dryRun/apply`; they represent editor-state transitions
- Long-running tools such as `unity_project_run_tests` enforce timeouts

## Related docs

- `Documentation~/mcp-tool-catalog.md`
- `Documentation~/mcp-tool-schemas-authoring.md`
