using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class ProjectReadToolHandlers
    {
        public static ControlToolCallResponse HandlePing(string controlVersion)
        {
            var payload = new PingStructuredContent
            {
                connected = true,
                controlVersion = controlVersion,
                serverTimeUtc = DateTime.UtcNow.ToString("O"),
                editor = new PingEditorState
                {
                    isResponding = true,
                    isPlaying = EditorApplication.isPlaying,
                    isPaused = EditorApplication.isPaused,
                    isCompiling = EditorApplication.isCompiling,
                },
            };

            return ControlResponses.Success("unity_project_ping handled by Unity control.", payload);
        }

        public static ControlToolCallResponse HandleProjectInfo(ControlToolCallRequest request)
        {
            ProjectInfoArgs args = ControlJson.ParseArgs(request.argumentsJson, new ProjectInfoArgs());
            string projectPath = ControlUtil.GetProjectRootPath();

            var payload = new ProjectInfoStructuredContent
            {
                projectName = Path.GetFileName(projectPath),
                projectPath = projectPath,
                unityVersion = Application.unityVersion,
                companyName = PlayerSettings.companyName,
                productName = PlayerSettings.productName,
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget).ToString(),
                editorState = new ProjectInfoEditorState
                {
                    isPlaying = EditorApplication.isPlaying,
                    isPaused = EditorApplication.isPaused,
                    isCompiling = EditorApplication.isCompiling,
                    isUpdating = EditorApplication.isUpdating,
                },
            };

            if (args.includePlatformMatrix)
            {
                payload.supportedBuildTargets = Enum.GetNames(typeof(BuildTarget));
            }

            return ControlResponses.Success("unity_project_get_info completed.", payload);
        }

        public static ControlToolCallResponse HandleProjectGetBuildSettings(ControlToolCallRequest request)
        {
            ControlJson.ParseArgs(request.argumentsJson, new ProjectGetBuildSettingsArgs());
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();

            var payload = new ProjectBuildSettingsResult
            {
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget).ToString(),
                sceneCount = scenes.Length,
                enabledSceneCount = CountEnabledScenes(scenes),
            };

            return ControlResponses.Success("unity_project_get_build_settings completed.", payload);
        }

        public static ControlToolCallResponse HandleProjectListBuildScenes(ControlToolCallRequest request)
        {
            ControlJson.ParseArgs(request.argumentsJson, new ProjectListBuildScenesArgs());
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();

            var payload = new ProjectListBuildScenesResult
            {
                count = scenes.Length,
                enabledCount = CountEnabledScenes(scenes),
                items = BuildProjectBuildSceneItems(scenes),
            };

            return ControlResponses.Success("unity_project_list_build_scenes completed.", payload);
        }

        public static ControlToolCallResponse HandleProjectGetPlayerSettings(ControlToolCallRequest request)
        {
            ControlJson.ParseArgs(request.argumentsJson, new ProjectGetPlayerSettingsArgs());

            BuildTarget activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(activeBuildTarget);
            string applicationIdentifier = activeBuildTargetGroup == BuildTargetGroup.Unknown
                ? null
                : PlayerSettings.GetApplicationIdentifier(activeBuildTargetGroup);

            var payload = new ProjectPlayerSettingsResult
            {
                activeBuildTarget = activeBuildTarget.ToString(),
                activeBuildTargetGroup = activeBuildTargetGroup.ToString(),
                companyName = PlayerSettings.companyName,
                productName = PlayerSettings.productName,
                bundleVersion = PlayerSettings.bundleVersion,
                applicationIdentifier = applicationIdentifier,
                runInBackground = PlayerSettings.runInBackground,
            };

            return ControlResponses.Success("unity_project_get_player_settings completed.", payload);
        }

        public static ControlToolCallResponse HandleProjectGetProjectSettings(ControlToolCallRequest request)
        {
            ControlJson.ParseArgs(request.argumentsJson, new ProjectGetProjectSettingsArgs());

            var payload = new ProjectSettingsResult
            {
                serializationMode = EditorSettings.serializationMode.ToString(),
                externalVersionControl = EditorSettings.externalVersionControl,
                enterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
                enterPlayModeOptions = EditorSettings.enterPlayModeOptions.ToString(),
            };

            return ControlResponses.Success("unity_project_get_project_settings completed.", payload);
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

        private static ProjectBuildSceneItem[] BuildProjectBuildSceneItems(EditorBuildSettingsScene[] scenes)
        {
            var items = new ProjectBuildSceneItem[scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
            {
                EditorBuildSettingsScene scene = scenes[i];
                items[i] = new ProjectBuildSceneItem
                {
                    path = scene.path,
                    name = Path.GetFileNameWithoutExtension(scene.path),
                    guid = AssetDatabase.AssetPathToGUID(scene.path),
                    enabled = scene.enabled,
                    buildIndex = i,
                };
            }

            return items;
        }
    }
}
