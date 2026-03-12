# Unity MCP Core, Diagnostics, and Scene-Read Tool Schemas

Status: Draft v0.1  
Scope: Core, diagnostics, and scene-read tools for project/scene/playmode/logs/tests

## 1) Scope

Core and diagnostics target:
- Verify Unity editor connectivity.
- Read project and scene metadata.
- Control Play Mode.
- Read and clear Console logs.
- Run EditMode tests and return summary.

Out of scope in this bundle:
- Asset bulk mutation and refactor tools.
- Prefab override workflows.
- Arbitrary code execution.

## Module coverage

- `core` schema: `Unity-MCP-Gateway/schemas/mcp-tools-core.input-schemas.json`
- Tools: `unity_ping`, `unity_project_info`, `unity_playmode_status`, `unity_playmode_start`, `unity_playmode_stop`
- `diagnostics` schema: `Unity-MCP-Gateway/schemas/mcp-tools-diagnostics.input-schemas.json`
- Tools: `unity_get_console_logs`, `unity_clear_console`, `unity_run_tests`
- `scene_read` schema: `Unity-MCP-Gateway/schemas/mcp-tools-scene-read.input-schemas.json`
- Tools in this document: `unity_list_scenes`, `unity_open_scene`
- Cross-reference: `unity_go_find` and `unity_component_get_fields` are documented in `mcp-scene-write-asset-prefab-tool-schemas.md` because they are heavily used by write flows.

## 2) Cross-tool conventions

### 2.1 Common response envelope

```ts
type ControlToolCallResponse = {
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
```

### 2.2 Shared constraints

- Scene paths must be under `Assets/` and end with `.unity`.
- `limit` fields must be validated (tool-specific bounds).
- All tools should return deterministic, machine-readable fields (no plain-text-only payloads).
- Long-running tools (`run_tests`) must enforce timeout.

## 3) Tool schemas (core + diagnostics + scene_read)

### 3.1 `unity_ping`

Purpose: Verify control connectivity and editor heartbeat.

Input:
```ts
{}
```

Output:
```ts
{
  connected: boolean;
  controlVersion: string;
  editor: {
    isResponding: boolean;
    isPlaying: boolean;
    isPaused: boolean;
    isCompiling: boolean;
  };
  serverTimeUtc: string;
}
```

### 3.2 `unity_project_info`

Purpose: Return project/runtime/editor basics.

Input:
```ts
{
  includePlatformMatrix?: boolean; // default false
}
```

Output:
```ts
{
  projectName: string;
  projectPath: string;
  unityVersion: string;
  companyName?: string;
  productName?: string;
  activeBuildTarget: string;
  activeBuildTargetGroup: string;
  editorState: {
    isPlaying: boolean;
    isPaused: boolean;
    isCompiling: boolean;
    isUpdating: boolean;
  };
  supportedBuildTargets?: string[];
}
```

### 3.3 `unity_list_scenes`

Purpose: List scenes from Build Settings and/or project assets.

Input:
```ts
{
  source?: "buildSettings" | "assets" | "both"; // default "both"
  includeDisabled?: boolean; // default true
  limit?: number; // 1..500, default 200
  offset?: number; // >= 0
}
```

Output:
```ts
{
  total: number;
  items: Array<{
    path: `Assets/${string}.unity`;
    name: string;
    guid?: string;
    inBuildSettings: boolean;
    enabledInBuildSettings?: boolean;
    buildIndex?: number; // when in Build Settings
  }>;
}
```

### 3.4 `unity_open_scene`

Purpose: Open a scene in Editor (this mutates loaded-scene/editor state).

Input:
```ts
{
  scenePath: `Assets/${string}.unity`;
  openMode?: "Single" | "Additive" | "AdditiveWithoutLoading"; // default "Single"
  saveModifiedScenes?: boolean; // default false
  setActive?: boolean; // default true
}
```

Output:
```ts
{
  openedScenePath: string;
  activeScenePath: string;
  loadedScenes: string[];
}
```

### 3.5 `unity_playmode_status`

Purpose: Query play mode runtime state.

Input:
```ts
{}
```

Output:
```ts
{
  isPlaying: boolean;
  isPaused: boolean;
  isChangingPlaymode: boolean;
}
```

### 3.6 `unity_playmode_start`

Purpose: Enter play mode and optionally wait until state transition completes.

Input:
```ts
{
  waitForEntered?: boolean; // default true
  timeoutMs?: number; // 1000..120000, default 15000
}
```

Output:
```ts
{
  entered: boolean;
  stopped: boolean;
  stateBefore: "EditMode" | "EnteringPlayMode" | "PlayMode" | "ExitingPlayMode";
  stateAfter: "EditMode" | "EnteringPlayMode" | "PlayMode" | "ExitingPlayMode";
  elapsedMs: number;
  waitRequested: boolean;
  timeoutMs: number;
}
```

### 3.7 `unity_playmode_stop`

Purpose: Exit play mode and optionally wait until complete.

Input:
```ts
{
  waitForExited?: boolean; // default true
  timeoutMs?: number; // 1000..120000, default 15000
}
```

Output:
```ts
{
  entered: boolean;
  stopped: boolean;
  stateBefore: "EditMode" | "EnteringPlayMode" | "PlayMode" | "ExitingPlayMode";
  stateAfter: "EditMode" | "EnteringPlayMode" | "PlayMode" | "ExitingPlayMode";
  elapsedMs: number;
  waitRequested: boolean;
  timeoutMs: number;
}
```

### 3.8 `unity_get_console_logs`

Purpose: Read Unity Console messages with incremental polling.

Input:
```ts
{
  levels?: Array<"log" | "warning" | "error" | "exception" | "assert">;
  sinceId?: string; // opaque cursor from previous response
  limit?: number; // 1..2000, default 200
  order?: "asc" | "desc"; // default "desc"
  includeStackTrace?: boolean; // default true
}
```

Output:
```ts
{
  nextSinceId: string;
  returned: number;
  items: Array<{
    id: string;
    level: "log" | "warning" | "error" | "exception" | "assert";
    message: string;
    stackTrace?: string;
    timestampUtc?: string;
  }>;
}
```

### 3.9 `unity_clear_console`

Purpose: Clear Unity Console.

Input:
```ts
{}
```

Output:
```ts
{
  cleared: boolean;
  clearedEditorConsole: boolean;
}
```

### 3.10 `unity_run_tests`

Purpose: Run Unity Test Framework tests (defaults to EditMode).

Input:
```ts
{
  mode?: "EditMode" | "PlayMode"; // default "EditMode"
  filter?: {
    assemblyNames?: string[];
    testNames?: string[];
    categoryNames?: string[];
  };
  timeoutMs?: number; // 5000..3600000, default 600000
  includePassed?: boolean; // default false
  includeXmlReportPath?: boolean; // default true
}
```

Output:
```ts
{
  runId: string;
  mode: "EditMode" | "PlayMode";
  summary: {
    total: number;
    passed: number;
    failed: number;
    skipped: number;
    inconclusive: number;
    durationMs: number;
  };
  results: Array<{
    fullName: string;
    outcome: "Passed" | "Failed" | "Skipped" | "Inconclusive";
    durationMs: number;
    message?: string;
    stackTrace?: string;
    filePath?: string;
    line: number;
    hasLine: boolean;
  }>;
  artifacts?: {
    xmlReportPath?: string;
  };
}
```

## 4) Acceptance checklist

- `ping`, `project_info`, `list_scenes` return stable machine-readable payloads.
- `open_scene` reliably opens a valid scene and reports active scene.
- `playmode_start/stop/status` are state-consistent under normal editor load.
- `get_console_logs` supports cursor polling via `sinceId`.
- `run_tests` returns summary and failed-case diagnostics with timeout handling.

## 5) Recommended implementation order

1. `unity_ping`
2. `unity_project_info`
3. `unity_list_scenes`
4. `unity_open_scene`
5. `unity_playmode_status/start/stop`
6. `unity_get_console_logs`
7. `unity_clear_console`
8. `unity_run_tests`









