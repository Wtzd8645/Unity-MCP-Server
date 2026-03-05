# Unity MCP Bridge (Skeleton)

This package is the Unity Editor-side bridge skeleton.

Current state:

- Adds menu commands to start/stop bridge server.
- Adds menu commands to start/stop full server (bridge + host).
- Adds server control window for runtime status, settings, and logs.
- Exposes local HTTP endpoint: `POST /mcp/tool/call`
- Exposes local Named Pipe endpoint: `unity-mcp-bridge`
- Implemented tools:
  - `unity.ping`
  - `unity.project_info`
  - `unity.playmode_status`
  - `unity.playmode_start`
  - `unity.playmode_stop`
  - `unity.list_scenes`
  - `unity.open_scene`
  - `unity.get_console_logs`
  - `unity.clear_console`
  - `unity.go_find`
  - `unity.component_get_fields`
  - `unity.go_create`
  - `unity.go_delete`
  - `unity.go_duplicate`
  - `unity.go_reparent`
  - `unity.go_rename`
  - `unity.go_set_active`
  - `unity.component_add`
  - `unity.component_remove`
  - `unity.component_set_fields`
  - `unity.asset_search`
  - `unity.asset_get`
  - `unity.asset_refs`
  - `unity.prefab_create`
  - `unity.prefab_instantiate`
  - `unity.prefab_apply_overrides`
  - `unity.prefab_revert_overrides`
  - `unity.prefab_unpack`
  - `unity.prefab_create_variant`
  - `unity.asset_move`
  - `unity.asset_rename`
  - `unity.asset_delete_to_trash`
  - `unity.asset_reimport`
  - `unity.asset_set_labels`
- `unity.run_tests` is implemented via Unity Test Framework reflection APIs (requires `com.unity.test-framework`)
  - when `includeXmlReportPath=true`, bridge writes `Library/McpReports/latest-test-results.xml`
- Selective include flags for `prefab_apply_overrides` / `prefab_revert_overrides` are currently `unsupported` (all include flags must be `true`).

Code layout:

- `UnityMcpBridgeServer.cs`: transport and lifecycle only (HTTP/Named Pipe + main-thread execution)
- `BridgeToolDispatcher.cs`: tool routing
- `CoreToolHandlers.cs`: core tools
- `DiagnosticsToolHandlers.cs`: diagnostics tools
- `SceneReadToolHandlers.cs`: scene read tools
- `SceneWriteToolHandlers.cs`: scene write tools
- `AssetReadToolHandlers.cs`: asset read tools
- `AssetWriteToolHandlers.cs`: asset write tools
- `PrefabWriteToolHandlers.cs`: prefab write tools
- `UnityBridgeLogStore.cs`: in-memory log buffer for diagnostics
- `BridgeContracts.cs`: request/response and DTO contracts
- `BridgeSupport.cs`: shared JSON/response/utility helpers
- `BridgeWriteSupport.cs`: write-tool shared helpers
- `BridgeMiniJson.cs`: dynamic JSON parser for write-tool field payloads
- `RunTestsReflectionRunner.cs`: Unity Test Framework runner bridge (`unity.run_tests`)
- `UnityMcpHostSettings.cs`: persisted host launch and bridge integration settings
- `UnityMcpHostSupervisor.cs`: host process lifecycle and startup probe
- `UnityMcpServerWindow.cs`: full server control UI

Default runtime values:

- HTTP URL: `http://127.0.0.1:38100/`
- Pipe name: `unity-mcp-bridge`

Full-server note:

- Host process startup in Unity uses `dotnet run --project ...` and validates startup via `initialize` + `tools/list`.
- Host transport is currently stdio-only, so this Unity-managed host is best for Unity-side orchestration/validation. External MCP clients typically still launch host process directly.

Next step:

- Add finer-grained prefab override filtering support.

