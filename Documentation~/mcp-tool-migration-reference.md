# Unity MCP Tool Migration Reference

This document is a historical rename reference for the breaking cutover to canonical tool names.

Current runtime tool names are the canonical names listed in `Gateway~/schemas/*.json` and routed by `Editor/ControlToolDispatcher.cs`. The legacy names below are historical only and are no longer accepted runtime names.

## Canonical naming direction

- Canonical format: `unity_<target>_<verb>`
- Target names use full words and avoid abbreviations such as `go`
- Canonical naming is now the only active runtime naming pattern

## Legacy to canonical map

- `unity_ping` -> `unity_project_ping`
- `unity_project_info` -> `unity_project_get_info`
- `unity_project_get_build_settings` -> `unity_project_get_build_settings`
- `unity_project_list_build_scenes` -> `unity_project_list_build_scenes`
- `unity_project_get_player_settings` -> `unity_project_get_player_settings`
- `unity_project_get_project_settings` -> `unity_project_get_project_settings`
- `unity_project_set_build_scenes` -> `unity_project_set_build_scenes`
- `unity_project_switch_build_target` -> `unity_project_switch_build_target`
- `unity_project_build_player` -> `unity_project_build_player`
- `unity_run_tests` -> `unity_project_run_tests`
- `unity_get_console_logs` -> `unity_editor_get_console_logs`
- `unity_editor_get_selection` -> `unity_editor_get_selection`
- `unity_editor_set_selection` -> `unity_editor_set_selection`
- `unity_editor_frame_selection` -> `unity_editor_frame_selection`
- `unity_clear_console` -> `unity_editor_clear_console`
- `unity_playmode_status` -> `unity_runtime_get_playmode_status`
- `unity_playmode_start` -> `unity_runtime_start_playmode`
- `unity_playmode_stop` -> `unity_runtime_stop_playmode`
- `unity_list_scenes` -> `unity_scene_list`
- `unity_scene_list_loaded` -> `unity_scene_list_loaded`
- `unity_scene_get_active` -> `unity_scene_get_active`
- `unity_open_scene` -> `unity_scene_open`
- `unity_scene_set_active` -> `unity_scene_set_active`
- `unity_scene_close` -> `unity_scene_close`
- `unity_scene_create` -> `unity_scene_create`
- `unity_scene_save` -> `unity_scene_save`
- `unity_scene_save_all` -> `unity_scene_save_all`
- `unity_go_find` -> `unity_gameobject_find`
- `unity_gameobject_get` -> `unity_gameobject_get`
- `unity_go_create` -> `unity_gameobject_create`
- `unity_go_delete` -> `unity_gameobject_delete`
- `unity_go_duplicate` -> `unity_gameobject_duplicate`
- `unity_go_reparent` -> `unity_gameobject_reparent`
- `unity_go_rename` -> `unity_gameobject_rename`
- `unity_go_set_active` -> `unity_gameobject_set_active`
- `unity_gameobject_set_transform` -> `unity_gameobject_set_transform`
- `unity_gameobject_set_tag` -> `unity_gameobject_set_tag`
- `unity_gameobject_set_layer` -> `unity_gameobject_set_layer`
- `unity_gameobject_set_static` -> `unity_gameobject_set_static`
- `unity_component_list` -> `unity_component_list`
- `unity_component_get_fields` -> `unity_component_get_fields`
- `unity_component_get_fields_batch` -> `unity_component_get_fields_batch`
- `unity_component_add` -> `unity_component_add`
- `unity_component_remove` -> `unity_component_remove`
- `unity_component_set_fields` -> `unity_component_set_fields`
- `unity_prefab_create` -> `unity_prefab_create`
- `unity_prefab_get` -> `unity_prefab_get`
- `unity_prefab_get_overrides` -> `unity_prefab_get_overrides`
- `unity_prefab_instantiate` -> `unity_prefab_create_instance`
- `unity_prefab_apply_overrides` -> `unity_prefab_apply_overrides`
- `unity_prefab_revert_overrides` -> `unity_prefab_revert_overrides`
- `unity_prefab_unpack` -> `unity_prefab_unpack`
- `unity_prefab_create_variant` -> `unity_prefab_create_variant`
- `unity_asset_copy` -> `unity_asset_copy`
- `unity_asset_import` -> `unity_asset_import`
- `unity_asset_create_folder` -> `unity_asset_create_folder`
- `unity_asset_create_text` -> `unity_asset_create_text`
- `unity_asset_create_material` -> `unity_asset_create_material`
- `unity_asset_create_scriptable_object` -> `unity_asset_create_scriptable_object`
- `unity_asset_search` -> `unity_asset_find`
- `unity_asset_get` -> `unity_asset_get`
- `unity_asset_refs` -> `unity_asset_get_references`
- `unity_asset_move` -> `unity_asset_move`
- `unity_asset_rename` -> `unity_asset_rename`
- `unity_asset_delete_to_trash` -> `unity_asset_delete_to_trash`
- `unity_asset_reimport` -> `unity_asset_reimport`
- `unity_asset_set_labels` -> `unity_asset_set_labels`

## Cutover status

- The active runtime contract has completed the canonical rename cutover.
- Schemas, dispatcher routing, and public docs now use canonical names only.
- Clients still using legacy names must update to the canonical names listed above.
