namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal static class BridgeToolDispatcher
    {
        public static BridgeToolCallResponse Dispatch(
            BridgeToolCallRequest request,
            UnityBridgeLogStore logStore,
            string bridgeVersion,
            MainThreadActionInvoker mainThreadInvoker)
        {
            return request.name switch
            {
                "unity.ping" => CoreToolHandlers.HandlePing(bridgeVersion),
                "unity.project_info" => CoreToolHandlers.HandleProjectInfo(request),
                "unity.playmode_status" => CoreToolHandlers.HandlePlaymodeStatus(),
                "unity.playmode_start" => CoreToolHandlers.HandlePlaymodeStart(request, mainThreadInvoker),
                "unity.playmode_stop" => CoreToolHandlers.HandlePlaymodeStop(request, mainThreadInvoker),
                "unity.list_scenes" => SceneReadToolHandlers.HandleListScenes(request),
                "unity.open_scene" => SceneReadToolHandlers.HandleOpenScene(request),
                "unity.go_find" => SceneReadToolHandlers.HandleGoFind(request),
                "unity.component_get_fields" => SceneReadToolHandlers.HandleComponentGetFields(request),
                "unity.go_create" => SceneWriteToolHandlers.HandleGoCreate(request),
                "unity.go_delete" => SceneWriteToolHandlers.HandleGoDelete(request),
                "unity.go_duplicate" => SceneWriteToolHandlers.HandleGoDuplicate(request),
                "unity.go_reparent" => SceneWriteToolHandlers.HandleGoReparent(request),
                "unity.go_rename" => SceneWriteToolHandlers.HandleGoRename(request),
                "unity.go_set_active" => SceneWriteToolHandlers.HandleGoSetActive(request),
                "unity.component_add" => SceneWriteToolHandlers.HandleComponentAdd(request),
                "unity.component_remove" => SceneWriteToolHandlers.HandleComponentRemove(request),
                "unity.component_set_fields" => SceneWriteToolHandlers.HandleComponentSetFields(request),
                "unity.get_console_logs" => DiagnosticsToolHandlers.HandleGetConsoleLogs(request, logStore),
                "unity.clear_console" => DiagnosticsToolHandlers.HandleClearConsole(logStore),
                "unity.run_tests" => DiagnosticsToolHandlers.HandleRunTests(request, mainThreadInvoker),
                "unity.asset_search" => AssetReadToolHandlers.HandleAssetSearch(request),
                "unity.asset_get" => AssetReadToolHandlers.HandleAssetGet(request),
                "unity.asset_refs" => AssetReadToolHandlers.HandleAssetRefs(request),
                "unity.prefab_create" => PrefabWriteToolHandlers.HandlePrefabCreate(request),
                "unity.prefab_instantiate" => PrefabWriteToolHandlers.HandlePrefabInstantiate(request),
                "unity.prefab_apply_overrides" => PrefabWriteToolHandlers.HandlePrefabApplyOverrides(request),
                "unity.prefab_revert_overrides" => PrefabWriteToolHandlers.HandlePrefabRevertOverrides(request),
                "unity.prefab_unpack" => PrefabWriteToolHandlers.HandlePrefabUnpack(request),
                "unity.prefab_create_variant" => PrefabWriteToolHandlers.HandlePrefabCreateVariant(request),
                "unity.asset_move" => AssetWriteToolHandlers.HandleAssetMove(request),
                "unity.asset_rename" => AssetWriteToolHandlers.HandleAssetRename(request),
                "unity.asset_delete_to_trash" => AssetWriteToolHandlers.HandleAssetDeleteToTrash(request),
                "unity.asset_reimport" => AssetWriteToolHandlers.HandleAssetReimport(request),
                "unity.asset_set_labels" => AssetWriteToolHandlers.HandleAssetSetLabels(request),
                _ => BridgeResponses.NotImplemented(request.name),
            };
        }
    }
}
