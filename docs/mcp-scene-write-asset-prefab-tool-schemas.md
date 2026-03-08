# Unity MCP Scene-Write, Asset, and Prefab Tool Schemas

Status: Draft v0.1  
Scope: Asset/Object/GameObject/Prefab operations

## Module coverage

- `asset_read` schema: `Unity-MCP-Gateway/schemas/mcp-tools-asset-read.input-schemas.json`
- Tools: `unity_asset_search`, `unity_asset_get`, `unity_asset_refs`
- `scene_read` schema: `Unity-MCP-Gateway/schemas/mcp-tools-scene-read.input-schemas.json`
- Tools: `unity_go_find`, `unity_component_get_fields`
- `scene_write` schema: `Unity-MCP-Gateway/schemas/mcp-tools-scene-write.input-schemas.json`
- Tools: `unity_go_create`, `unity_go_delete`, `unity_go_duplicate`, `unity_go_reparent`, `unity_go_rename`, `unity_go_set_active`, `unity_component_add`, `unity_component_remove`, `unity_component_set_fields`
- `prefab_write` schema: `Unity-MCP-Gateway/schemas/mcp-tools-prefab-write.input-schemas.json`
- Tools: `unity_prefab_create`, `unity_prefab_instantiate`, `unity_prefab_apply_overrides`, `unity_prefab_revert_overrides`, `unity_prefab_unpack`, `unity_prefab_create_variant`
- `asset_write` schema: `Unity-MCP-Gateway/schemas/mcp-tools-asset-write.input-schemas.json`
- Tools: `unity_asset_move`, `unity_asset_rename`, `unity_asset_delete_to_trash`, `unity_asset_reimport`, `unity_asset_set_labels`

## 1) Cross-tool conventions

### 1.1 Mutating tools must be explicit
Every mutating tool in this document supports:

```json
{
  "dryRun": true,
  "apply": false
}
```

Execution rule:
- Preview only (default): `dryRun=true`, `apply=false`
- Real execution: `dryRun=false`, `apply=true`
- Any other combination is invalid (`invalid_argument`)

### 1.2 Common response envelope

```ts
type BridgeToolCallResponse = {
  isError: boolean;
  contentText: string;
  structuredContentJson?: string; // JSON string of success payload or error payload
};

type ErrorStructuredContent = {
  status:
    | "invalid_argument"
    | "not_found"
    | "cancelled"
    | "unsupported"
    | "tool_timeout"
    | "tool_exception"
    | "internal_error";
  message: string;
  tool?: string;
};

type MutationResult = {
  tool: string;
  dryRun: boolean;
  applied: boolean;
  requested: number;
  succeeded: number;
  failed: number;
  items: MutationItem[];
  warnings?: string[];
};

type MutationItem = {
  target?: string;
  action: string;
  status: "planned" | "succeeded" | "failed";
  changed: boolean;
  message?: string;
  scenePath?: string;
  hierarchyPath?: string;
  path?: string;
  guid?: string;
  globalObjectId?: string;
  componentType?: string;
  componentId?: string;
};
```

### 1.3 Shared refs and value types

```ts
type AssetRef =
  | { guid: string; path?: never }
  | { path: `Assets/${string}`; guid?: never };

type GameObjectRef =
  | { globalObjectId: string } // preferred
  | { scenePath: `Assets/${string}.unity`; hierarchyPath: string };

type Vec3 = { x: number; y: number; z: number };
type TransformInput = {
  position?: Vec3;
  rotationEuler?: Vec3;
  scale?: Vec3;
  local?: boolean; // default true
};
```

Safety defaults:
- Only `Assets/` paths are allowed.
- Destructive operations are blocked unless `apply=true`.
- Component and menu access should be allowlisted in server config.

### 1.4 Module boundary for assets

- `asset_read`: `unity_asset_search`, `unity_asset_get`, `unity_asset_refs`
- `asset_write`: `unity_asset_move`, `unity_asset_rename`, `unity_asset_delete_to_trash`, `unity_asset_reimport`, `unity_asset_set_labels`
- Recommendation: keep read/write modules separately enabled for permission isolation.

## 2) Module: asset_read

### 2.1 `unity_asset_search`

Purpose: Search assets by type/name/label/path.

Input:
```ts
{
  query?: string;
  types?: string[]; // e.g. Prefab, Texture2D, ScriptableObject
  labels?: string[];
  pathPrefixes?: `Assets/${string}`[]; // default ["Assets/"]
  includePackages?: boolean; // default false
  limit?: number; // 1..500, default 100
  offset?: number; // >= 0
  sortBy?: "name" | "path" | "type" | "modifiedTime"; // default "path"
  sortOrder?: "asc" | "desc"; // default "asc"
}
```

Output:
```ts
{
  total: number;
  items: Array<{
    guid: string;
    path: string;
    name: string;
    type: string;
    labels: string[];
    isMainAsset: boolean;
    modifiedTimeUtc?: string;
  }>;
}
```

### 2.2 `unity_asset_get`

Purpose: Read one asset's metadata and optional graph info.

Input:
```ts
{
  target: AssetRef;
  includeDependencies?: boolean; // default true
  includeDependents?: boolean; // default false
  includeMeta?: boolean; // default false
}
```

Output:
```ts
{
  asset: {
    guid: string;
    path: string;
    name: string;
    type: string;
    fileSizeBytes?: number;
    labels: string[];
    importerType?: string;
  };
  dependencies?: Array<{ guid: string; path: string; type: string }>;
  dependents?: Array<{ guid: string; path: string; type: string }>;
  meta?: Record<string, unknown>;
}
```

### 2.3 `unity_asset_refs`

Purpose: Traverse references for one asset.

Input:
```ts
{
  target: AssetRef;
  direction?: "inbound" | "outbound"; // default "inbound"
  recursive?: boolean; // default false
  maxDepth?: number; // default 1
  filterTypes?: string[];
}
```

Output:
```ts
{
  nodes: Array<{ guid: string; path: string; type: string }>;
  edges: Array<{ fromGuid: string; toGuid: string; kind: "reference" }>;
}
```

## 3) Module: scene_write

Note: `unity_go_find` and `unity_component_get_fields` are `scene_read` tools kept in this document because write flows commonly depend on them.

### 3.1 `unity_go_find`

Purpose: Find scene objects by conditions.

Input:
```ts
{
  scenePath?: `Assets/${string}.unity`; // default active scene
  namePattern?: string;
  tag?: string;
  layer?: number;
  isActive?: boolean;
  hasComponents?: string[];
  hierarchyPathPrefix?: string;
  inSelection?: boolean; // default false
  limit?: number; // 1..1000, default 200
  offset?: number; // >= 0
}
```

Output:
```ts
{
  total: number;
  items: Array<{
    globalObjectId: string;
    scenePath: string;
    hierarchyPath: string;
    name: string;
    activeSelf: boolean;
    tag: string;
    layer: number;
    componentTypes: string[];
  }>;
}
```

### 3.2 `unity_go_create`

Purpose: Create a new GameObject.

Input:
```ts
{
  scenePath?: `Assets/${string}.unity`;
  name: string;
  parent?: GameObjectRef;
  transform?: TransformInput;
  components?: Array<{ type: string; fields?: Record<string, unknown> }>;
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 3.3 `unity_go_delete`

Purpose: Delete one or many GameObjects.

Input:
```ts
{
  targets: GameObjectRef[];
  mode?: "undoable" | "immediate"; // default "undoable"
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 3.4 `unity_go_duplicate`

Purpose: Duplicate one or many GameObjects.

Input:
```ts
{
  targets: GameObjectRef[];
  parent?: GameObjectRef;
  renamePattern?: string; // e.g. "{name}_Copy"
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 3.5 `unity_go_reparent`

Purpose: Move objects under a new parent (or scene root).

Input:
```ts
{
  targets: GameObjectRef[];
  newParent?: GameObjectRef; // omit => scene root
  worldPositionStays?: boolean; // default true
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 3.6 `unity_go_rename`

Input:
```ts
{
  target: GameObjectRef;
  newName: string;
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 3.7 `unity_go_set_active`

Input:
```ts
{
  targets: GameObjectRef[];
  active: boolean;
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 3.8 `unity_component_add`

Input:
```ts
{
  target: GameObjectRef;
  componentType: string; // must be allowlisted
  fields?: Record<string, unknown>;
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 3.9 `unity_component_remove`

Input:
```ts
{
  target: GameObjectRef;
  componentType?: string;
  componentId?: string;
  dryRun?: boolean;
  apply?: boolean;
}
```

Validation:
- Exactly one of `componentType` or `componentId` must be provided.

Output:
```ts
MutationResult
```

### 3.10 `unity_component_get_fields`

Input:
```ts
{
  target: GameObjectRef;
  componentType?: string;
  componentId?: string;
  includePrivateSerialized?: boolean; // default false
}
```

Output:
```ts
{
  componentId: string;
  componentType: string;
  fields: Array<{
    name: string;
    fieldType: string;
    value: unknown;
    serialized: boolean;
    readOnly: boolean;
  }>;
}
```

### 3.11 `unity_component_set_fields`

Input:
```ts
{
  target: GameObjectRef;
  componentType?: string;
  componentId?: string;
  fields: Record<string, unknown>;
  strict?: boolean; // default true
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

## 4) Module: prefab_write

### 4.1 `unity_prefab_create`

Purpose: Save a scene object as a prefab.

Input:
```ts
{
  source: GameObjectRef;
  outputPath: `Assets/${string}.prefab`;
  connectToInstance?: boolean; // default true
  overwrite?: boolean; // default false
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 4.2 `unity_prefab_instantiate`

Input:
```ts
{
  prefab: AssetRef;
  scenePath?: `Assets/${string}.unity`;
  parent?: GameObjectRef;
  transform?: TransformInput;
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 4.3 `unity_prefab_apply_overrides`

Input:
```ts
{
  instances: GameObjectRef[];
  includePropertyOverrides?: boolean; // default true
  includeAddedComponents?: boolean; // default true
  includeRemovedComponents?: boolean; // default true
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 4.4 `unity_prefab_revert_overrides`

Input:
```ts
{
  instances: GameObjectRef[];
  includePropertyOverrides?: boolean; // default true
  includeAddedComponents?: boolean; // default true
  includeRemovedComponents?: boolean; // default true
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 4.5 `unity_prefab_unpack`

Input:
```ts
{
  instances: GameObjectRef[];
  mode?: "OutermostRoot" | "Completely"; // default "OutermostRoot"
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 4.6 `unity_prefab_create_variant`

Input:
```ts
{
  basePrefab: AssetRef;
  outputPath: `Assets/${string}.prefab`;
  sourceInstance?: GameObjectRef; // optional overrides source
  overwrite?: boolean; // default false
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

## 5) Module: asset_write

### 5.1 `unity_asset_move`

Input:
```ts
{
  targets: AssetRef[];
  destinationFolder: `Assets/${string}`;
  conflictPolicy?: "fail" | "overwrite" | "rename"; // default "fail"
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 5.2 `unity_asset_rename`

Input:
```ts
{
  target: AssetRef;
  newName: string; // name without folder path
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 5.3 `unity_asset_delete_to_trash`

Input:
```ts
{
  targets: AssetRef[];
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 5.4 `unity_asset_reimport`

Input:
```ts
{
  targets: AssetRef[];
  recursive?: boolean; // default false
  forceUpdate?: boolean; // default false
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

### 5.5 `unity_asset_set_labels`

Input:
```ts
{
  target: AssetRef;
  mode: "set" | "add" | "remove";
  labels: string[];
  dryRun?: boolean;
  apply?: boolean;
}
```

Output:
```ts
MutationResult
```

## 6) Recommended implementation order

1. Enable and validate `asset_read` first.
2. Enable `scene_write` and keep `dryRun=true` as default.
3. Enable `prefab_write` after scene mutation flows are stable.
4. Enable `asset_write` last (highest risk).

## 7) Acceptance checklist

- All mutating tools enforce `dryRun/apply` rule.
- All responses use a unified envelope and error code set.
- Every mutating tool emits `MutationResult.items[]`.
- All object/asset identifiers resolve by `guid`/`globalObjectId` first.
- All operations are restricted to allowlisted paths/types/components.






