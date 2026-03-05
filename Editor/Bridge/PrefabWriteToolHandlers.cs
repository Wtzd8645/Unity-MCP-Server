#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blanketmen.UnityMcpBridge.Editor
{
    internal static class PrefabWriteToolHandlers
    {
        public static BridgeToolCallResponse HandlePrefabCreate(BridgeToolCallRequest request)
        {
            PrefabCreateArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new PrefabCreateArgs
                {
                    connectToInstance = true,
                    overwrite = false,
                    dryRun = true,
                    apply = false,
                });

            if (args.source == null || string.IsNullOrEmpty(args.outputPath))
            {
                return BridgeResponses.Error("source and outputPath are required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            if (!BridgeWriteSupport.TryResolveGameObject(args.source, out GameObject source, out string resolveError))
            {
                return BridgeResponses.Error(resolveError, "not_found", request.name);
            }

            if (!args.overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(args.outputPath) != null)
            {
                return BridgeResponses.Error("Prefab already exists: " + args.outputPath, "already_exists", request.name);
            }

            string folderPath = Path.GetDirectoryName(args.outputPath).Replace('\\', '/');
            if (!BridgeWriteSupport.TryValidateFolderPath(folderPath, out string folderError))
            {
                return BridgeResponses.Error(folderError, "permission_denied", request.name);
            }

            if (shouldApply && !BridgeWriteSupport.EnsureFolderPathExists(folderPath, out folderError))
            {
                return BridgeResponses.Error(folderError, "invalid_argument", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = BridgeWriteSupport.BuildGoMutationItem(source, "prefab_create");
            item.path = args.outputPath;
            item.target = args.outputPath;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Prefab create planned.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            bool success;
            GameObject prefabAsset;
            if (args.connectToInstance)
            {
                prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(source, args.outputPath, InteractionMode.AutomatedAction, out success);
            }
            else
            {
                prefabAsset = PrefabUtility.SaveAsPrefabAsset(source, args.outputPath, out success);
            }

            if (!success || prefabAsset == null)
            {
                item.status = "failed";
                item.changed = false;
                item.message = "Failed to create prefab asset.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            item.status = "succeeded";
            item.changed = true;
            item.path = AssetDatabase.GetAssetPath(prefabAsset);
            item.guid = AssetDatabase.AssetPathToGUID(item.path);
            item.message = "Prefab created.";
            return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static BridgeToolCallResponse HandlePrefabInstantiate(BridgeToolCallRequest request)
        {
            PrefabInstantiateArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new PrefabInstantiateArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.prefab == null)
            {
                return BridgeResponses.Error("prefab is required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            if (!BridgeWriteSupport.TryResolveAssetRef(args.prefab, out string prefabPath, out _))
            {
                return BridgeResponses.Error("Prefab target not found.", "not_found", request.name);
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null || !PrefabUtility.IsPartOfPrefabAsset(prefabAsset))
            {
                return BridgeResponses.Error("Prefab asset not found: " + prefabPath, "not_found", request.name);
            }

            GameObject parent = null;
            if (args.parent != null)
            {
                if (!BridgeWriteSupport.TryResolveGameObject(args.parent, out parent, out string parentError))
                {
                    return BridgeResponses.Error(parentError, "not_found", request.name);
                }
            }

            Scene targetScene;
            if (parent != null)
            {
                targetScene = parent.scene;
            }
            else if (!BridgeWriteSupport.TryGetSceneByPath(args.scenePath, out targetScene))
            {
                return BridgeResponses.Error("Scene not loaded: " + args.scenePath, "not_found", request.name);
            }

            var items = new List<MutationItem>();
            var item = new MutationItem
            {
                action = "prefab_instantiate",
                target = prefabPath,
                path = prefabPath,
                status = "planned",
                scenePath = targetScene.path,
            };
            items.Add(item);

            if (!shouldApply)
            {
                item.message = "Prefab instantiate planned.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            if (instance == null)
            {
                item.status = "failed";
                item.message = "Failed to instantiate prefab.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            Undo.RegisterCreatedObjectUndo(instance, "MCP Instantiate Prefab");
            if (parent != null)
            {
                instance.transform.SetParent(parent.transform, false);
            }
            else if (targetScene.IsValid() && instance.scene != targetScene)
            {
                SceneManager.MoveGameObjectToScene(instance, targetScene);
            }

            BridgeWriteSupport.ApplyTransform(instance.transform, args.transform);

            item.status = "succeeded";
            item.changed = true;
            item.target = instance.name;
            item.globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(instance).ToString();
            item.scenePath = instance.scene.path;
            item.hierarchyPath = BuildHierarchyPath(instance.transform);
            item.message = "Prefab instantiated.";
            return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static BridgeToolCallResponse HandlePrefabApplyOverrides(BridgeToolCallRequest request)
        {
            PrefabApplyOverridesArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new PrefabApplyOverridesArgs
                {
                    includePropertyOverrides = true,
                    includeAddedComponents = true,
                    includeRemovedComponents = true,
                    dryRun = true,
                    apply = false,
                });

            if (args.instances == null || args.instances.Length == 0)
            {
                return BridgeResponses.Error("instances is required.", "invalid_argument", request.name);
            }

            if (!args.includePropertyOverrides || !args.includeAddedComponents || !args.includeRemovedComponents)
            {
                return BridgeResponses.Error(
                    "Selective apply options are not supported yet. Set include* options to true.",
                    "unsupported",
                    request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            return HandlePrefabInstanceBatch(request.name, args.instances, shouldApply, "prefab_apply_overrides", (root) =>
            {
                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
            });
        }

        public static BridgeToolCallResponse HandlePrefabRevertOverrides(BridgeToolCallRequest request)
        {
            PrefabRevertOverridesArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new PrefabRevertOverridesArgs
                {
                    includePropertyOverrides = true,
                    includeAddedComponents = true,
                    includeRemovedComponents = true,
                    dryRun = true,
                    apply = false,
                });

            if (args.instances == null || args.instances.Length == 0)
            {
                return BridgeResponses.Error("instances is required.", "invalid_argument", request.name);
            }

            if (!args.includePropertyOverrides || !args.includeAddedComponents || !args.includeRemovedComponents)
            {
                return BridgeResponses.Error(
                    "Selective revert options are not supported yet. Set include* options to true.",
                    "unsupported",
                    request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            return HandlePrefabInstanceBatch(request.name, args.instances, shouldApply, "prefab_revert_overrides", (root) =>
            {
                PrefabUtility.RevertPrefabInstance(root, InteractionMode.AutomatedAction);
            });
        }

        public static BridgeToolCallResponse HandlePrefabUnpack(BridgeToolCallRequest request)
        {
            PrefabUnpackArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new PrefabUnpackArgs
                {
                    mode = "OutermostRoot",
                    dryRun = true,
                    apply = false,
                });

            if (args.instances == null || args.instances.Length == 0)
            {
                return BridgeResponses.Error("instances is required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            PrefabUnpackMode unpackMode = string.Equals(args.mode, "Completely", StringComparison.OrdinalIgnoreCase)
                ? PrefabUnpackMode.Completely
                : PrefabUnpackMode.OutermostRoot;

            return HandlePrefabInstanceBatch(request.name, args.instances, shouldApply, "prefab_unpack", (root) =>
            {
                PrefabUtility.UnpackPrefabInstance(root, unpackMode, InteractionMode.AutomatedAction);
            });
        }

        public static BridgeToolCallResponse HandlePrefabCreateVariant(BridgeToolCallRequest request)
        {
            PrefabCreateVariantArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new PrefabCreateVariantArgs
                {
                    overwrite = false,
                    dryRun = true,
                    apply = false,
                });

            if (args.basePrefab == null || string.IsNullOrEmpty(args.outputPath))
            {
                return BridgeResponses.Error("basePrefab and outputPath are required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            if (!BridgeWriteSupport.TryResolveAssetRef(args.basePrefab, out string basePrefabPath, out _))
            {
                return BridgeResponses.Error("basePrefab not found.", "not_found", request.name);
            }

            GameObject basePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            if (basePrefabAsset == null || !PrefabUtility.IsPartOfPrefabAsset(basePrefabAsset))
            {
                return BridgeResponses.Error("basePrefab is not a prefab asset.", "invalid_argument", request.name);
            }

            if (!args.overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(args.outputPath) != null)
            {
                return BridgeResponses.Error("Prefab already exists: " + args.outputPath, "already_exists", request.name);
            }

            string folderPath = Path.GetDirectoryName(args.outputPath).Replace('\\', '/');
            if (!BridgeWriteSupport.TryValidateFolderPath(folderPath, out string folderError))
            {
                return BridgeResponses.Error(folderError, "permission_denied", request.name);
            }

            if (shouldApply && !BridgeWriteSupport.EnsureFolderPathExists(folderPath, out folderError))
            {
                return BridgeResponses.Error(folderError, "invalid_argument", request.name);
            }

            GameObject sourceInstance = null;
            if (args.sourceInstance != null)
            {
                if (!BridgeWriteSupport.TryResolveGameObject(args.sourceInstance, out sourceInstance, out string sourceError))
                {
                    return BridgeResponses.Error(sourceError, "not_found", request.name);
                }

                string sourceAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sourceInstance);
                if (!string.Equals(sourceAssetPath, basePrefabPath, StringComparison.OrdinalIgnoreCase))
                {
                    return BridgeResponses.Error(
                        "sourceInstance is not an instance of basePrefab.",
                        "invalid_argument",
                        request.name);
                }
            }

            var items = new List<MutationItem>();
            MutationItem item = new MutationItem
            {
                action = "prefab_create_variant",
                target = args.outputPath,
                path = args.outputPath,
                status = "planned",
            };
            items.Add(item);

            if (!shouldApply)
            {
                item.message = "Prefab variant create planned.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            GameObject variantSource = sourceInstance;
            bool destroySourceAfterSave = false;
            try
            {
                if (variantSource == null)
                {
                    variantSource = PrefabUtility.InstantiatePrefab(basePrefabAsset) as GameObject;
                    if (variantSource == null)
                    {
                        item.status = "failed";
                        item.message = "Failed to instantiate base prefab for variant creation.";
                        return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
                    }

                    destroySourceAfterSave = true;
                }

                GameObject variantAsset = PrefabUtility.SaveAsPrefabAsset(variantSource, args.outputPath, out bool success);
                if (!success || variantAsset == null)
                {
                    item.status = "failed";
                    item.message = "Failed to create prefab variant.";
                    return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
                }

                item.status = "succeeded";
                item.changed = true;
                item.path = AssetDatabase.GetAssetPath(variantAsset);
                item.guid = AssetDatabase.AssetPathToGUID(item.path);
                item.message = "Prefab variant created.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
            finally
            {
                if (destroySourceAfterSave && variantSource != null)
                {
                    UnityEngine.Object.DestroyImmediate(variantSource);
                }
            }
        }

        private static BridgeToolCallResponse HandlePrefabInstanceBatch(
            string toolName,
            GameObjectRef[] instanceRefs,
            bool shouldApply,
            string action,
            Action<GameObject> mutator)
        {
            var items = new List<MutationItem>();
            var processedRoots = new HashSet<int>();

            for (int i = 0; i < instanceRefs.Length; i++)
            {
                GameObjectRef instanceRef = instanceRefs[i];
                if (!BridgeWriteSupport.TryResolveGameObject(instanceRef, out GameObject resolved, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = action,
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(resolved);
                if (root == null || !PrefabUtility.IsPartOfPrefabInstance(root))
                {
                    items.Add(new MutationItem
                    {
                        action = action,
                        target = resolved.name,
                        status = "failed",
                        changed = false,
                        message = "Target is not part of a prefab instance.",
                    });
                    continue;
                }

                if (!processedRoots.Add(root.GetInstanceID()))
                {
                    items.Add(new MutationItem
                    {
                        action = action,
                        target = root.name,
                        status = "skipped",
                        changed = false,
                        message = "Same instance root already handled.",
                    });
                    continue;
                }

                MutationItem item = BridgeWriteSupport.BuildGoMutationItem(root, action);
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.changed = false;
                    item.message = action + " planned.";
                    continue;
                }

                mutator(root);
                item.status = "succeeded";
                item.changed = true;
                item.message = action + " applied.";
            }

            return BridgeWriteSupport.BuildMutationResponse(toolName, shouldApply, instanceRefs.Length, items);
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
#endif

