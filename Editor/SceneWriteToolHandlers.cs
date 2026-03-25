using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blanketmen.UnityMcp.Control.Editor
{
    internal static class SceneWriteToolHandlers
    {
        public static ControlToolCallResponse HandleGoCreate(ControlToolCallRequest request)
        {
            GoCreateArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GoCreateArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (string.IsNullOrEmpty(args.name))
            {
                return ControlResponses.Error("name is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            GameObject parentObject = null;
            if (args.parent != null)
            {
                if (!ControlWriteSupport.TryResolveGameObject(args.parent, out parentObject, out string parentError))
                {
                    return ControlResponses.Error(parentError, "not_found", request.name);
                }
            }

            Scene targetScene = default(Scene);
            if (!string.IsNullOrEmpty(args.scenePath))
            {
                if (!ControlWriteSupport.TryGetSceneByPath(args.scenePath, out targetScene))
                {
                    return ControlResponses.Error("Scene not loaded: " + args.scenePath, "not_found", request.name);
                }
            }

            List<GoComponentSpec> componentSpecs = ControlWriteSupport.ExtractGoComponentSpecs(request.argumentsJson);
            for (int i = 0; i < componentSpecs.Count; i++)
            {
                string componentTypeName = componentSpecs[i].type;
                Type componentType = ControlWriteSupport.FindTypeByName(componentTypeName);
                if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                {
                    return ControlResponses.Error("Unknown component type: " + componentTypeName, "invalid_argument", request.name);
                }

                if (!ControlWriteSupport.TryValidateComponentTypeAllowed(componentType, out string allowError))
                {
                    return ControlResponses.Error(allowError, "permission_denied", request.name);
                }
            }

            var items = new List<MutationItem>();
            var item = new MutationItem
            {
                target = args.name,
                action = "go_create",
                status = "planned",
                changed = false,
                scenePath = !string.IsNullOrEmpty(args.scenePath)
                    ? args.scenePath
                    : (parentObject == null ? SceneManager.GetActiveScene().path : parentObject.scene.path),
                hierarchyPath = parentObject == null ? args.name : BuildChildHierarchyPath(parentObject.transform, args.name),
            };
            items.Add(item);

            if (!shouldApply)
            {
                item.message = "GameObject creation planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            GameObject created = null;
            try
            {
                created = new GameObject(args.name);
                Undo.RegisterCreatedObjectUndo(created, "MCP Create GameObject");

                if (parentObject != null)
                {
                    created.transform.SetParent(parentObject.transform, false);
                }
                else if (targetScene.IsValid() && created.scene != targetScene)
                {
                    SceneManager.MoveGameObjectToScene(created, targetScene);
                }

                ControlWriteSupport.ApplyTransform(created.transform, args.transform);

                for (int i = 0; i < componentSpecs.Count; i++)
                {
                    GoComponentSpec spec = componentSpecs[i];
                    Type componentType = ControlWriteSupport.FindTypeByName(spec.type);
                    Component component = Undo.AddComponent(created, componentType);
                    if (component == null)
                    {
                        throw new InvalidOperationException("Failed to add component: " + spec.type);
                    }

                    if (spec.fields != null && spec.fields.Count > 0)
                    {
                        if (!ControlWriteSupport.TryApplyComponentFields(component, spec.fields, true, out _, out string fieldError))
                        {
                            throw new InvalidOperationException(fieldError);
                        }
                    }
                }

                item.status = "succeeded";
                item.changed = true;
                item.target = created.name;
                item.globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(created).ToString();
                item.scenePath = created.scene.path;
                item.hierarchyPath = BuildHierarchyPath(created.transform);
                item.message = "GameObject created.";
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
            catch (Exception ex)
            {
                if (created != null)
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }

                return ControlResponses.Error("Failed to create GameObject: " + ex.Message, "tool_exception", request.name);
            }
        }

        public static ControlToolCallResponse HandleGoDelete(ControlToolCallRequest request)
        {
            GoDeleteArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GoDeleteArgs
                {
                    mode = "undoable",
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return ControlResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            bool immediate = string.Equals(args.mode, "immediate", StringComparison.OrdinalIgnoreCase);
            var items = new List<MutationItem>();

            for (int i = 0; i < args.targets.Length; i++)
            {
                GameObjectRef targetRef = args.targets[i];
                if (!ControlWriteSupport.TryResolveGameObject(targetRef, out GameObject target, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "go_delete",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "go_delete");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "Deletion planned.";
                    continue;
                }

                if (immediate)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
                else
                {
                    Undo.DestroyObjectImmediate(target);
                }

                item.status = "succeeded";
                item.changed = true;
                item.message = "GameObject deleted.";
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleGoDuplicate(ControlToolCallRequest request)
        {
            GoDuplicateArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GoDuplicateArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return ControlResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            GameObject parentOverride = null;
            if (args.parent != null)
            {
                if (!ControlWriteSupport.TryResolveGameObject(args.parent, out parentOverride, out string parentError))
                {
                    return ControlResponses.Error(parentError, "not_found", request.name);
                }
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                GameObjectRef targetRef = args.targets[i];
                if (!ControlWriteSupport.TryResolveGameObject(targetRef, out GameObject source, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "go_duplicate",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = ControlWriteSupport.BuildGoMutationItem(source, "go_duplicate");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.changed = false;
                    item.message = "Duplication planned.";
                    item.target = ControlWriteSupport.BuildRename(args.renamePattern, source.name, i + 1);
                    continue;
                }

                Transform duplicateParent = parentOverride == null ? source.transform.parent : parentOverride.transform;
                GameObject duplicate = duplicateParent == null
                    ? UnityEngine.Object.Instantiate(source)
                    : UnityEngine.Object.Instantiate(source, duplicateParent);
                Undo.RegisterCreatedObjectUndo(duplicate, "MCP Duplicate GameObject");

                duplicate.name = ControlWriteSupport.BuildRename(args.renamePattern, source.name, i + 1);
                item.target = duplicate.name;
                item.status = "succeeded";
                item.changed = true;
                item.globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(duplicate).ToString();
                item.scenePath = duplicate.scene.path;
                item.hierarchyPath = BuildHierarchyPath(duplicate.transform);
                item.message = "GameObject duplicated.";
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleGoReparent(ControlToolCallRequest request)
        {
            GoReparentArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GoReparentArgs
                {
                    worldPositionStays = true,
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return ControlResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            GameObject newParent = null;
            if (args.newParent != null)
            {
                if (!ControlWriteSupport.TryResolveGameObject(args.newParent, out newParent, out string parentError))
                {
                    return ControlResponses.Error(parentError, "not_found", request.name);
                }
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                GameObjectRef targetRef = args.targets[i];
                if (!ControlWriteSupport.TryResolveGameObject(targetRef, out GameObject target, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "go_reparent",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "go_reparent");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "Reparent planned.";
                    continue;
                }

                Undo.RecordObject(target.transform, "MCP Reparent GameObject");
                target.transform.SetParent(newParent == null ? null : newParent.transform, args.worldPositionStays);
                item.status = "succeeded";
                item.changed = true;
                item.scenePath = target.scene.path;
                item.hierarchyPath = BuildHierarchyPath(target.transform);
                item.message = "GameObject reparented.";
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleGoRename(ControlToolCallRequest request)
        {
            GoRenameArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GoRenameArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null || string.IsNullOrEmpty(args.newName))
            {
                return ControlResponses.Error("target and newName are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "go_rename");
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.target = args.newName;
                item.message = "Rename planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            Undo.RecordObject(target, "MCP Rename GameObject");
            target.name = args.newName;
            EditorUtility.SetDirty(target);

            item.status = "succeeded";
            item.changed = true;
            item.target = target.name;
            item.hierarchyPath = BuildHierarchyPath(target.transform);
            item.message = "GameObject renamed.";
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static ControlToolCallResponse HandleGoSetActive(ControlToolCallRequest request)
        {
            GoSetActiveArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GoSetActiveArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return ControlResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                GameObjectRef targetRef = args.targets[i];
                if (!ControlWriteSupport.TryResolveGameObject(targetRef, out GameObject target, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "go_set_active",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "go_set_active");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "SetActive planned: " + args.active;
                    continue;
                }

                Undo.RecordObject(target, "MCP Set Active");
                target.SetActive(args.active);
                item.status = "succeeded";
                item.changed = true;
                item.message = "Active state updated.";
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleGameObjectSetTransform(ControlToolCallRequest request)
        {
            GameObjectSetTransformArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GameObjectSetTransformArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null || args.transform == null || !HasTransformPatch(args.transform))
            {
                return ControlResponses.Error("target and transform patch are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "gameobject_set_transform");
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Transform update planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            Undo.RecordObject(target.transform, "MCP Set GameObject Transform");
            ControlWriteSupport.ApplyTransform(target.transform, args.transform);
            EditorUtility.SetDirty(target.transform);

            item.status = "succeeded";
            item.changed = true;
            item.message = "Transform updated.";
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static ControlToolCallResponse HandleGameObjectSetTag(ControlToolCallRequest request)
        {
            GameObjectSetTagArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GameObjectSetTagArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0 || string.IsNullOrEmpty(args.tag))
            {
                return ControlResponses.Error("targets and tag are required.", "invalid_argument", request.name);
            }

            if (Array.IndexOf(InternalEditorUtility.tags, args.tag) < 0)
            {
                return ControlResponses.Error("Unknown tag: " + args.tag, "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                if (!ControlWriteSupport.TryResolveGameObject(args.targets[i], out GameObject target, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "gameobject_set_tag",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "gameobject_set_tag");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "Tag update planned: " + args.tag;
                    continue;
                }

                Undo.RecordObject(target, "MCP Set GameObject Tag");
                target.tag = args.tag;
                EditorUtility.SetDirty(target);
                item.status = "succeeded";
                item.changed = true;
                item.message = "Tag updated.";
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleGameObjectSetLayer(ControlToolCallRequest request)
        {
            GameObjectSetLayerArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GameObjectSetLayerArgs
                {
                    layer = -1,
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0 || args.layer < 0 || args.layer > 31)
            {
                return ControlResponses.Error("targets and layer (0..31) are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                if (!ControlWriteSupport.TryResolveGameObject(args.targets[i], out GameObject target, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "gameobject_set_layer",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "gameobject_set_layer");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "Layer update planned: " + args.layer;
                    continue;
                }

                Undo.RecordObject(target, "MCP Set GameObject Layer");
                target.layer = args.layer;
                EditorUtility.SetDirty(target);
                item.status = "succeeded";
                item.changed = true;
                item.message = "Layer updated.";
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleGameObjectSetStatic(ControlToolCallRequest request)
        {
            GameObjectSetStaticArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GameObjectSetStaticArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return ControlResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                if (!ControlWriteSupport.TryResolveGameObject(args.targets[i], out GameObject target, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "gameobject_set_static",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "gameobject_set_static");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "Static flag update planned: " + args.isStatic;
                    continue;
                }

                Undo.RecordObject(target, "MCP Set GameObject Static");
                target.isStatic = args.isStatic;
                EditorUtility.SetDirty(target);
                item.status = "succeeded";
                item.changed = true;
                item.message = "Static flag updated.";
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleSceneCreate(ControlToolCallRequest request)
        {
            SceneCreateArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new SceneCreateArgs
                {
                    setup = "EmptyScene",
                    openMode = "Additive",
                    setActive = true,
                    overwrite = false,
                    saveModifiedScenes = false,
                    dryRun = true,
                    apply = false,
                });

            if (string.IsNullOrEmpty(args.outputPath))
            {
                return ControlResponses.Error("outputPath is required.", "invalid_argument", request.name);
            }

            if (!TryNormalizeSceneAssetPath(args.outputPath, out string outputPath, out string pathError))
            {
                return ControlResponses.Error(pathError, "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            var items = new List<MutationItem>();
            MutationItem item = BuildSceneMutationItem(outputPath, "scene_create");
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Scene creation planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            NewSceneMode openMode = ParseNewSceneMode(args.openMode);
            if (openMode == NewSceneMode.Single && HasDirtyLoadedScenes())
            {
                if (!args.saveModifiedScenes)
                {
                    return ControlResponses.Error("Refusing to create a scene in Single mode while modified scenes are open. Set saveModifiedScenes=true or use Additive.", "invalid_argument", request.name);
                }

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return ControlResponses.Error("Scene creation cancelled while saving modified scenes.", "cancelled", request.name);
                }
            }

            string existingPath = AssetDatabase.LoadAssetAtPath<SceneAsset>(outputPath) == null ? null : outputPath;
            if (!string.IsNullOrEmpty(existingPath) && !args.overwrite)
            {
                return ControlResponses.Error("Scene already exists: " + outputPath, "invalid_argument", request.name);
            }

            string folderPath = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folderPath) && !ControlWriteSupport.EnsureFolderPathExists(folderPath, out string folderError))
            {
                return ControlResponses.Error(folderError, "invalid_argument", request.name);
            }

            Scene createdScene = default(Scene);
            try
            {
                createdScene = EditorSceneManager.NewScene(ParseNewSceneSetup(args.setup), openMode);
                if (!createdScene.IsValid())
                {
                    return ControlResponses.Error("Failed to create scene.", "tool_exception", request.name);
                }

                if (!EditorSceneManager.SaveScene(createdScene, outputPath))
                {
                    return ControlResponses.Error("Failed to save created scene: " + outputPath, "tool_exception", request.name);
                }

                if (args.setActive)
                {
                    SceneManager.SetActiveScene(createdScene);
                }

                item.status = "succeeded";
                item.changed = true;
                item.path = outputPath;
                item.scenePath = createdScene.path;
                item.target = outputPath;
                item.message = "Scene created.";
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
            catch (Exception ex)
            {
                if (createdScene.IsValid() && createdScene.isLoaded && string.IsNullOrEmpty(createdScene.path))
                {
                    EditorSceneManager.CloseScene(createdScene, true);
                }

                return ControlResponses.Error("Failed to create scene: " + ex.Message, "tool_exception", request.name);
            }
        }

        public static ControlToolCallResponse HandleSceneSave(ControlToolCallRequest request)
        {
            SceneSaveArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new SceneSaveArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryGetSceneByPath(args.scenePath, out Scene scene) || !scene.IsValid() || !scene.isLoaded)
            {
                return ControlResponses.Error("Scene is not loaded: " + args.scenePath, "not_found", request.name);
            }

            string outputPath = scene.path;
            if (!string.IsNullOrEmpty(args.outputPath))
            {
                if (!TryNormalizeSceneAssetPath(args.outputPath, out outputPath, out string pathError))
                {
                    return ControlResponses.Error(pathError, "invalid_argument", request.name);
                }
            }
            else if (string.IsNullOrEmpty(outputPath))
            {
                return ControlResponses.Error("outputPath is required for untitled scenes.", "invalid_argument", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = BuildSceneMutationItem(outputPath, "scene_save");
            item.scenePath = scene.path;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Scene save planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            if (!string.Equals(scene.path, outputPath, StringComparison.OrdinalIgnoreCase) &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(outputPath) != null)
            {
                return ControlResponses.Error("Refusing to overwrite existing scene without explicit create flow: " + outputPath, "invalid_argument", request.name);
            }

            string folderPath = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folderPath) && !ControlWriteSupport.EnsureFolderPathExists(folderPath, out string folderError))
            {
                return ControlResponses.Error(folderError, "invalid_argument", request.name);
            }

            bool changed = scene.isDirty || !string.Equals(scene.path, outputPath, StringComparison.OrdinalIgnoreCase);
            if (!EditorSceneManager.SaveScene(scene, outputPath))
            {
                item.status = "failed";
                item.changed = false;
                item.message = "Failed to save scene.";
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            item.status = "succeeded";
            item.changed = changed;
            item.scenePath = outputPath;
            item.path = outputPath;
            item.target = outputPath;
            item.message = "Scene saved.";
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static ControlToolCallResponse HandleSceneSaveAll(ControlToolCallRequest request)
        {
            SceneSaveAllArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new SceneSaveAllArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            var items = new List<MutationItem>();
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                MutationItem item = BuildSceneMutationItem(
                    string.IsNullOrEmpty(scene.path) ? scene.name : scene.path,
                    "scene_save_all");
                item.scenePath = scene.path;
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = scene.isDirty
                        ? "Scene save planned."
                        : "Scene already clean.";
                    continue;
                }

                if (!scene.isDirty)
                {
                    item.status = "succeeded";
                    item.changed = false;
                    item.message = "Scene already clean.";
                    continue;
                }

                if (string.IsNullOrEmpty(scene.path))
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = "Cannot save untitled scene in save_all.";
                    continue;
                }

                if (!EditorSceneManager.SaveScene(scene))
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = "Failed to save scene.";
                    continue;
                }

                item.status = "succeeded";
                item.changed = true;
                item.path = scene.path;
                item.target = scene.path;
                item.message = "Scene saved.";
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, sceneCount, items);
        }

        public static ControlToolCallResponse HandleComponentAdd(ControlToolCallRequest request)
        {
            ComponentAddArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ComponentAddArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null || string.IsNullOrEmpty(args.componentType))
            {
                return ControlResponses.Error("target and componentType are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            Type componentType = ControlWriteSupport.FindTypeByName(args.componentType);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return ControlResponses.Error("Unknown component type: " + args.componentType, "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryValidateComponentTypeAllowed(componentType, out string allowError))
            {
                return ControlResponses.Error(allowError, "permission_denied", request.name);
            }

            Dictionary<string, object> fields = ControlWriteSupport.ExtractObject(request.argumentsJson, "fields");

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "component_add");
            item.componentType = componentType.FullName ?? componentType.Name;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Add component planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            Component component = Undo.AddComponent(target, componentType);
            if (component == null)
            {
                item.status = "failed";
                item.changed = false;
                item.message = "Failed to add component.";
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            if (fields != null && fields.Count > 0)
            {
                if (!ControlWriteSupport.TryApplyComponentFields(component, fields, false, out int appliedFields, out string fieldError))
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = "Component created but fields apply failed: " + fieldError;
                    item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
                    return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
                }

                item.message = "Component added. Applied fields=" + appliedFields;
            }
            else
            {
                item.message = "Component added.";
            }

            item.status = "succeeded";
            item.changed = true;
            item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            item.componentType = component.GetType().FullName ?? component.GetType().Name;
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static ControlToolCallResponse HandleComponentRemove(ControlToolCallRequest request)
        {
            ComponentRemoveArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ComponentRemoveArgs
                {
                    dryRun = true,
                    apply = false,
                });

            bool hasComponentId = !string.IsNullOrEmpty(args.componentId);
            bool hasComponentType = !string.IsNullOrEmpty(args.componentType);
            if (args.target == null || hasComponentId == hasComponentType)
            {
                return ControlResponses.Error("target and exactly one of componentId/componentType are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            if (!ControlWriteSupport.TryResolveComponent(target, args.componentType, args.componentId, out Component component))
            {
                return ControlResponses.Error("Component not found on target GameObject.", "not_found", request.name);
            }


            if (!ControlWriteSupport.TryValidateComponentTypeAllowed(component.GetType(), out string allowError))
            {
                return ControlResponses.Error(allowError, "permission_denied", request.name);
            }
            if (component is Transform)
            {
                return ControlResponses.Error("Transform component cannot be removed.", "invalid_argument", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "component_remove");
            item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            item.componentType = component.GetType().FullName ?? component.GetType().Name;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Remove component planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            Undo.DestroyObjectImmediate(component);
            item.status = "succeeded";
            item.changed = true;
            item.message = "Component removed.";
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static ControlToolCallResponse HandleComponentSetFields(ControlToolCallRequest request)
        {
            ComponentSetFieldsArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ComponentSetFieldsArgs
                {
                    strict = true,
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null)
            {
                return ControlResponses.Error("target is required.", "invalid_argument", request.name);
            }

            Dictionary<string, object> fields = ControlWriteSupport.ExtractObject(request.argumentsJson, "fields");
            if (fields == null || fields.Count == 0)
            {
                return ControlResponses.Error("fields is required and must contain at least one entry.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            if (!ControlWriteSupport.TryResolveComponent(target, args.componentType, args.componentId, out Component component))
            {
                return ControlResponses.Error("Component not found on target GameObject.", "not_found", request.name);
            }


            if (!ControlWriteSupport.TryValidateComponentTypeAllowed(component.GetType(), out string allowError))
            {
                return ControlResponses.Error(allowError, "permission_denied", request.name);
            }
            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "component_set_fields");
            item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            item.componentType = component.GetType().FullName ?? component.GetType().Name;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Set fields planned. fieldCount=" + fields.Count;
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            if (!ControlWriteSupport.TryApplyComponentFields(component, fields, args.strict, out int appliedCount, out string applyError))
            {
                item.status = "failed";
                item.changed = false;
                item.message = applyError;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            item.status = "succeeded";
            item.changed = appliedCount > 0;
            item.message = "Fields updated. applied=" + appliedCount;
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        private static bool HasTransformPatch(TransformInput input)
        {
            return input != null &&
                   (input.position != null || input.rotationEuler != null || input.scale != null);
        }

        private static MutationItem BuildSceneMutationItem(string targetPath, string action)
        {
            return new MutationItem
            {
                target = targetPath,
                action = action,
                status = "planned",
                changed = false,
                path = targetPath,
                scenePath = targetPath,
            };
        }

        private static bool HasDirtyLoadedScenes()
        {
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryNormalizeSceneAssetPath(string path, out string normalizedPath, out string error)
        {
            normalizedPath = null;
            error = null;

            if (!ControlWriteSupport.TryNormalizeAssetPath(path, out normalizedPath, out error))
            {
                return false;
            }

            if (!normalizedPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                error = "Scene path must end with .unity";
                return false;
            }

            if (!ControlWriteSupport.TryValidateAssetPathAllowed(normalizedPath, out error))
            {
                return false;
            }

            return true;
        }

        private static NewSceneSetup ParseNewSceneSetup(string setup)
        {
            if (string.Equals(setup, "DefaultGameObjects", StringComparison.OrdinalIgnoreCase))
            {
                return NewSceneSetup.DefaultGameObjects;
            }

            return NewSceneSetup.EmptyScene;
        }

        private static NewSceneMode ParseNewSceneMode(string openMode)
        {
            if (string.Equals(openMode, "Single", StringComparison.OrdinalIgnoreCase))
            {
                return NewSceneMode.Single;
            }

            return NewSceneMode.Additive;
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

        private static string BuildChildHierarchyPath(Transform parent, string childName)
        {
            string parentPath = BuildHierarchyPath(parent);
            if (string.IsNullOrEmpty(parentPath))
            {
                return childName;
            }

            return parentPath + "/" + childName;
        }
    }
}
