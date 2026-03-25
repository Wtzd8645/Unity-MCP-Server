# Unity MCP Tool Catalog

This document is the current module and tool catalog for the runtime contract exposed by the gateway.

## Source of truth

- Module manifest: `Gateway~/schemas/mcp-tool-modules.json`
- Active tool schemas: `Gateway~/schemas/mcp-tools-*.input-schemas.json`
- Runtime routing: `Editor/Control/ControlToolDispatcher.cs`

Current runtime tool names are the canonical names listed in the schema files and the dispatcher. Legacy names are historical only and are no longer active runtime names.

## Taxonomy

- First axis: the Unity target being operated on
- Second axis: `read`, `write`, or `execute`
- `execute` is reserved for editor-state transitions and procedural actions that are neither pure inspection nor durable content mutation
- `risk` is rollout metadata and does not change the module name

## Module catalog

1. `project_read`
- Tools: `unity_project_ping`, `unity_project_get_info`, `unity_project_get_build_settings`, `unity_project_list_build_scenes`, `unity_project_get_player_settings`, `unity_project_get_project_settings`
- Risk: low
- Enabled by default: yes
- Schema: `Gateway~/schemas/mcp-tools-project-read.input-schemas.json`

2. `project_execute`
- Tools: `unity_project_run_tests`, `unity_project_switch_build_target`, `unity_project_build_player`
- Risk: medium
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-project-execute.input-schemas.json`

3. `project_write`
- Tools: `unity_project_set_build_scenes`
- Risk: medium
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-project-write.input-schemas.json`

4. `editor_read`
- Tools: `unity_editor_get_console_logs`, `unity_editor_get_selection`
- Risk: low
- Enabled by default: yes
- Schema: `Gateway~/schemas/mcp-tools-editor-read.input-schemas.json`

5. `editor_execute`
- Tools: `unity_editor_set_selection`, `unity_editor_frame_selection`
- Risk: low-medium
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-editor-execute.input-schemas.json`

6. `editor_write`
- Tools: `unity_editor_clear_console`
- Risk: medium
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-editor-write.input-schemas.json`

7. `runtime_read`
- Tools: `unity_runtime_get_playmode_status`
- Risk: low
- Enabled by default: yes
- Schema: `Gateway~/schemas/mcp-tools-runtime-read.input-schemas.json`

8. `runtime_execute`
- Tools: `unity_runtime_start_playmode`, `unity_runtime_stop_playmode`
- Risk: medium
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-runtime-execute.input-schemas.json`

9. `scene_read`
- Tools: `unity_scene_list`, `unity_scene_list_loaded`, `unity_scene_get_active`
- Risk: low
- Enabled by default: yes
- Schema: `Gateway~/schemas/mcp-tools-scene-read.input-schemas.json`

10. `scene_execute`
- Tools: `unity_scene_open`, `unity_scene_set_active`, `unity_scene_close`
- Risk: medium
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-scene-execute.input-schemas.json`

11. `scene_write`
- Tools: `unity_scene_create`, `unity_scene_save`, `unity_scene_save_all`
- Risk: medium
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-scene-write.input-schemas.json`

12. `gameobject_read`
- Tools: `unity_gameobject_find`, `unity_gameobject_get`, `unity_gameobject_list_components`, `unity_gameobject_get_component_fields`, `unity_gameobject_get_component_fields_batch`
- Risk: low
- Enabled by default: yes
- Schema: `Gateway~/schemas/mcp-tools-gameobject-read.input-schemas.json`

13. `gameobject_write`
- Tools: `unity_gameobject_create`, `unity_gameobject_delete`, `unity_gameobject_duplicate`, `unity_gameobject_reparent`, `unity_gameobject_rename`, `unity_gameobject_set_active`, `unity_gameobject_set_transform`, `unity_gameobject_set_tag`, `unity_gameobject_set_layer`, `unity_gameobject_set_static`, `unity_gameobject_add_component`, `unity_gameobject_remove_component`, `unity_gameobject_set_component_fields`
- Risk: medium
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-gameobject-write.input-schemas.json`

14. `prefab_read`
- Tools: `unity_prefab_get`, `unity_prefab_get_instance`, `unity_prefab_get_overrides`, `unity_prefab_find_gameobjects`, `unity_prefab_get_gameobject`, `unity_prefab_get_component_fields`
- Risk: low
- Enabled by default: yes
- Schema: `Gateway~/schemas/mcp-tools-prefab-read.input-schemas.json`

15. `prefab_write`
- Tools: `unity_prefab_create`, `unity_prefab_create_instance`, `unity_prefab_apply_overrides`, `unity_prefab_revert_overrides`, `unity_prefab_unpack`, `unity_prefab_create_variant`
- Risk: medium-high
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-prefab-write.input-schemas.json`

16. `asset_read`
- Tools: `unity_asset_find`, `unity_asset_get`, `unity_asset_get_references`
- Risk: low
- Enabled by default: yes
- Schema: `Gateway~/schemas/mcp-tools-asset-read.input-schemas.json`

17. `asset_write`
- Tools: `unity_asset_move`, `unity_asset_rename`, `unity_asset_delete_to_trash`, `unity_asset_reimport`, `unity_asset_import`, `unity_asset_set_labels`, `unity_asset_copy`, `unity_asset_create_folder`, `unity_asset_create_text`, `unity_asset_create_material`, `unity_asset_create_scriptable_object`
- Risk: high
- Enabled by default: no
- Schema: `Gateway~/schemas/mcp-tools-asset-write.input-schemas.json`

## Recommended default enablement

- Enabled by default: `project_read`, `editor_read`, `runtime_read`, `scene_read`, `gameobject_read`, `prefab_read`, `asset_read`
- Disabled by default: `project_execute`, `project_write`, `editor_execute`, `editor_write`, `runtime_execute`, `scene_execute`, `scene_write`, `gameobject_write`, `prefab_write`, `asset_write`

## Rollout profiles

1. Read-mostly profile
- Modules: `project_read`, `editor_read`, `runtime_read`, `scene_read`, `gameobject_read`, `prefab_read`, `asset_read`

2. Safe edit profile
- Modules: `project_read`, `editor_read`, `editor_execute`, `runtime_read`, `scene_read`, `scene_execute`, `gameobject_read`, `prefab_read`, `gameobject_write`, `asset_read`

3. Full automation profile
- Modules: all modules
- Extra guardrails: path allowlist, component allowlist, `dryRun/apply` enforcement

## Related docs

- `Documentation~/mcp-tool-schemas-runtime.md`
- `Documentation~/mcp-tool-schemas-authoring.md`
- `Documentation~/mcp-tool-migration-reference.md`
