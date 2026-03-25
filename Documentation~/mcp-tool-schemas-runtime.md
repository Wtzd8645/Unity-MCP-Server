# Unity MCP Runtime Tool Schemas

This document summarizes the current schema families for project, editor, runtime, and scene runtime-facing tools.

## Source of truth

- Validation schemas live under `Gateway~/schemas/`
- Active module membership is defined by `Gateway~/schemas/mcp-tool-modules.json`

This document is a summary. When there is any mismatch, the schema files and dispatcher names are authoritative.

## Module coverage

- `project_read`: `unity_project_ping`, `unity_project_get_info`, `unity_project_get_build_settings`, `unity_project_list_build_scenes`, `unity_project_get_player_settings`, `unity_project_get_project_settings`
- `project_execute`: `unity_project_run_tests`, `unity_project_switch_build_target`, `unity_project_build_player`
- `project_write`: `unity_project_set_build_scenes`
- `editor_read`: `unity_editor_get_console_logs`, `unity_editor_get_selection`
- `editor_execute`: `unity_editor_set_selection`, `unity_editor_frame_selection`
- `editor_write`: `unity_editor_clear_console`
- `runtime_read`: `unity_runtime_get_playmode_status`
- `runtime_execute`: `unity_runtime_start_playmode`, `unity_runtime_stop_playmode`
- `scene_read`: `unity_scene_list`, `unity_scene_list_loaded`, `unity_scene_get_active`, `unity_gameobject_find`
- `scene_execute`: `unity_scene_open`, `unity_scene_set_active`, `unity_scene_close`

## Tool groups

### Project and editor

- `unity_project_ping`: control connectivity and editor heartbeat
- `unity_project_get_info`: project and editor summary, with optional build target data
- `unity_project_get_build_settings`: active build target summary and Build Settings counts
- `unity_project_list_build_scenes`: ordered Build Settings scene list
- `unity_project_switch_build_target`: synchronously switch the active Unity build target
- `unity_project_build_player`: build a player for the active build target using enabled Build Settings scenes
- `unity_project_set_build_scenes`: replace the Build Settings scene list with `dryRun/apply`
- `unity_project_get_player_settings`: common PlayerSettings values for the active build target
- `unity_project_get_project_settings`: common project-wide editor settings
- `unity_editor_get_console_logs`: incremental console polling
- `unity_editor_get_selection`: current Unity Editor selection snapshot
- `unity_editor_set_selection`: replace the current Unity Editor selection
- `unity_editor_frame_selection`: frame the current scene selection in SceneView
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
- `unity_project_build_player` only accepts project-relative output paths under `Builds/`
- `unity_project_build_player` uses the active build target and the enabled Build Settings scene list; it does not accept per-call scene overrides
- `project_write` uses `dryRun/apply` and replaces the full Build Settings scene list in the order provided
- `execute` tools do not use `dryRun/apply`; they represent editor-state transitions
- Long-running tools such as `unity_project_run_tests`, `unity_project_switch_build_target`, and `unity_project_build_player` enforce timeouts

## Related docs

- `Documentation~/mcp-tool-catalog.md`
- `Documentation~/mcp-tool-schemas-authoring.md`
