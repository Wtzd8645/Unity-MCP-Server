using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Blanketmen.UnityMcp.Control.Editor
{
    internal static class CoreToolHandlers
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

        public static ControlToolCallResponse HandleProjectSwitchBuildTarget(
            ControlToolCallRequest request,
            MainThreadActionInvoker mainThreadInvoker)
        {
            ProjectSwitchBuildTargetArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ProjectSwitchBuildTargetArgs
                {
                    timeoutMs = 600000,
                });

            if (mainThreadInvoker == null)
            {
                return ControlResponses.Error("Main-thread invoker is unavailable.", "internal_error", request.name);
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return ControlResponses.Error("Build target switching is unavailable while Play Mode is active.", "invalid_state", request.name);
            }

            if (string.IsNullOrEmpty(args.buildTarget))
            {
                return ControlResponses.Error("buildTarget is required.", "invalid_argument", request.name);
            }

            int timeoutMs = Mathf.Clamp(args.timeoutMs, 5000, 3600000);
            if (!Enum.TryParse(args.buildTarget, true, out BuildTarget requestedTarget))
            {
                return ControlResponses.Error("Unknown buildTarget: " + args.buildTarget, "invalid_argument", request.name);
            }

            BuildTargetGroup requestedGroup = BuildPipeline.GetBuildTargetGroup(requestedTarget);
            if (requestedGroup == BuildTargetGroup.Unknown)
            {
                return ControlResponses.Error("Unsupported buildTarget: " + args.buildTarget, "invalid_argument", request.name);
            }

            BuildTarget beforeTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup beforeGroup = BuildPipeline.GetBuildTargetGroup(beforeTarget);
            if (beforeTarget == requestedTarget)
            {
                var unchangedPayload = new ProjectSwitchBuildTargetResult
                {
                    changed = false,
                    buildTargetBefore = beforeTarget.ToString(),
                    buildTargetGroupBefore = beforeGroup.ToString(),
                    buildTargetAfter = beforeTarget.ToString(),
                    buildTargetGroupAfter = beforeGroup.ToString(),
                    elapsedMs = 0,
                };

                return ControlResponses.Success("unity_project_switch_build_target completed.", unchangedPayload);
            }

            bool switched = false;
            var stopwatch = Stopwatch.StartNew();
            if (!TryInvokeOnMainThread(
                    mainThreadInvoker,
                    () =>
                    {
                        switched = EditorUserBuildSettings.SwitchActiveBuildTarget(requestedGroup, requestedTarget);
                    },
                    timeoutMs,
                    out string switchError))
            {
                return ControlResponses.Error(
                    "Failed to switch build target: " + switchError,
                    InferExecutionErrorStatus(switchError),
                    request.name);
            }

            stopwatch.Stop();

            BuildTarget afterTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup afterGroup = BuildPipeline.GetBuildTargetGroup(afterTarget);
            if (!switched || afterTarget != requestedTarget)
            {
                return ControlResponses.Error(
                    "Failed to switch active build target to " + requestedTarget + ".",
                    "tool_exception",
                    request.name);
            }

            var payload = new ProjectSwitchBuildTargetResult
            {
                changed = beforeTarget != afterTarget,
                buildTargetBefore = beforeTarget.ToString(),
                buildTargetGroupBefore = beforeGroup.ToString(),
                buildTargetAfter = afterTarget.ToString(),
                buildTargetGroupAfter = afterGroup.ToString(),
                elapsedMs = (int)stopwatch.ElapsedMilliseconds,
            };

            return ControlResponses.Success("unity_project_switch_build_target completed.", payload);
        }

        public static ControlToolCallResponse HandleProjectBuildPlayer(
            ControlToolCallRequest request,
            MainThreadActionInvoker mainThreadInvoker)
        {
            ProjectBuildPlayerArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ProjectBuildPlayerArgs
                {
                    timeoutMs = 1800000,
                });

            if (mainThreadInvoker == null)
            {
                return ControlResponses.Error("Main-thread invoker is unavailable.", "internal_error", request.name);
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return ControlResponses.Error("Player builds are unavailable while Play Mode is active.", "invalid_state", request.name);
            }

            if (string.IsNullOrEmpty(args.outputPath))
            {
                return ControlResponses.Error("outputPath is required.", "invalid_argument", request.name);
            }

            int timeoutMs = Mathf.Clamp(args.timeoutMs, 10000, 7200000);
            if (!TryResolveBuildOutputPath(
                    args.outputPath,
                    request.name,
                    out string outputPath,
                    out string absoluteOutputPath,
                    out string absoluteParentFolder,
                    out ControlToolCallResponse pathError))
            {
                return pathError;
            }

            string[] enabledScenes = GetEnabledBuildScenes();
            if (enabledScenes.Length == 0)
            {
                return ControlResponses.Error("Build Settings does not contain any enabled scenes.", "invalid_state", request.name);
            }

            BuildTarget activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(activeBuildTarget);
            if (activeBuildTargetGroup == BuildTargetGroup.Unknown)
            {
                return ControlResponses.Error("Active build target is unsupported for BuildPipeline.BuildPlayer.", "invalid_state", request.name);
            }

            try
            {
                Directory.CreateDirectory(absoluteParentFolder);
            }
            catch (Exception ex)
            {
                return ControlResponses.Error("Failed to prepare build output folder: " + ex.Message, "tool_exception", request.name);
            }

            BuildReport report = null;
            if (!TryInvokeOnMainThread(
                    mainThreadInvoker,
                    () =>
                    {
                        var options = new BuildPlayerOptions
                        {
                            scenes = enabledScenes,
                            target = activeBuildTarget,
                            targetGroup = activeBuildTargetGroup,
                            locationPathName = absoluteOutputPath,
                        };

                        report = BuildPipeline.BuildPlayer(options);
                    },
                    timeoutMs,
                    out string buildError))
            {
                return ControlResponses.Error(
                    "Failed to build player: " + buildError,
                    InferExecutionErrorStatus(buildError),
                    request.name);
            }

            if (report == null)
            {
                return ControlResponses.Error("BuildPipeline.BuildPlayer returned no report.", "tool_exception", request.name);
            }

            BuildSummary summary = report.summary;
            var payload = new ProjectBuildPlayerResult
            {
                buildTarget = activeBuildTarget.ToString(),
                buildTargetGroup = activeBuildTargetGroup.ToString(),
                outputPath = outputPath,
                result = summary.result.ToString(),
                sceneCount = enabledScenes.Length,
                totalErrors = summary.totalErrors,
                totalWarnings = summary.totalWarnings,
                totalSizeBytes = summary.totalSize > (ulong)long.MaxValue ? long.MaxValue : (long)summary.totalSize,
                durationMs = (int)summary.totalTime.TotalMilliseconds,
            };

            return ControlResponses.Success("unity_project_build_player completed.", payload);
        }

        public static ControlToolCallResponse HandlePlaymodeStatus()
        {
            var payload = new PlaymodeStatusStructuredContent
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isChangingPlaymode = EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying,
            };

            return ControlResponses.Success("unity_runtime_get_playmode_status completed.", payload);
        }

        public static ControlToolCallResponse HandlePlaymodeStart(
            ControlToolCallRequest request,
            MainThreadActionInvoker mainThreadInvoker)
        {
            PlaymodeTransitionArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new PlaymodeTransitionArgs
                {
                    waitForEntered = true,
                    timeoutMs = 15000,
                });

            if (mainThreadInvoker == null)
            {
                return ControlResponses.Error("Main-thread invoker is unavailable.", "internal_error", request.name);
            }

            int timeoutMs = Mathf.Clamp(args.timeoutMs, 1000, 120000);
            if (!TryQueryPlaymodeState(mainThreadInvoker, out string stateBefore, out bool isPlayingBefore, out string queryError))
            {
                return ControlResponses.Error("Failed to query playmode state: " + queryError, "tool_exception", request.name);
            }

            var stopwatch = Stopwatch.StartNew();
            if (!TryInvokeOnMainThread(mainThreadInvoker, () =>
                {
                    EditorApplication.isPlaying = true;
                },
                5000,
                out string startError))
            {
                return ControlResponses.Error("Failed to request Play Mode: " + startError, "tool_exception", request.name);
            }

            bool entered = false;
            string stateAfter = isPlayingBefore ? "PlayMode" : "EnteringPlayMode";
            if (args.waitForEntered)
            {
                while (stopwatch.ElapsedMilliseconds < timeoutMs)
                {
                    if (!TryQueryPlaymodeState(mainThreadInvoker, out stateAfter, out bool isPlayingNow, out queryError))
                    {
                        return ControlResponses.Error("Failed to query playmode state: " + queryError, "tool_exception", request.name);
                    }

                    entered = isPlayingNow;
                    if (entered)
                    {
                        break;
                    }

                    Thread.Sleep(50);
                }

                if (!entered)
                {
                    stopwatch.Stop();
                    return ControlResponses.Error(
                        "Timed out waiting to enter Play Mode after " + timeoutMs + " ms.",
                        "tool_timeout",
                        request.name);
                }
            }
            else
            {
                if (!TryQueryPlaymodeState(mainThreadInvoker, out stateAfter, out bool isPlayingNow, out queryError))
                {
                    return ControlResponses.Error("Failed to query playmode state: " + queryError, "tool_exception", request.name);
                }

                entered = isPlayingNow;
            }

            stopwatch.Stop();

            var payload = new PlaymodeTransitionResult
            {
                entered = entered,
                stopped = false,
                stateBefore = stateBefore,
                stateAfter = stateAfter,
                elapsedMs = (int)stopwatch.ElapsedMilliseconds,
                waitRequested = args.waitForEntered,
                timeoutMs = timeoutMs,
            };

            return ControlResponses.Success("unity_runtime_start_playmode completed.", payload);
        }

        public static ControlToolCallResponse HandlePlaymodeStop(
            ControlToolCallRequest request,
            MainThreadActionInvoker mainThreadInvoker)
        {
            PlaymodeTransitionArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new PlaymodeTransitionArgs
                {
                    waitForExited = true,
                    timeoutMs = 15000,
                });

            if (mainThreadInvoker == null)
            {
                return ControlResponses.Error("Main-thread invoker is unavailable.", "internal_error", request.name);
            }

            int timeoutMs = Mathf.Clamp(args.timeoutMs, 1000, 120000);
            if (!TryQueryPlaymodeState(mainThreadInvoker, out string stateBefore, out bool isPlayingBefore, out string queryError))
            {
                return ControlResponses.Error("Failed to query playmode state: " + queryError, "tool_exception", request.name);
            }

            var stopwatch = Stopwatch.StartNew();
            if (!TryInvokeOnMainThread(mainThreadInvoker, () =>
                {
                    EditorApplication.isPlaying = false;
                },
                5000,
                out string stopError))
            {
                return ControlResponses.Error("Failed to request stop Play Mode: " + stopError, "tool_exception", request.name);
            }

            bool stopped = !isPlayingBefore;
            string stateAfter = stopped ? "EditMode" : "ExitingPlayMode";
            if (args.waitForExited)
            {
                while (stopwatch.ElapsedMilliseconds < timeoutMs)
                {
                    if (!TryQueryPlaymodeState(mainThreadInvoker, out stateAfter, out bool isPlayingNow, out queryError))
                    {
                        return ControlResponses.Error("Failed to query playmode state: " + queryError, "tool_exception", request.name);
                    }

                    stopped = !isPlayingNow && string.Equals(stateAfter, "EditMode", StringComparison.Ordinal);
                    if (stopped)
                    {
                        break;
                    }

                    Thread.Sleep(50);
                }

                if (!stopped)
                {
                    stopwatch.Stop();
                    return ControlResponses.Error(
                        "Timed out waiting to exit Play Mode after " + timeoutMs + " ms.",
                        "tool_timeout",
                        request.name);
                }
            }
            else
            {
                if (!TryQueryPlaymodeState(mainThreadInvoker, out stateAfter, out bool isPlayingNow, out queryError))
                {
                    return ControlResponses.Error("Failed to query playmode state: " + queryError, "tool_exception", request.name);
                }

                stopped = !isPlayingNow;
            }

            stopwatch.Stop();

            var payload = new PlaymodeTransitionResult
            {
                entered = false,
                stopped = stopped,
                stateBefore = stateBefore,
                stateAfter = stateAfter,
                elapsedMs = (int)stopwatch.ElapsedMilliseconds,
                waitRequested = args.waitForExited,
                timeoutMs = timeoutMs,
            };

            return ControlResponses.Success("unity_runtime_stop_playmode completed.", payload);
        }

        private static bool TryQueryPlaymodeState(
            MainThreadActionInvoker mainThreadInvoker,
            out string state,
            out bool isPlaying,
            out string error)
        {
            state = "EditMode";
            isPlaying = false;
            error = null;

            string localState = state;
            bool localIsPlaying = isPlaying;
            bool result = TryInvokeOnMainThread(mainThreadInvoker, () =>
                {
                    localState = GetPlaymodeState();
                    localIsPlaying = EditorApplication.isPlaying;
                },
                1000,
                out error);
            state = localState;
            isPlaying = localIsPlaying;
            return result;
        }

        private static bool TryInvokeOnMainThread(
            MainThreadActionInvoker mainThreadInvoker,
            Action action,
            int timeoutMs,
            out string error)
        {
            error = null;
            if (mainThreadInvoker == null)
            {
                error = "Main-thread invoker is unavailable.";
                return false;
            }

            if (action == null)
            {
                error = "Action is null.";
                return false;
            }

            return mainThreadInvoker(action, timeoutMs, out error);
        }

        private static string GetPlaymodeState()
        {
            if (EditorApplication.isPlaying)
            {
                return "PlayMode";
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "EnteringPlayMode";
            }

            return "EditMode";
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

        private static string InferExecutionErrorStatus(string error)
        {
            if (!string.IsNullOrEmpty(error) &&
                error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "tool_timeout";
            }

            return "tool_exception";
        }

        private static string[] GetEnabledBuildScenes()
        {
            var enabledScenes = new List<string>();
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            for (int i = 0; i < buildScenes.Length; i++)
            {
                EditorBuildSettingsScene scene = buildScenes[i];
                if (!scene.enabled || string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }

                enabledScenes.Add(scene.path);
            }

            return enabledScenes.ToArray();
        }

        private static bool TryResolveBuildOutputPath(
            string rawPath,
            string toolName,
            out string outputPath,
            out string absoluteOutputPath,
            out string absoluteParentFolder,
            out ControlToolCallResponse errorResponse)
        {
            outputPath = null;
            absoluteOutputPath = null;
            absoluteParentFolder = null;
            errorResponse = null;

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                errorResponse = ControlResponses.Error("outputPath is required.", "invalid_argument", toolName);
                return false;
            }

            if (Path.IsPathRooted(rawPath))
            {
                errorResponse = ControlResponses.Error("outputPath must be project-relative and must not be absolute.", "invalid_argument", toolName);
                return false;
            }

            string candidate = rawPath.Trim().Replace('\\', '/');
            while (candidate.StartsWith("./", StringComparison.Ordinal))
            {
                candidate = candidate.Substring(2);
            }

            candidate = candidate.Trim('/');
            if (candidate.Length == 0)
            {
                errorResponse = ControlResponses.Error("outputPath must resolve to a path under Builds/.", "invalid_argument", toolName);
                return false;
            }

            if (candidate.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                errorResponse = ControlResponses.Error("outputPath must not point under Assets/ or Packages/.", "invalid_argument", toolName);
                return false;
            }

            if (!candidate.StartsWith("Builds/", StringComparison.Ordinal))
            {
                errorResponse = ControlResponses.Error("outputPath must start with Builds/.", "invalid_argument", toolName);
                return false;
            }

            string projectRoot = Path.GetFullPath(ControlUtil.GetProjectRootPath());
            string absoluteBuildRoot = Path.GetFullPath(Path.Combine(projectRoot, "Builds"));
            absoluteOutputPath = Path.GetFullPath(Path.Combine(projectRoot, candidate.Replace('/', Path.DirectorySeparatorChar)));

            if (!IsPathWithinRoot(absoluteOutputPath, absoluteBuildRoot))
            {
                errorResponse = ControlResponses.Error("outputPath must stay within the project's Builds/ directory.", "invalid_argument", toolName);
                return false;
            }

            string relativeOutputPath = ToProjectRelativePath(absoluteOutputPath, projectRoot);
            if (string.Equals(relativeOutputPath, "Builds", StringComparison.Ordinal))
            {
                errorResponse = ControlResponses.Error("outputPath must not target the Builds root directory itself.", "invalid_argument", toolName);
                return false;
            }

            absoluteParentFolder = Path.GetDirectoryName(absoluteOutputPath);
            if (string.IsNullOrEmpty(absoluteParentFolder))
            {
                errorResponse = ControlResponses.Error("outputPath must include a destination under Builds/.", "invalid_argument", toolName);
                return false;
            }

            outputPath = relativeOutputPath;
            return true;
        }

        private static bool IsPathWithinRoot(string absolutePath, string absoluteRoot)
        {
            string normalizedPath = Path.GetFullPath(absolutePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(absoluteRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToProjectRelativePath(string absolutePath, string projectRoot)
        {
            string normalizedPath = Path.GetFullPath(absolutePath);
            string normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath.Replace('\\', '/');
            }

            return normalizedPath.Substring(normalizedRoot.Length).Replace('\\', '/');
        }
    }
}

