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
                "unity_ping" => CoreToolHandlers.HandlePing(bridgeVersion),
                "unity_project_info" => CoreToolHandlers.HandleProjectInfo(request),
                "unity_playmode_status" => CoreToolHandlers.HandlePlaymodeStatus(),
                "unity_playmode_start" => CoreToolHandlers.HandlePlaymodeStart(request, mainThreadInvoker),
                "unity_playmode_stop" => CoreToolHandlers.HandlePlaymodeStop(request, mainThreadInvoker),
                "unity_list_scenes" => SceneReadToolHandlers.HandleListScenes(request),
                "unity_open_scene" => SceneReadToolHandlers.HandleOpenScene(request),
                "unity_go_find" => SceneReadToolHandlers.HandleGoFind(request),
                "unity_component_get_fields" => SceneReadToolHandlers.HandleComponentGetFields(request),
                "unity_go_create" => SceneWriteToolHandlers.HandleGoCreate(request),
                "unity_go_delete" => SceneWriteToolHandlers.HandleGoDelete(request),
                "unity_go_duplicate" => SceneWriteToolHandlers.HandleGoDuplicate(request),
                "unity_go_reparent" => SceneWriteToolHandlers.HandleGoReparent(request),
                "unity_go_rename" => SceneWriteToolHandlers.HandleGoRename(request),
                "unity_go_set_active" => SceneWriteToolHandlers.HandleGoSetActive(request),
                "unity_component_add" => SceneWriteToolHandlers.HandleComponentAdd(request),
                "unity_component_remove" => SceneWriteToolHandlers.HandleComponentRemove(request),
                "unity_component_set_fields" => SceneWriteToolHandlers.HandleComponentSetFields(request),
                "unity_get_console_logs" => DiagnosticsToolHandlers.HandleGetConsoleLogs(request, logStore),
                "unity_clear_console" => DiagnosticsToolHandlers.HandleClearConsole(logStore),
                "unity_run_tests" => DiagnosticsToolHandlers.HandleRunTests(request, mainThreadInvoker),
                "unity_asset_search" => AssetReadToolHandlers.HandleAssetSearch(request),
                "unity_asset_get" => AssetReadToolHandlers.HandleAssetGet(request),
                "unity_asset_refs" => AssetReadToolHandlers.HandleAssetRefs(request),
                "unity_prefab_create" => PrefabWriteToolHandlers.HandlePrefabCreate(request),
                "unity_prefab_instantiate" => PrefabWriteToolHandlers.HandlePrefabInstantiate(request),
                "unity_prefab_apply_overrides" => PrefabWriteToolHandlers.HandlePrefabApplyOverrides(request),
                "unity_prefab_revert_overrides" => PrefabWriteToolHandlers.HandlePrefabRevertOverrides(request),
                "unity_prefab_unpack" => PrefabWriteToolHandlers.HandlePrefabUnpack(request),
                "unity_prefab_create_variant" => PrefabWriteToolHandlers.HandlePrefabCreateVariant(request),
                "unity_asset_move" => AssetWriteToolHandlers.HandleAssetMove(request),
                "unity_asset_rename" => AssetWriteToolHandlers.HandleAssetRename(request),
                "unity_asset_delete_to_trash" => AssetWriteToolHandlers.HandleAssetDeleteToTrash(request),
                "unity_asset_reimport" => AssetWriteToolHandlers.HandleAssetReimport(request),
                "unity_asset_set_labels" => AssetWriteToolHandlers.HandleAssetSetLabels(request),
                _ => BridgeResponses.NotImplemented(request.name),
            };
        }
    }
}

