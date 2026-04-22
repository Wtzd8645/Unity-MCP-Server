using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class EditorContextDirtyPolicySupport
    {
        internal enum EditorContextDirtyPolicy
        {
            ErrorIfDirty = 0,
            SaveSaved = 1,
            DiscardDirty = 2,
        }

        internal readonly struct SingleSceneReplacementPlan
        {
            public readonly bool useSingleMode;
            public readonly Scene scratchScene;

            public SingleSceneReplacementPlan(bool useSingleMode, Scene scratchScene)
            {
                this.useSingleMode = useSingleMode;
                this.scratchScene = scratchScene;
            }
        }

        public static bool TryParsePolicy(
            string rawPolicy,
            string toolName,
            out EditorContextDirtyPolicy policy,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            if (string.IsNullOrWhiteSpace(rawPolicy) ||
                string.Equals(rawPolicy, "ErrorIfDirty", StringComparison.OrdinalIgnoreCase))
            {
                policy = EditorContextDirtyPolicy.ErrorIfDirty;
                return true;
            }

            if (string.Equals(rawPolicy, "SaveSaved", StringComparison.OrdinalIgnoreCase))
            {
                policy = EditorContextDirtyPolicy.SaveSaved;
                return true;
            }

            if (string.Equals(rawPolicy, "DiscardDirty", StringComparison.OrdinalIgnoreCase))
            {
                policy = EditorContextDirtyPolicy.DiscardDirty;
                return true;
            }

            policy = EditorContextDirtyPolicy.ErrorIfDirty;
            errorResponse = ControlResponses.Error(
                "dirtyEditorContextPolicy must be one of: ErrorIfDirty, SaveSaved, DiscardDirty.",
                "invalid_argument",
                toolName);
            return false;
        }

        public static bool TryPrepareForSceneOperation(
            string toolName,
            EditorContextDirtyPolicy policy,
            bool replaceLoadedScenes,
            out SingleSceneReplacementPlan plan,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            plan = new SingleSceneReplacementPlan(true, default(Scene));
            Scene[] loadedScenes = GetLoadedScenes();
            bool hasLoadedScenes = replaceLoadedScenes && loadedScenes.Length > 0;

            if (policy == EditorContextDirtyPolicy.ErrorIfDirty)
            {
                if (!TryValidatePrefabStageForErrorIfDirty(toolName, out errorResponse))
                {
                    return false;
                }

                if (hasLoadedScenes && TryGetFirstDirtyScene(loadedScenes, out Scene errorIfDirtyScene))
                {
                    errorResponse = BuildDirtySceneBlockedError(
                        toolName,
                        errorIfDirtyScene,
                        "Scene switch would replace a dirty loaded scene. Set dirtyEditorContextPolicy to SaveSaved or DiscardDirty.");
                    return false;
                }

                return TryExitCleanPrefabStageIfNeeded(toolName, out errorResponse);
            }

            if (policy == EditorContextDirtyPolicy.SaveSaved)
            {
                if (hasLoadedScenes && TryGetFirstUnsavableDirtyScene(loadedScenes, out Scene unsavableScene))
                {
                    errorResponse = ControlResponses.Error(
                        "Cannot auto-save an untitled dirty scene. Save it explicitly or use dirtyEditorContextPolicy=DiscardDirty. Dirty scene: " + GetSceneDisplayName(unsavableScene),
                        "invalid_argument",
                        toolName);
                    return false;
                }

                if (!TryPrepareForPrefabStageExit(toolName, policy, out errorResponse))
                {
                    return false;
                }

                if (!hasLoadedScenes)
                {
                    return true;
                }

                return TrySaveDirtyScenes(loadedScenes, toolName, out errorResponse);
            }

            if (!TryPrepareForPrefabStageExit(toolName, policy, out errorResponse))
            {
                return false;
            }

            if (!hasLoadedScenes)
            {
                return true;
            }

            if (!TryCreateScratchScene(toolName, out Scene scratchScene, out errorResponse))
            {
                return false;
            }

            if (!TryCloseScenes(loadedScenes, toolName, out errorResponse))
            {
                TryCloseScratchScene(scratchScene);
                return false;
            }

            plan = new SingleSceneReplacementPlan(false, scratchScene);
            return true;
        }

        public static bool TryApplySceneClosePolicy(
            Scene scene,
            string toolName,
            EditorContextDirtyPolicy policy,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            if (!scene.isDirty)
            {
                return true;
            }

            switch (policy)
            {
                case EditorContextDirtyPolicy.ErrorIfDirty:
                    errorResponse = BuildDirtySceneBlockedError(
                        toolName,
                        scene,
                        "Scene is dirty. Save it first or set dirtyEditorContextPolicy to SaveSaved or DiscardDirty.");
                    return false;

                case EditorContextDirtyPolicy.SaveSaved:
                    return TrySaveDirtyScene(scene, toolName, out errorResponse);

                case EditorContextDirtyPolicy.DiscardDirty:
                    return true;

                default:
                    return true;
            }
        }

        public static void TryCloseScratchScene(Scene scratchScene)
        {
            if (scratchScene.IsValid() && scratchScene.isLoaded)
            {
                EditorSceneManager.CloseScene(scratchScene, true);
            }
        }

        private static Scene[] GetLoadedScenes()
        {
            int sceneCount = SceneManager.sceneCount;
            var scenes = new List<Scene>(sceneCount);
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded)
                {
                    scenes.Add(scene);
                }
            }

            return scenes.ToArray();
        }

        private static bool TryGetFirstDirtyScene(Scene[] scenes, out Scene dirtyScene)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].isDirty)
                {
                    dirtyScene = scenes[i];
                    return true;
                }
            }

            dirtyScene = default(Scene);
            return false;
        }

        private static bool TryGetFirstUnsavableDirtyScene(Scene[] scenes, out Scene unsavableScene)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                Scene scene = scenes[i];
                if (scene.isDirty && string.IsNullOrEmpty(scene.path))
                {
                    unsavableScene = scene;
                    return true;
                }
            }

            unsavableScene = default(Scene);
            return false;
        }

        private static bool TrySaveDirtyScenes(
            Scene[] scenes,
            string toolName,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (!TrySaveDirtyScene(scenes[i], toolName, out errorResponse))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TrySaveDirtyScene(
            Scene scene,
            string toolName,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            if (!scene.isDirty)
            {
                return true;
            }

            if (string.IsNullOrEmpty(scene.path))
            {
                errorResponse = ControlResponses.Error(
                    "Cannot auto-save an untitled dirty scene. Save it explicitly or use dirtyEditorContextPolicy=DiscardDirty.",
                    "invalid_argument",
                    toolName);
                return false;
            }

            if (!EditorSceneManager.SaveScene(scene))
            {
                errorResponse = ControlResponses.Error(
                    "Failed to save dirty scene before continuing: " + scene.path,
                    "tool_exception",
                    toolName);
                return false;
            }

            return true;
        }

        private static bool TryPrepareForPrefabStageExit(
            string toolName,
            EditorContextDirtyPolicy policy,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            if (!TryGetCurrentPrefabStageInfo(
                    out bool hasPrefabStage,
                    out string prefabAssetPath,
                    out bool isDirty,
                    out string inspectError))
            {
                errorResponse = ControlResponses.Error(inspectError, "tool_exception", toolName);
                return false;
            }

            if (!hasPrefabStage)
            {
                return true;
            }

            switch (policy)
            {
                case EditorContextDirtyPolicy.ErrorIfDirty:
                    if (isDirty)
                    {
                        errorResponse = ControlResponses.Error(
                            "Current Prefab Stage is dirty. Save it first or set dirtyEditorContextPolicy to SaveSaved or DiscardDirty. Dirty prefab: " + GetPrefabStageDisplayName(prefabAssetPath),
                            "conflict",
                            toolName);
                        return false;
                    }

                    return TryExitCurrentPrefabStage(toolName, out errorResponse);

                case EditorContextDirtyPolicy.SaveSaved:
                    if (isDirty && !TrySaveCurrentPrefabStage(toolName, out errorResponse))
                    {
                        return false;
                    }

                    return TryExitCurrentPrefabStage(toolName, out errorResponse);

                case EditorContextDirtyPolicy.DiscardDirty:
                    if (isDirty)
                    {
                        errorResponse = ControlResponses.Error(
                            "DiscardDirty is not supported for a dirty Prefab Stage because Unity does not expose a guaranteed non-interactive discard path. Dirty prefab: " + GetPrefabStageDisplayName(prefabAssetPath),
                            "conflict",
                            toolName);
                        return false;
                    }

                    return TryExitCurrentPrefabStage(toolName, out errorResponse);

                default:
                    return true;
            }
        }

        private static bool TryValidatePrefabStageForErrorIfDirty(
            string toolName,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            if (!TryGetCurrentPrefabStageInfo(
                    out bool hasPrefabStage,
                    out string prefabAssetPath,
                    out bool isDirty,
                    out string inspectError))
            {
                errorResponse = ControlResponses.Error(inspectError, "tool_exception", toolName);
                return false;
            }

            if (hasPrefabStage && isDirty)
            {
                errorResponse = ControlResponses.Error(
                    "Current Prefab Stage is dirty. Save it first or set dirtyEditorContextPolicy to SaveSaved or DiscardDirty. Dirty prefab: " + GetPrefabStageDisplayName(prefabAssetPath),
                    "conflict",
                    toolName);
                return false;
            }

            return true;
        }

        private static bool TryExitCleanPrefabStageIfNeeded(
            string toolName,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            if (!TryGetCurrentPrefabStageInfo(
                    out bool hasPrefabStage,
                    out _,
                    out bool isDirty,
                    out string inspectError))
            {
                errorResponse = ControlResponses.Error(inspectError, "tool_exception", toolName);
                return false;
            }

            if (!hasPrefabStage || isDirty)
            {
                return true;
            }

            return TryExitCurrentPrefabStage(toolName, out errorResponse);
        }

        private static bool TryGetCurrentPrefabStageInfo(
            out bool hasPrefabStage,
            out string prefabAssetPath,
            out bool isDirty,
            out string error)
        {
            hasPrefabStage = false;
            prefabAssetPath = null;
            isDirty = false;
            error = null;

            object prefabStage = GetCurrentPrefabStage();
            if (prefabStage == null)
            {
                return true;
            }

            hasPrefabStage = true;
            prefabAssetPath = GetStringProperty(prefabStage, "assetPath");
            bool? hasUnsavedChanges = GetBoolProperty(prefabStage, "hasUnsavedChanges");
            if (hasUnsavedChanges.HasValue)
            {
                isDirty = hasUnsavedChanges.Value;
                return true;
            }

            if (TryGetSceneFromStage(prefabStage, out Scene prefabStageScene))
            {
                isDirty = prefabStageScene.IsValid() && prefabStageScene.isDirty;
                return true;
            }

            error = "Failed to inspect the current Prefab Stage dirty state.";
            return false;
        }

        private static bool TrySaveCurrentPrefabStage(string toolName, out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            object prefabStage = GetCurrentPrefabStage();
            if (prefabStage == null)
            {
                return true;
            }

            if (!TryInvokeSavePrefabStage(prefabStage, out string saveError))
            {
                errorResponse = ControlResponses.Error(
                    "Failed to save dirty Prefab Stage before continuing: " + saveError,
                    "tool_exception",
                    toolName);
                return false;
            }

            if (!TryGetCurrentPrefabStageInfo(out bool hasPrefabStage, out string prefabAssetPath, out bool isDirty, out string inspectError))
            {
                errorResponse = ControlResponses.Error(inspectError, "tool_exception", toolName);
                return false;
            }

            if (hasPrefabStage && isDirty)
            {
                errorResponse = ControlResponses.Error(
                    "Prefab Stage is still dirty after save: " + GetPrefabStageDisplayName(prefabAssetPath),
                    "tool_exception",
                    toolName);
                return false;
            }

            return true;
        }

        private static bool TryExitCurrentPrefabStage(string toolName, out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            if (GetCurrentPrefabStage() == null)
            {
                return true;
            }

            Type stageUtilityType = GetStageUtilityType();
            MethodInfo goToMainStage = stageUtilityType?.GetMethod(
                "GoToMainStage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            if (goToMainStage == null)
            {
                errorResponse = ControlResponses.Error(
                    "Current Unity version does not expose a non-interactive Prefab Stage exit API.",
                    "tool_exception",
                    toolName);
                return false;
            }

            try
            {
                goToMainStage.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                errorResponse = ControlResponses.Error(
                    "Failed to exit Prefab Stage before continuing: " + ex.InnerException?.Message,
                    "tool_exception",
                    toolName);
                return false;
            }
            catch (Exception ex)
            {
                errorResponse = ControlResponses.Error(
                    "Failed to exit Prefab Stage before continuing: " + ex.Message,
                    "tool_exception",
                    toolName);
                return false;
            }

            if (GetCurrentPrefabStage() != null)
            {
                errorResponse = ControlResponses.Error(
                    "Failed to exit Prefab Stage before continuing.",
                    "tool_exception",
                    toolName);
                return false;
            }

            return true;
        }

        private static bool TryInvokeSavePrefabStage(object prefabStage, out string error)
        {
            error = null;
            if (prefabStage == null)
            {
                return true;
            }

            MethodInfo instanceSave = prefabStage.GetType().GetMethod(
                "SavePrefab",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (instanceSave != null)
            {
                return TryInvokeBooleanOptionalMethod(prefabStage, instanceSave, out error);
            }

            Type prefabStageUtilityType = GetPrefabStageUtilityType();
            MethodInfo staticSave = prefabStageUtilityType?.GetMethod(
                "SaveCurrentPrefabStage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            if (staticSave != null)
            {
                return TryInvokeBooleanOptionalMethod(null, staticSave, out error);
            }

            error = "Current Unity version does not expose a non-interactive Prefab Stage save API.";
            return false;
        }

        private static bool TryInvokeBooleanOptionalMethod(object target, MethodInfo method, out string error)
        {
            error = null;
            try
            {
                object result = method.Invoke(target, null);
                if (method.ReturnType == typeof(bool) && result is bool boolResult && !boolResult)
                {
                    error = "Operation returned false.";
                    return false;
                }

                return true;
            }
            catch (TargetInvocationException ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static object GetCurrentPrefabStage()
        {
            Type prefabStageUtilityType = GetPrefabStageUtilityType();
            MethodInfo getCurrentPrefabStage = prefabStageUtilityType?.GetMethod(
                "GetCurrentPrefabStage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            if (getCurrentPrefabStage == null)
            {
                return null;
            }

            try
            {
                return getCurrentPrefabStage.Invoke(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static Type GetPrefabStageUtilityType()
        {
            Assembly editorAssembly = typeof(Editor).Assembly;
            return editorAssembly.GetType("UnityEditor.SceneManagement.PrefabStageUtility")
                ?? editorAssembly.GetType("UnityEditor.Experimental.SceneManagement.PrefabStageUtility");
        }

        private static Type GetStageUtilityType()
        {
            Assembly editorAssembly = typeof(Editor).Assembly;
            return editorAssembly.GetType("UnityEditor.SceneManagement.StageUtility")
                ?? editorAssembly.GetType("UnityEditor.StageUtility");
        }

        private static bool TryGetSceneFromStage(object prefabStage, out Scene scene)
        {
            scene = default(Scene);
            if (prefabStage == null)
            {
                return false;
            }

            PropertyInfo sceneProperty = prefabStage.GetType().GetProperty("scene", BindingFlags.Public | BindingFlags.Instance);
            if (sceneProperty == null)
            {
                return false;
            }

            object sceneValue = sceneProperty.GetValue(prefabStage, null);
            if (sceneValue is Scene prefabStageScene)
            {
                scene = prefabStageScene;
                return true;
            }

            return false;
        }

        private static string GetStringProperty(object target, string propertyName)
        {
            PropertyInfo property = target?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(target, null) as string;
        }

        private static bool? GetBoolProperty(object target, string propertyName)
        {
            PropertyInfo property = target?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != typeof(bool))
            {
                return null;
            }

            object value = property.GetValue(target, null);
            return value is bool boolValue ? boolValue : (bool?)null;
        }

        private static string GetPrefabStageDisplayName(string prefabAssetPath)
        {
            return string.IsNullOrEmpty(prefabAssetPath) ? "<current-prefab-stage>" : prefabAssetPath;
        }

        private static bool TryCreateScratchScene(
            string toolName,
            out Scene scratchScene,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            scratchScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (!scratchScene.IsValid() || !scratchScene.isLoaded)
            {
                errorResponse = ControlResponses.Error(
                    "Failed to create scratch scene for non-interactive discard.",
                    "tool_exception",
                    toolName);
                return false;
            }

            return true;
        }

        private static bool TryCloseScenes(
            Scene[] scenes,
            string toolName,
            out ControlToolCallResponse errorResponse)
        {
            errorResponse = null;
            for (int i = 0; i < scenes.Length; i++)
            {
                Scene scene = scenes[i];
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                if (!EditorSceneManager.CloseScene(scene, true))
                {
                    errorResponse = ControlResponses.Error(
                        "Failed to discard and close scene without prompting: " + GetSceneDisplayName(scene),
                        "tool_exception",
                        toolName);
                    return false;
                }
            }

            return true;
        }

        private static ControlToolCallResponse BuildDirtySceneBlockedError(string toolName, Scene scene, string message)
        {
            string sceneName = GetSceneDisplayName(scene);
            return ControlResponses.Error(
                message + " Dirty scene: " + sceneName,
                "conflict",
                toolName);
        }

        private static string GetSceneDisplayName(Scene scene)
        {
            return string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
        }
    }
}
