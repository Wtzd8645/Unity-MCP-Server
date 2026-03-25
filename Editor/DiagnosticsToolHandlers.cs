using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Blanketmen.UnityMcp.Control.Editor
{
    internal static class DiagnosticsToolHandlers
    {
        public static ControlToolCallResponse HandleGetConsoleLogs(ControlToolCallRequest request, UnityControlLogStore logStore)
        {
            GetConsoleLogsArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GetConsoleLogsArgs
                {
                    includeStackTrace = true,
                    limit = 200,
                    order = "desc",
                });

            long sinceId = ControlUtil.ParseLong(args.sinceId, 0);
            HashSet<string> levels = BuildLevelSet(args.levels);
            LogSnapshot snapshot = logStore.Snapshot();

            IEnumerable<ControlLogEntry> filtered = snapshot.entries
                .Where(entry => entry.id > sinceId && IsLevelAccepted(entry.level, levels));
            if (string.Equals(args.order, "asc", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.OrderBy(entry => entry.id);
            }
            else
            {
                filtered = filtered.OrderByDescending(entry => entry.id);
            }

            int limit = ControlUtil.Clamp(args.limit, 1, 2000, 200);
            List<ControlLogEntry> page = filtered.Take(limit).ToList();

            var items = new List<ConsoleLogItem>(page.Count);
            long nextSinceId = snapshot.maxId;
            for (int i = 0; i < page.Count; i++)
            {
                ControlLogEntry log = page[i];
                if (log.id > nextSinceId)
                {
                    nextSinceId = log.id;
                }

                items.Add(new ConsoleLogItem
                {
                    id = log.id.ToString(),
                    level = log.level,
                    message = log.message,
                    stackTrace = args.includeStackTrace ? log.stackTrace : null,
                    timestampUtc = log.timestampUtc,
                });
            }

            var payload = new ConsoleLogsResult
            {
                nextSinceId = nextSinceId.ToString(),
                returned = items.Count,
                items = items.ToArray(),
            };

            return ControlResponses.Success("unity_editor_get_console_logs completed.", payload);
        }

        public static ControlToolCallResponse HandleClearConsole(UnityControlLogStore logStore)
        {
            logStore.Clear();

            bool clearedEditorConsole = TryClearEditorConsole();
            var payload = new ClearConsoleResult
            {
                cleared = true,
                clearedEditorConsole = clearedEditorConsole,
            };

            return ControlResponses.Success("unity_editor_clear_console completed.", payload);
        }

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

        private static HashSet<string> BuildLevelSet(string[] levels)
        {
            if (levels == null || levels.Length == 0)
            {
                return null;
            }

            return new HashSet<string>(levels, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsLevelAccepted(string level, HashSet<string> acceptedLevels)
        {
            if (acceptedLevels == null || acceptedLevels.Count == 0)
            {
                return true;
            }

            return acceptedLevels.Contains(level);
        }

        private static bool TryClearEditorConsole()
        {
            try
            {
                Type logEntries = Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
                if (logEntries == null)
                {
                    logEntries = Type.GetType("UnityEditorInternal.LogEntries, UnityEditor.dll");
                }

                if (logEntries == null)
                {
                    return false;
                }

                MethodInfo clear = logEntries.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (clear == null)
                {
                    return false;
                }

                clear.Invoke(null, null);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

