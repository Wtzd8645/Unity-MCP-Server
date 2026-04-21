# Unity MCP Authoring Tool Schemas

This document summarizes the current schema families for scene write, gameobject, prefab, and asset tools.

## Source of truth

- Validation schemas live under `Gateway~/schemas/`
- Active module membership is defined by `Gateway~/schemas/mcp-tool-modules.json`

This document is a summary. When there is any mismatch, the schema files and dispatcher names are authoritative.

All active authoring tool names use canonical naming. Legacy names are historical only and are documented separately in the migration reference.
Prefab tools keep the external `prefab_read` / `prefab_write` module boundary while using explicit `prefab_asset_*` and `prefab_instance_*` tool names inside those modules.

## Module coverage

- `scene_write`: `unity_scene_create`, `unity_scene_save`, `unity_scene_save_all`
- `gameobject_read`: `unity_gameobject_find`, `unity_gameobject_get`, `unity_gameobject_list_components`, `unity_gameobject_get_component_fields`, `unity_gameobject_get_component_fields_batch`
- `gameobject_write`: `unity_gameobject_create`, `unity_gameobject_delete`, `unity_gameobject_duplicate`, `unity_gameobject_reparent`, `unity_gameobject_rename`, `unity_gameobject_set_active`, `unity_gameobject_set_transform`, `unity_gameobject_set_tag`, `unity_gameobject_set_layer`, `unity_gameobject_set_static`, `unity_gameobject_add_component`, `unity_gameobject_remove_component`, `unity_gameobject_set_component_fields`
- `prefab_read`: `unity_prefab_asset_get`, `unity_prefab_instance_get`, `unity_prefab_instance_get_overrides`, `unity_prefab_asset_find_gameobjects`, `unity_prefab_asset_get_gameobject`, `unity_prefab_asset_get_component_fields`
- `prefab_write`: `unity_prefab_asset_create`, `unity_prefab_instance_create`, `unity_prefab_instance_apply_overrides`, `unity_prefab_instance_revert_overrides`, `unity_prefab_instance_unpack`, `unity_prefab_asset_create_variant`
- `asset_read`: `unity_asset_find`, `unity_asset_get`, `unity_asset_get_references`
- `asset_write`: `unity_asset_move`, `unity_asset_rename`, `unity_asset_delete_to_trash`, `unity_asset_reimport`, `unity_asset_import`, `unity_asset_set_labels`, `unity_asset_copy`, `unity_asset_create_folder`, `unity_asset_create_text`, `unity_asset_create_material`, `unity_asset_create_scriptable_object`

## Read surfaces

- `unity_gameobject_find`: search loaded scene objects by hierarchy, metadata, and component filters
- `unity_gameobject_get`: single-object snapshot with identifiers, hierarchy, tag, layer, static state, parent summary, optional child summary, component summary, and local or world transform data
- `unity_gameobject_list_components`: ordered component inventory for one scene GameObject
- `unity_gameobject_get_component_fields`: serialized field snapshot for one scene component
- `unity_gameobject_get_component_fields_batch`: read-only batch field snapshot for multiple scene component ids
- `unity_prefab_asset_get`: prefab asset summary with source info
- `unity_prefab_instance_get`: prefab instance summary with source info and override counts
- `unity_prefab_instance_get_overrides`: detailed prefab override inspection for one instance root
- `unity_prefab_asset_find_gameobjects`: flattened search over GameObjects inside one prefab asset
- `unity_prefab_asset_get_gameobject`: single prefab asset GameObject snapshot with hierarchy, local transform, and component summary
- `unity_prefab_asset_get_component_fields`: serialized field snapshot for one component inside a prefab asset
- `unity_asset_find`, `unity_asset_get`, `unity_asset_get_references`: project asset search, lookup, and reference inspection

## Write surfaces

- `unity_scene_create`: create and save a new scene asset
- `unity_scene_save`: save the active or specified loaded scene
- `unity_scene_save_all`: save all loaded scenes that already have asset paths
- `unity_gameobject_create`, `unity_gameobject_delete`, `unity_gameobject_duplicate`, `unity_gameobject_reparent`, `unity_gameobject_rename`, `unity_gameobject_set_active`: current GameObject mutation tools
- `unity_gameobject_set_transform`, `unity_gameobject_set_tag`, `unity_gameobject_set_layer`, `unity_gameobject_set_static`: current GameObject property mutation tools
- `unity_gameobject_add_component`, `unity_gameobject_remove_component`, `unity_gameobject_set_component_fields`: scene component mutation tools
- `unity_prefab_asset_create`, `unity_prefab_instance_create`, `unity_prefab_instance_apply_overrides`, `unity_prefab_instance_revert_overrides`, `unity_prefab_instance_unpack`, `unity_prefab_asset_create_variant`: prefab mutation tools
- `unity_asset_move`, `unity_asset_rename`, `unity_asset_delete_to_trash`, `unity_asset_reimport`, `unity_asset_import`, `unity_asset_set_labels`, `unity_asset_copy`: asset move, rename, delete, import, reimport, label, and copy tools
- `unity_asset_create_folder`: create one folder path under `Assets/`
- `unity_asset_create_text`: create or overwrite one UTF-8 text asset and import it immediately
- `unity_asset_create_material`: create one `.mat` asset bound to an explicit shader
- `unity_asset_create_scriptable_object`: create one `.asset` for a concrete `ScriptableObject` type

## Shared mutation rules

- Write schemas support `dryRun` and `apply` as an inverse pair
- If either `dryRun` or `apply` is provided, the other must also be provided
- If both are omitted, schemas and handlers default to dry-run behavior with `dryRun=true` and `apply=false`
- Batch write is used only when one payload applies to multiple targets
- Paths remain limited to allowed project-relative `Assets/` locations
- Scene component writes remain constrained by the component allowlist

## Risk notes

- `prefab_write` stays opt-in because it can combine scene and asset side effects
- `asset_write` is the highest-risk module and should be enabled last

## Related docs

- `Documentation~/mcp-tool-catalog.md`
- `Documentation~/mcp-tool-schemas-runtime.md`
