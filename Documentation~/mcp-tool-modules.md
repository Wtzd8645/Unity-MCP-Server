# Unity MCP Tool Modules

Status: Draft v0.1  
Goal: Split tools by domain and risk, with explicit `asset_read` / `asset_write`.

## Module layout

1. `core`
- Purpose: editor connectivity and runtime status.
- Tools: `unity_ping`, `unity_project_info`, `unity_playmode_status`, `unity_playmode_start`, `unity_playmode_stop`
- Risk: low

2. `diagnostics`
- Purpose: logs and test execution.
- Tools: `unity_get_console_logs`, `unity_clear_console`, `unity_run_tests`
- Risk: low to medium (`run_tests` is long-running)

3. `scene_read`
- Purpose: scene/object inspection with optional scene switching (`unity_open_scene`).
- Tools: `unity_list_scenes`, `unity_open_scene`, `unity_go_find`, `unity_component_get_fields`
- Risk: low to medium (`unity_open_scene` changes editor loaded-scene state)

4. `scene_write`
- Purpose: mutate GameObjects/components in scenes.
- Tools: `unity_go_create`, `unity_go_delete`, `unity_go_duplicate`, `unity_go_reparent`, `unity_go_rename`, `unity_go_set_active`, `unity_component_add`, `unity_component_remove`, `unity_component_set_fields`
- Risk: medium

5. `prefab_write`
- Purpose: prefab creation/instance mutation workflows.
- Tools: `unity_prefab_create`, `unity_prefab_instantiate`, `unity_prefab_apply_overrides`, `unity_prefab_revert_overrides`, `unity_prefab_unpack`, `unity_prefab_create_variant`
- Risk: medium to high

6. `asset_read`
- Purpose: read/search dependency graph for assets.
- Tools: `unity_asset_search`, `unity_asset_get`, `unity_asset_refs`
- Risk: low

7. `asset_write`
- Purpose: mutate asset files/import state/labels.
- Tools: `unity_asset_move`, `unity_asset_rename`, `unity_asset_delete_to_trash`, `unity_asset_reimport`, `unity_asset_set_labels`
- Risk: high

## Schema files

- `core`: `Gateway~/schemas/mcp-tools-core.input-schemas.json`
- `diagnostics`: `Gateway~/schemas/mcp-tools-diagnostics.input-schemas.json`
- `scene_read`: `Gateway~/schemas/mcp-tools-scene-read.input-schemas.json`
- `scene_write`: `Gateway~/schemas/mcp-tools-scene-write.input-schemas.json`
- `prefab_write`: `Gateway~/schemas/mcp-tools-prefab-write.input-schemas.json`
- `asset_read`: `Gateway~/schemas/mcp-tools-asset-read.input-schemas.json`
- `asset_write`: `Gateway~/schemas/mcp-tools-asset-write.input-schemas.json`
- all tools: `Gateway~/schemas/mcp-tools-all.input-schemas.json`

## Operational docs

- `Documentation~/unity-editor-control.md`: Unity Editor control start/stop and control settings flow.

## Recommended default enablement

- Enable by default: `core`, `diagnostics`, `scene_read`, `asset_read`
- Disabled by default (explicit opt-in): `scene_write`, `prefab_write`, `asset_write`

## Rollout profile examples

1. Read-mostly profile
- Modules: `core`, `diagnostics`, `scene_read`, `asset_read`

2. Safe edit profile
- Modules: `core`, `diagnostics`, `scene_read`, `scene_write`, `asset_read`

3. Full automation profile
- Modules: all modules
- Extra guardrails: path allowlist, type/component allowlist, `dryRun/apply` enforcement



