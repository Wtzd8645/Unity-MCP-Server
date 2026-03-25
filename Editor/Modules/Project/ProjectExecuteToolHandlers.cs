using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class ProjectExecuteToolHandlers
    {
        public static ControlToolCallResponse HandleRunTests(
            ControlToolCallRequest request,
            MainThreadActionInvoker mainThreadInvoker)
        {
            RunTestsArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new RunTestsArgs
                {
                    mode = "EditMode",
                    timeoutMs = 600000,
                    includePassed = false,
                    includeXmlReportPath = true,
                });

            if (mainThreadInvoker == null)
            {
                return ControlResponses.Error("Main-thread invoker is unavailable.", "internal_error", request.name);
            }

            if (!RunTestsReflectionRunner.TryRun(
                    args,
                    mainThreadInvoker,
                    out RunTestsResult result,
                    out string errorStatus,
                    out string errorMessage))
            {
                return ControlResponses.Error(errorMessage ?? "Failed to run tests.", errorStatus ?? "tool_exception", request.name);
            }

            return ControlResponses.Success("unity_project_run_tests completed.", result);
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
