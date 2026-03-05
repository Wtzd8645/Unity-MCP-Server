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
            switch (request.name)
            {
                case "unity.ping":
                    return CoreToolHandlers.HandlePing(bridgeVersion);
                case "unity.project_info":
                    return CoreToolHandlers.HandleProjectInfo(request);
                case "unity.playmode_status":
                    return CoreToolHandlers.HandlePlaymodeStatus();
                case "unity.playmode_start":
                    return CoreToolHandlers.HandlePlaymodeStart(request, mainThreadInvoker);
                case "unity.playmode_stop":
                    return CoreToolHandlers.HandlePlaymodeStop(request, mainThreadInvoker);

                case "unity.list_scenes":
                    return SceneReadToolHandlers.HandleListScenes(request);
                case "unity.open_scene":
                    return SceneReadToolHandlers.HandleOpenScene(request);
                case "unity.go_find":
                    return SceneReadToolHandlers.HandleGoFind(request);
                case "unity.component_get_fields":
                    return SceneReadToolHandlers.HandleComponentGetFields(request);

                case "unity.go_create":
                    return SceneWriteToolHandlers.HandleGoCreate(request);
                case "unity.go_delete":
                    return SceneWriteToolHandlers.HandleGoDelete(request);
                case "unity.go_duplicate":
                    return SceneWriteToolHandlers.HandleGoDuplicate(request);
                case "unity.go_reparent":
                    return SceneWriteToolHandlers.HandleGoReparent(request);
                case "unity.go_rename":
                    return SceneWriteToolHandlers.HandleGoRename(request);
                case "unity.go_set_active":
                    return SceneWriteToolHandlers.HandleGoSetActive(request);
                case "unity.component_add":
                    return SceneWriteToolHandlers.HandleComponentAdd(request);
                case "unity.component_remove":
                    return SceneWriteToolHandlers.HandleComponentRemove(request);
                case "unity.component_set_fields":
                    return SceneWriteToolHandlers.HandleComponentSetFields(request);

                case "unity.get_console_logs":
                    return DiagnosticsToolHandlers.HandleGetConsoleLogs(request, logStore);
                case "unity.clear_console":
                    return DiagnosticsToolHandlers.HandleClearConsole(logStore);
                case "unity.run_tests":
                    return DiagnosticsToolHandlers.HandleRunTests(request, mainThreadInvoker);

                case "unity.asset_search":
                    return AssetReadToolHandlers.HandleAssetSearch(request);
                case "unity.asset_get":
                    return AssetReadToolHandlers.HandleAssetGet(request);
                case "unity.asset_refs":
                    return AssetReadToolHandlers.HandleAssetRefs(request);
                case "unity.prefab_create":
                    return PrefabWriteToolHandlers.HandlePrefabCreate(request);
                case "unity.prefab_instantiate":
                    return PrefabWriteToolHandlers.HandlePrefabInstantiate(request);
                case "unity.prefab_apply_overrides":
                    return PrefabWriteToolHandlers.HandlePrefabApplyOverrides(request);
                case "unity.prefab_revert_overrides":
                    return PrefabWriteToolHandlers.HandlePrefabRevertOverrides(request);
                case "unity.prefab_unpack":
                    return PrefabWriteToolHandlers.HandlePrefabUnpack(request);
                case "unity.prefab_create_variant":
                    return PrefabWriteToolHandlers.HandlePrefabCreateVariant(request);
                case "unity.asset_move":
                    return AssetWriteToolHandlers.HandleAssetMove(request);
                case "unity.asset_rename":
                    return AssetWriteToolHandlers.HandleAssetRename(request);
                case "unity.asset_delete_to_trash":
                    return AssetWriteToolHandlers.HandleAssetDeleteToTrash(request);
                case "unity.asset_reimport":
                    return AssetWriteToolHandlers.HandleAssetReimport(request);
                case "unity.asset_set_labels":
                    return AssetWriteToolHandlers.HandleAssetSetLabels(request);

                default:
                    return BridgeResponses.NotImplemented(request.name);
            }
        }
    }
}
