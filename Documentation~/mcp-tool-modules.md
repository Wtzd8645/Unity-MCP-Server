# Unity MCP Tool Modules

Status: Draft v0.1  
Goal: Split tools by domain and risk, with explicit `asset_read` / `asset_write`.

## Module layout

1. `core`
- Purpose: editor connectivity and runtime status.
- Tools: `unity.ping`, `unity.project_info`, `unity.playmode_status`, `unity.playmode_start`, `unity.playmode_stop`
- Risk: low

2. `diagnostics`
- Purpose: logs and test execution.
- Tools: `unity.get_console_logs`, `unity.clear_console`, `unity.run_tests`
- Risk: low to medium (`run_tests` is long-running)

3. `scene_read`
- Purpose: scene/object inspection with optional scene switching (`unity.open_scene`).
- Tools: `unity.list_scenes`, `unity.open_scene`, `unity.go_find`, `unity.component_get_fields`
- Risk: low to medium (`unity.open_scene` changes editor loaded-scene state)

4. `scene_write`
- Purpose: mutate GameObjects/components in scenes.
- Tools: `unity.go_create`, `unity.go_delete`, `unity.go_duplicate`, `unity.go_reparent`, `unity.go_rename`, `unity.go_set_active`, `unity.component_add`, `unity.component_remove`, `unity.component_set_fields`
- Risk: medium

5. `prefab_write`
- Purpose: prefab creation/instance mutation workflows.
- Tools: `unity.prefab_create`, `unity.prefab_instantiate`, `unity.prefab_apply_overrides`, `unity.prefab_revert_overrides`, `unity.prefab_unpack`, `unity.prefab_create_variant`
- Risk: medium to high

6. `asset_read`
- Purpose: read/search dependency graph for assets.
- Tools: `unity.asset_search`, `unity.asset_get`, `unity.asset_refs`
- Risk: low

7. `asset_write`
- Purpose: mutate asset files/import state/labels.
- Tools: `unity.asset_move`, `unity.asset_rename`, `unity.asset_delete_to_trash`, `unity.asset_reimport`, `unity.asset_set_labels`
- Risk: high

## Schema files

- `core`: `Editor/Host~/schemas/mcp-tools-core.input-schemas.json`
- `diagnostics`: `Editor/Host~/schemas/mcp-tools-diagnostics.input-schemas.json`
- `scene_read`: `Editor/Host~/schemas/mcp-tools-scene-read.input-schemas.json`
- `scene_write`: `Editor/Host~/schemas/mcp-tools-scene-write.input-schemas.json`
- `prefab_write`: `Editor/Host~/schemas/mcp-tools-prefab-write.input-schemas.json`
- `asset_read`: `Editor/Host~/schemas/mcp-tools-asset-read.input-schemas.json`
- `asset_write`: `Editor/Host~/schemas/mcp-tools-asset-write.input-schemas.json`
- all tools: `Editor/Host~/schemas/mcp-tools-all.input-schemas.json`

## Operational docs

- `Documentation~/unity-editor-server-control.md`: Unity Editor full-server start/stop and supervision flow.

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
