using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class SceneWriteToolHandlers
    {
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
    }
}
