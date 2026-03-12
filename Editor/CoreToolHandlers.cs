using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
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

            return ControlResponses.Success("unity_ping handled by Unity control.", payload);
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

            return ControlResponses.Success("unity_project_info completed.", payload);
        }

        public static ControlToolCallResponse HandlePlaymodeStatus()
        {
            var payload = new PlaymodeStatusStructuredContent
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isChangingPlaymode = EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying,
            };

            return ControlResponses.Success("unity_playmode_status completed.", payload);
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

            return ControlResponses.Success("unity_playmode_start completed.", payload);
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

            return ControlResponses.Success("unity_playmode_stop completed.", payload);
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
    }
}

