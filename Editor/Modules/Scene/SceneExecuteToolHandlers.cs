using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class SceneExecuteToolHandlers
    {
        public static ControlToolCallResponse HandleOpenScene(ControlToolCallRequest request)
        {
            OpenSceneArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new OpenSceneArgs
                {
                    openMode = "Single",
                    saveModifiedScenes = false,
                    setActive = true,
                });

            if (string.IsNullOrEmpty(args.scenePath))
            {
                return ControlResponses.Error("scenePath is required.", "invalid_argument", request.name);
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(args.scenePath) == null)
            {
                return ControlResponses.Error("Scene not found: " + args.scenePath, "not_found", request.name);
            }

            if (args.saveModifiedScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return ControlResponses.Error("Open scene cancelled by user.", "cancelled", request.name);
            }

            OpenSceneMode mode = ParseOpenSceneMode(args.openMode);
            Scene openedScene = EditorSceneManager.OpenScene(args.scenePath, mode);

            if (args.setActive && openedScene.IsValid())
            {
                SceneManager.SetActiveScene(openedScene);
            }

            var payload = new OpenSceneResult
            {
                openedScenePath = openedScene.path,
                activeScenePath = SceneManager.GetActiveScene().path,
                loadedScenes = GetLoadedScenePaths(),
            };

            return ControlResponses.Success("unity_scene_open completed.", payload);
        }

        public static ControlToolCallResponse HandleSceneSetActive(ControlToolCallRequest request)
        {
            SceneSetActiveArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new SceneSetActiveArgs());

            if (string.IsNullOrEmpty(args.scenePath))
            {
                return ControlResponses.Error("scenePath is required.", "invalid_argument", request.name);
            }

            Scene scene = SceneManager.GetSceneByPath(args.scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return ControlResponses.Error("Scene is not loaded: " + args.scenePath, "not_found", request.name);
            }

            if (!SceneManager.SetActiveScene(scene))
            {
                return ControlResponses.Error("Failed to set active scene: " + args.scenePath, "tool_exception", request.name);
            }

            var payload = new SceneSetActiveResult
            {
                activeScenePath = SceneManager.GetActiveScene().path,
                loadedScenes = GetLoadedScenePaths(),
            };

            return ControlResponses.Success("unity_scene_set_active completed.", payload);
        }

        public static ControlToolCallResponse HandleSceneClose(ControlToolCallRequest request)
        {
            SceneCloseArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new SceneCloseArgs
                {
                    removeScene = true,
                    saveModifiedScene = false,
                });

            if (string.IsNullOrEmpty(args.scenePath))
            {
                return ControlResponses.Error("scenePath is required.", "invalid_argument", request.name);
            }

            Scene scene = SceneManager.GetSceneByPath(args.scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return ControlResponses.Error("Scene is not loaded: " + args.scenePath, "not_found", request.name);
            }

            if (args.saveModifiedScene && scene.isDirty)
            {
                if (string.IsNullOrEmpty(scene.path))
                {
                    return ControlResponses.Error("Cannot save a loaded untitled scene before close.", "invalid_argument", request.name);
                }

                if (!EditorSceneManager.SaveScene(scene))
                {
                    return ControlResponses.Error("Failed to save scene before close: " + scene.path, "tool_exception", request.name);
                }
            }

            if (!EditorSceneManager.CloseScene(scene, args.removeScene))
            {
                return ControlResponses.Error("Failed to close scene: " + args.scenePath, "tool_exception", request.name);
            }

            Scene activeScene = SceneManager.GetActiveScene();
            var payload = new SceneCloseResult
            {
                closedScenePath = args.scenePath,
                activeScenePath = activeScene.IsValid() ? activeScene.path : null,
                loadedScenes = GetLoadedScenePaths(),
            };

            return ControlResponses.Success("unity_scene_close completed.", payload);
        }

        private static string[] GetLoadedScenePaths()
        {
            var scenes = new List<string>();
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                scenes.Add(scene.path);
            }

            return scenes.ToArray();
        }

        private static OpenSceneMode ParseOpenSceneMode(string mode)
        {
            if (string.Equals(mode, "Additive", StringComparison.OrdinalIgnoreCase))
            {
                return OpenSceneMode.Additive;
            }

            if (string.Equals(mode, "AdditiveWithoutLoading", StringComparison.OrdinalIgnoreCase))
            {
                return OpenSceneMode.AdditiveWithoutLoading;
            }

            return OpenSceneMode.Single;
        }
    }
}
