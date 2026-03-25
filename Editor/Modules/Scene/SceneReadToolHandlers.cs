using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class SceneReadToolHandlers
    {
        public static ControlToolCallResponse HandleListScenes(ControlToolCallRequest request)
        {
            ListScenesArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ListScenesArgs
                {
                    source = "both",
                    includeDisabled = true,
                    limit = 200,
                    offset = 0,
                });

            var byPath = new Dictionary<string, SceneListItem>(StringComparer.OrdinalIgnoreCase);
            bool includeBuildSettings = args.source == "buildSettings" || args.source == "both" || string.IsNullOrEmpty(args.source);
            bool includeAssets = args.source == "assets" || args.source == "both" || string.IsNullOrEmpty(args.source);

            if (includeBuildSettings)
            {
                EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
                for (int i = 0; i < buildScenes.Length; i++)
                {
                    EditorBuildSettingsScene scene = buildScenes[i];
                    if (!args.includeDisabled && !scene.enabled)
                    {
                        continue;
                    }

                    byPath[scene.path] = new SceneListItem
                    {
                        path = scene.path,
                        name = System.IO.Path.GetFileNameWithoutExtension(scene.path),
                        guid = AssetDatabase.AssetPathToGUID(scene.path),
                        inBuildSettings = true,
                        enabledInBuildSettings = scene.enabled,
                        hasEnabledInBuildSettings = true,
                        buildIndex = i,
                        hasBuildIndex = true,
                    };
                }
            }

            if (includeAssets)
            {
                string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
                for (int i = 0; i < guids.Length; i++)
                {
                    string guid = guids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    SceneListItem item;
                    if (!byPath.TryGetValue(path, out item))
                    {
                        item = new SceneListItem
                        {
                            path = path,
                            name = System.IO.Path.GetFileNameWithoutExtension(path),
                            guid = guid,
                            inBuildSettings = false,
                            hasBuildIndex = false,
                            hasEnabledInBuildSettings = false,
                        };
                    }
                    else if (string.IsNullOrEmpty(item.guid))
                    {
                        item.guid = guid;
                    }

                    byPath[path] = item;
                }
            }

            List<SceneListItem> all = byPath.Values
                .OrderBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            PaginationRange range = ControlUtil.BuildPaginationRange(all.Count, args.offset, args.limit, 500);
            List<SceneListItem> page = all.Skip(range.offset).Take(range.limit).ToList();

            var payload = new ListScenesResult
            {
                total = all.Count,
                items = page.ToArray(),
            };

            return ControlResponses.Success("unity_scene_list completed.", payload);
        }

        public static ControlToolCallResponse HandleSceneListLoaded(ControlToolCallRequest request)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            var payload = new SceneListLoadedResult
            {
                activeScenePath = activeScene.IsValid() ? activeScene.path : null,
                items = GetLoadedSceneItems(),
            };

            return ControlResponses.Success("unity_scene_list_loaded completed.", payload);
        }

        public static ControlToolCallResponse HandleSceneGetActive(ControlToolCallRequest request)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return ControlResponses.Error("Active scene is not available.", "not_found", request.name);
            }

            var payload = new SceneGetActiveResult
            {
                activeScenePath = activeScene.path,
                scene = BuildSceneInfo(activeScene, true),
            };

            return ControlResponses.Success("unity_scene_get_active completed.", payload);
        }

        private static SceneInfoItem[] GetLoadedSceneItems()
        {
            var scenes = new List<SceneInfoItem>();
            Scene activeScene = SceneManager.GetActiveScene();
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                scenes.Add(BuildSceneInfo(scene, scene == activeScene));
            }

            return scenes.ToArray();
        }

        private static SceneInfoItem BuildSceneInfo(Scene scene, bool isActive)
        {
            int buildIndex = scene.buildIndex;
            return new SceneInfoItem
            {
                path = scene.path,
                name = scene.name,
                isLoaded = scene.isLoaded,
                isDirty = scene.isDirty,
                isActive = isActive,
                rootCount = scene.isLoaded ? scene.rootCount : 0,
                buildIndex = buildIndex,
                hasBuildIndex = buildIndex >= 0,
            };
        }
    }
}
