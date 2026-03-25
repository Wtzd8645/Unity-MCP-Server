using System;
using System.Collections.Generic;
using UnityEditor;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class ProjectWriteToolHandlers
    {
        public static ControlToolCallResponse HandleProjectSetBuildScenes(ControlToolCallRequest request)
        {
            ProjectSetBuildScenesArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ProjectSetBuildScenesArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.scenes == null)
            {
                return ControlResponses.Error("scenes is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!TryBuildRequestedBuildScenes(args.scenes, out EditorBuildSettingsScene[] requestedScenes, out string validationError))
            {
                return ControlResponses.Error(validationError, "invalid_argument", request.name);
            }

            EditorBuildSettingsScene[] currentScenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            bool changed = !AreBuildScenesEqual(currentScenes, requestedScenes);

            if (shouldApply && changed)
            {
                EditorBuildSettings.scenes = requestedScenes;
            }

            var payload = new ProjectSetBuildScenesResult
            {
                tool = request.name,
                dryRun = !shouldApply,
                applied = shouldApply,
                changed = changed,
                requested = requestedScenes.Length,
                sceneCount = requestedScenes.Length,
                enabledSceneCount = CountEnabledScenes(requestedScenes),
                items = BuildProjectSetBuildSceneItems(currentScenes, requestedScenes, shouldApply, changed),
            };

            return ControlResponses.Success("unity_project_set_build_scenes completed.", payload);
        }

        private static int CountEnabledScenes(EditorBuildSettingsScene[] scenes)
        {
            int enabledCount = 0;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled)
                {
                    enabledCount++;
                }
            }

            return enabledCount;
        }

        private static bool TryBuildRequestedBuildScenes(
            ProjectBuildSceneInput[] inputs,
            out EditorBuildSettingsScene[] scenes,
            out string error)
        {
            scenes = Array.Empty<EditorBuildSettingsScene>();
            error = null;

            var requestedScenes = new List<EditorBuildSettingsScene>(inputs.Length);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < inputs.Length; i++)
            {
                ProjectBuildSceneInput input = inputs[i];
                if (input == null || string.IsNullOrEmpty(input.path))
                {
                    error = "Each scenes entry must include a path.";
                    return false;
                }

                if (!input.path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !input.path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Scene path must be an Assets/*.unity path: " + input.path;
                    return false;
                }

                if (!seenPaths.Add(input.path))
                {
                    error = "Duplicate scene path in scenes: " + input.path;
                    return false;
                }

                SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(input.path);
                if (sceneAsset == null)
                {
                    error = "Scene asset not found: " + input.path;
                    return false;
                }

                requestedScenes.Add(new EditorBuildSettingsScene(input.path, input.enabled));
            }

            scenes = requestedScenes.ToArray();
            return true;
        }

        private static bool AreBuildScenesEqual(EditorBuildSettingsScene[] currentScenes, EditorBuildSettingsScene[] requestedScenes)
        {
            if (currentScenes.Length != requestedScenes.Length)
            {
                return false;
            }

            for (int i = 0; i < currentScenes.Length; i++)
            {
                if (!string.Equals(currentScenes[i].path, requestedScenes[i].path, StringComparison.OrdinalIgnoreCase) ||
                    currentScenes[i].enabled != requestedScenes[i].enabled)
                {
                    return false;
                }
            }

            return true;
        }

        private static ProjectSetBuildScenesItem[] BuildProjectSetBuildSceneItems(
            EditorBuildSettingsScene[] currentScenes,
            EditorBuildSettingsScene[] requestedScenes,
            bool shouldApply,
            bool changed)
        {
            var items = new ProjectSetBuildScenesItem[requestedScenes.Length];
            for (int i = 0; i < requestedScenes.Length; i++)
            {
                EditorBuildSettingsScene requested = requestedScenes[i];
                bool unchangedAtIndex =
                    i < currentScenes.Length &&
                    string.Equals(currentScenes[i].path, requested.path, StringComparison.OrdinalIgnoreCase) &&
                    currentScenes[i].enabled == requested.enabled;

                items[i] = new ProjectSetBuildScenesItem
                {
                    path = requested.path,
                    guid = AssetDatabase.AssetPathToGUID(requested.path),
                    enabled = requested.enabled,
                    buildIndex = i,
                    status = shouldApply ? "succeeded" : "planned",
                    changed = changed && !unchangedAtIndex,
                    message = shouldApply
                        ? (changed
                            ? (unchangedAtIndex ? "Build settings entry unchanged." : "Build settings entry applied.")
                            : "Build settings already match requested entry.")
                        : (unchangedAtIndex ? "Build settings already match requested entry." : "Build settings update planned."),
                };
            }

            return items;
        }
    }
}
