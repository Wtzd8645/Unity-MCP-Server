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
                    dirtyEditorContextPolicy = "ErrorIfDirty",
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

            if (!EditorContextDirtyPolicySupport.TryParsePolicy(
                    args.dirtyEditorContextPolicy,
                    request.name,
                    out EditorContextDirtyPolicySupport.EditorContextDirtyPolicy dirtyEditorContextPolicy,
                    out ControlToolCallResponse policyError))
            {
                return policyError;
            }

            OpenSceneMode mode = ParseOpenSceneMode(args.openMode);
            EditorContextDirtyPolicySupport.SingleSceneReplacementPlan replacementPlan = default(EditorContextDirtyPolicySupport.SingleSceneReplacementPlan);
            if (!EditorContextDirtyPolicySupport.TryPrepareForSceneOperation(
                    request.name,
                    dirtyEditorContextPolicy,
                    mode == OpenSceneMode.Single,
                    out replacementPlan,
                    out ControlToolCallResponse preflightError))
            {
                return preflightError;
            }

            Scene openedScene;
            OpenSceneMode effectiveMode = replacementPlan.useSingleMode ? mode : OpenSceneMode.Additive;
            openedScene = EditorSceneManager.OpenScene(args.scenePath, effectiveMode);

            if (args.setActive && openedScene.IsValid())
            {
                SceneManager.SetActiveScene(openedScene);
            }

            if (mode == OpenSceneMode.Single)
            {
                EditorContextDirtyPolicySupport.TryCloseScratchScene(replacementPlan.scratchScene);
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
                    dirtyEditorContextPolicy = "ErrorIfDirty",
                });

            if (string.IsNullOrEmpty(args.scenePath))
            {
                return ControlResponses.Error("scenePath is required.", "invalid_argument", request.name);
            }

            if (!EditorContextDirtyPolicySupport.TryParsePolicy(
                    args.dirtyEditorContextPolicy,
                    request.name,
                    out EditorContextDirtyPolicySupport.EditorContextDirtyPolicy dirtyEditorContextPolicy,
                    out ControlToolCallResponse policyError))
            {
                return policyError;
            }

            Scene scene = SceneManager.GetSceneByPath(args.scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return ControlResponses.Error("Scene is not loaded: " + args.scenePath, "not_found", request.name);
            }

            if (dirtyEditorContextPolicy == EditorContextDirtyPolicySupport.EditorContextDirtyPolicy.ErrorIfDirty &&
                scene.isDirty)
            {
                return ControlResponses.Error(
                    "Scene is dirty. Save it first or set dirtyEditorContextPolicy to SaveSaved or DiscardDirty. Dirty scene: " + (string.IsNullOrEmpty(scene.path) ? scene.name : scene.path),
                    "conflict",
                    request.name);
            }

            if (dirtyEditorContextPolicy == EditorContextDirtyPolicySupport.EditorContextDirtyPolicy.SaveSaved &&
                scene.isDirty &&
                string.IsNullOrEmpty(scene.path))
            {
                return ControlResponses.Error(
                    "Cannot auto-save an untitled dirty scene. Save it explicitly or use dirtyEditorContextPolicy=DiscardDirty. Dirty scene: " + scene.name,
                    "invalid_argument",
                    request.name);
            }

            if (!EditorContextDirtyPolicySupport.TryPrepareForSceneOperation(
                    request.name,
                    dirtyEditorContextPolicy,
                    false,
                    out _,
                    out ControlToolCallResponse contextError))
            {
                return contextError;
            }

            if (!EditorContextDirtyPolicySupport.TryApplySceneClosePolicy(
                    scene,
                    request.name,
                    dirtyEditorContextPolicy,
                    out ControlToolCallResponse closePolicyError))
            {
                return closePolicyError;
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
