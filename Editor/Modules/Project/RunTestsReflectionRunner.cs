using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class RunTestsReflectionRunner
    {
        private const string TestRunnerApiTypeName = "UnityEditor.TestTools.TestRunner.Api.TestRunnerApi";
        private const string ExecutionSettingsTypeName = "UnityEditor.TestTools.TestRunner.Api.ExecutionSettings";
        private const string FilterTypeName = "UnityEditor.TestTools.TestRunner.Api.Filter";
        private const string TestModeTypeName = "UnityEditor.TestTools.TestRunner.Api.TestMode";
        private static readonly object RecoveryLock = new object();
        private static readonly Dictionary<string, RecoveryWatcher> RecoveryWatchers = new Dictionary<string, RecoveryWatcher>(StringComparer.Ordinal);

        public static bool TryRun(
            RunTestsArgs args,
            MainThreadActionInvoker mainThreadInvoker,
            out RunTestsResult result,
            out string errorStatus,
            out string errorMessage)
        {
            result = null;
            errorStatus = null;
            errorMessage = null;

            EnsureRunId(args);
            string requestedMode = NormalizeMode(args.mode);
            int timeoutMs = Mathf.Clamp(args.timeoutMs, 5000, 3600000);
            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            Type apiType = FindType(TestRunnerApiTypeName);
            Type settingsType = FindType(ExecutionSettingsTypeName);
            Type filterType = FindType(FilterTypeName);
            Type testModeType = FindType(TestModeTypeName);

            if (apiType == null || settingsType == null || filterType == null || testModeType == null)
            {
                errorStatus = "unsupported";
                errorMessage = "Unity Test Framework API not found. Ensure com.unity.test-framework is installed.";
                TestRunStore.MarkFailed(args.runId, requestedMode, errorStatus, errorMessage, args.includeXmlReportPath);
                return false;
            }

            TestRunStore.Begin(args, requestedMode, deadlineUtc);

            object api = null;
            object settings = null;
            object filter = null;
            var observer = new TestRunObserver(args.includePassed);

            if (!mainThreadInvoker(() =>
                {
                    api = CreateApiInstance(apiType);
                    filter = CreateFilter(filterType, testModeType, requestedMode, args.filter);
                    settings = CreateExecutionSettings(settingsType, filterType, filter, requestedMode);
                    observer.TryAttach(api, apiType);
                },
                timeoutMs: RemainingMs(deadlineUtc),
                out string setupError))
            {
                errorStatus = DateTime.UtcNow >= deadlineUtc ? "test_run_startup_timeout" : "tool_exception";
                errorMessage = "Failed to initialize test runner: " + setupError;
                TestRunStore.MarkFailed(args.runId, requestedMode, errorStatus, errorMessage, args.includeXmlReportPath);
                return false;
            }

            if (api == null || settings == null)
            {
                errorStatus = "tool_exception";
                errorMessage = "Failed to initialize test runner objects.";
                TestRunStore.MarkFailed(args.runId, requestedMode, errorStatus, errorMessage, args.includeXmlReportPath);
                return false;
            }

            MethodInfo execute = FindExecuteMethod(apiType, settingsType);
            if (execute == null)
            {
                observer.TryDetach(mainThreadInvoker, api, apiType);
                errorStatus = "unsupported";
                errorMessage = "TestRunnerApi.Execute method not found.";
                TestRunStore.MarkFailed(args.runId, requestedMode, errorStatus, errorMessage, args.includeXmlReportPath);
                return false;
            }

            bool started = mainThreadInvoker(
                () => ExecuteRun(api, execute, settings, observer),
                timeoutMs: RemainingMs(deadlineUtc),
                out string executeError);
            if (!started)
            {
                observer.TryDetach(mainThreadInvoker, api, apiType);
                errorStatus = DateTime.UtcNow >= deadlineUtc ? "test_run_execute_timeout" : "tool_exception";
                errorMessage = "Failed to start test run: " + executeError;
                TestRunStore.MarkFailed(args.runId, requestedMode, errorStatus, errorMessage, args.includeXmlReportPath);
                return false;
            }

            TestRunStore.MarkRunning(args.runId, requestedMode, args.includeXmlReportPath);
            int remainingMs = RemainingMs(deadlineUtc);
            if (remainingMs <= 0 || !observer.WaitForFinished(remainingMs))
            {
                CancelRun(mainThreadInvoker, api, apiType);
                observer.TryDetach(mainThreadInvoker, api, apiType);
                errorStatus = "test_run_timeout";
                errorMessage = "Test run timed out after " + timeoutMs + " ms.";
                TestRunStore.MarkFailed(args.runId, requestedMode, errorStatus, errorMessage, args.includeXmlReportPath);
                return false;
            }

            observer.TryDetach(mainThreadInvoker, api, apiType);
            if (observer.RunError != null)
            {
                errorStatus = "tool_exception";
                errorMessage = observer.RunError;
                TestRunStore.MarkFailed(args.runId, requestedMode, errorStatus, errorMessage, args.includeXmlReportPath);
                return false;
            }

            var summary = observer.BuildSummary();
            result = new RunTestsResult
            {
                runId = args.runId,
                mode = requestedMode,
                summary = summary,
                results = observer.Results.ToArray(),
            };
            result.artifacts = BuildArtifacts(args.includeXmlReportPath, result, isRecovered: false);
            TestRunStore.MarkCompleted(result, isRecovered: false);

            return true;
        }

        private static int RemainingMs(DateTime deadlineUtc)
        {
            double remaining = (deadlineUtc - DateTime.UtcNow).TotalMilliseconds;
            if (remaining <= 0)
            {
                return 0;
            }

            return (int)Math.Min(int.MaxValue, remaining);
        }

        public static ControlToolCallResponse HandleGetTestRunState(
            ControlToolCallRequest request,
            MainThreadActionInvoker mainThreadInvoker)
        {
            TestRunStateQueryArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new TestRunStateQueryArgs());

            if (string.IsNullOrWhiteSpace(args.runId))
            {
                return ControlResponses.Error("runId is required.", "invalid_argument", request.name);
            }

            string runId = TestRunStore.NormalizeRunId(args.runId);

            TestRunState state;
            if (!TestRunStore.TryLoad(runId, out state))
            {
                return ControlResponses.Success(
                    "Test run state not found.",
                    new TestRunStateQueryResult
                    {
                        status = "unknown",
                        runId = runId,
                        statePath = TestRunStore.GetStatePath(runId),
                        xmlReportPath = TestRunStore.GetXmlReportPath(runId),
                    });
            }

            if (string.Equals(state.status, "running", StringComparison.Ordinal))
            {
                EnsureRecoveryWatcher(state, mainThreadInvoker);
                TestRunStore.TryLoad(runId, out state);
            }

            return ControlResponses.Success("Test run state loaded.", TestRunStore.ToQueryResult(state));
        }

        private static string NormalizeMode(string raw)
        {
            if (string.Equals(raw, "PlayMode", StringComparison.OrdinalIgnoreCase))
            {
                return "PlayMode";
            }

            return "EditMode";
        }

        private static void EnsureRunId(RunTestsArgs args)
        {
            if (args == null)
            {
                return;
            }

            args.runId = TestRunStore.NormalizeRunId(args.runId);
        }

        private static RunTestsArtifacts BuildArtifacts(bool includeXmlReportPath, RunTestsResult result, bool isRecovered)
        {
            if (!includeXmlReportPath)
            {
                return new RunTestsArtifacts
                {
                    statePath = TestRunStore.GetStatePath(result.runId),
                    isRecovered = isRecovered,
                };
            }

            string reportsDir = TestRunStore.GetReportsRoot();
            string xmlPath = TestRunStore.GetXmlReportPath(result.runId);
            string latestXmlPath = Path.Combine(reportsDir, "latest-test-results.xml");
            try
            {
                Directory.CreateDirectory(reportsDir);
                Directory.CreateDirectory(Path.GetDirectoryName(xmlPath));
                File.WriteAllText(xmlPath, BuildXmlReport(result));
                File.WriteAllText(latestXmlPath, BuildXmlReport(result));
            }
            catch
            {
            }

            return new RunTestsArtifacts
            {
                xmlReportPath = xmlPath,
                statePath = TestRunStore.GetStatePath(result.runId),
                isRecovered = isRecovered,
            };
        }

        private static string BuildXmlReport(RunTestsResult result)
        {
            var sb = new StringBuilder(1024);
            sb.Append("<test-run");
            AppendAttr(sb, "id", result.runId);
            AppendAttr(sb, "mode", result.mode);
            AppendAttr(sb, "total", result.summary.total.ToString(CultureInfo.InvariantCulture));
            AppendAttr(sb, "passed", result.summary.passed.ToString(CultureInfo.InvariantCulture));
            AppendAttr(sb, "failed", result.summary.failed.ToString(CultureInfo.InvariantCulture));
            AppendAttr(sb, "skipped", result.summary.skipped.ToString(CultureInfo.InvariantCulture));
            AppendAttr(sb, "inconclusive", result.summary.inconclusive.ToString(CultureInfo.InvariantCulture));
            AppendAttr(sb, "durationMs", result.summary.durationMs.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(">");

            if (result.results != null)
            {
                for (int i = 0; i < result.results.Length; i++)
                {
                    RunTestCaseResult item = result.results[i];
                    sb.Append("  <test-case");
                    AppendAttr(sb, "fullName", item.fullName);
                    AppendAttr(sb, "outcome", item.outcome);
                    AppendAttr(sb, "durationMs", item.durationMs.ToString(CultureInfo.InvariantCulture));
                    if (!string.IsNullOrEmpty(item.filePath))
                    {
                        AppendAttr(sb, "filePath", item.filePath);
                    }

                    if (item.hasLine)
                    {
                        AppendAttr(sb, "line", item.line.ToString(CultureInfo.InvariantCulture));
                    }

                    sb.AppendLine(">");
                    if (!string.IsNullOrEmpty(item.message))
                    {
                        sb.Append("    <message>");
                        sb.Append(EscapeXml(item.message));
                        sb.AppendLine("</message>");
                    }

                    if (!string.IsNullOrEmpty(item.stackTrace))
                    {
                        sb.Append("    <stackTrace>");
                        sb.Append(EscapeXml(item.stackTrace));
                        sb.AppendLine("</stackTrace>");
                    }

                    sb.AppendLine("  </test-case>");
                }
            }

            sb.AppendLine("</test-run>");
            return sb.ToString();
        }

        private static void AppendAttr(StringBuilder sb, string key, string value)
        {
            sb.Append(' ');
            sb.Append(key);
            sb.Append("=\"");
            sb.Append(EscapeXml(value));
            sb.Append('"');
        }

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static void ExecuteRun(object api, MethodInfo execute, object settings, TestRunObserver observer)
        {
            ParameterInfo[] parameters = execute.GetParameters();
            if (parameters.Length == 1)
            {
                execute.Invoke(api, new[] { settings });
                return;
            }

            if (parameters.Length == 2)
            {
                Delegate callback = CreateDelegate(parameters[1].ParameterType, observer, nameof(TestRunObserver.OnRunFinished));
                execute.Invoke(api, new object[] { settings, callback });
                return;
            }

            throw new InvalidOperationException("Unsupported Execute signature.");
        }

        private static void CancelRun(MainThreadActionInvoker mainThreadInvoker, object api, Type apiType)
        {
            MethodInfo cancel = apiType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(m => string.Equals(m.Name, "CancelTestRun", StringComparison.Ordinal) && m.GetParameters().Length == 0);
            if (cancel == null)
            {
                return;
            }

            mainThreadInvoker(
                () =>
                {
                    object target = cancel.IsStatic ? null : api;
                    cancel.Invoke(target, Array.Empty<object>());
                },
                5000,
                out _);
        }

        private static MethodInfo FindExecuteMethod(Type apiType, Type settingsType)
        {
            MethodInfo[] methods = apiType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (MethodInfo method in methods)
            {
                if (!string.Equals(method.Name, "Execute", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length >= 1 && parameters[0].ParameterType == settingsType)
                {
                    return method;
                }
            }

            return null;
        }

        private static object CreateApiInstance(Type apiType)
        {
            MethodInfo getDefault = apiType.GetMethod("GetDefault", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (getDefault != null)
            {
                object api = getDefault.Invoke(null, null);
                if (api != null)
                {
                    return api;
                }
            }

            MethodInfo createInstance = typeof(ScriptableObject).GetMethod(
                "CreateInstance",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Type) },
                null);
            object created = createInstance.Invoke(null, new object[] { apiType });
            if (created == null)
            {
                throw new InvalidOperationException("Unable to create TestRunnerApi instance.");
            }

            return created;
        }

        private static object CreateFilter(Type filterType, Type testModeType, string mode, RunTestsFilter filterArgs)
        {
            object filter = Activator.CreateInstance(filterType);
            SetMember(filter, "testMode", Enum.Parse(testModeType, mode));
            if (filterArgs != null)
            {
                if (filterArgs.assemblyNames != null && filterArgs.assemblyNames.Length > 0)
                {
                    SetMember(filter, "assemblyNames", filterArgs.assemblyNames);
                }

                if (filterArgs.testNames != null && filterArgs.testNames.Length > 0)
                {
                    SetMember(filter, "testNames", filterArgs.testNames);
                }

                if (filterArgs.categoryNames != null && filterArgs.categoryNames.Length > 0)
                {
                    SetMember(filter, "categoryNames", filterArgs.categoryNames);
                }
            }

            return filter;
        }

        private static object CreateExecutionSettings(Type settingsType, Type filterType, object filter, string mode)
        {
            Array filtersArray = Array.CreateInstance(filterType, 1);
            filtersArray.SetValue(filter, 0);

            object settings = null;
            ConstructorInfo[] constructors = settingsType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < constructors.Length; i++)
            {
                ConstructorInfo ctor = constructors[i];
                ParameterInfo[] parameters = ctor.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsArray)
                {
                    settings = ctor.Invoke(new object[] { filtersArray });
                    break;
                }
            }

            if (settings == null)
            {
                settings = Activator.CreateInstance(settingsType);
            }

            SetMember(settings, "filters", filtersArray);
            if (string.Equals(mode, "EditMode", StringComparison.Ordinal))
            {
                SetMember(settings, "runSynchronously", true);
            }

            return settings;
        }

        private static Type FindType(string fullName)
        {
            Type direct = Type.GetType(fullName, false);
            if (direct != null)
            {
                return direct;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type match = assemblies[i].GetType(fullName, false);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static void SetMember(object target, string memberName, object value)
        {
            if (target == null)
            {
                return;
            }

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return;
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }

        private static Delegate CreateDelegate(Type delegateType, object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException("Callback method not found: " + methodName);
            }

            MethodInfo invoke = delegateType.GetMethod("Invoke");
            if (invoke == null)
            {
                throw new InvalidOperationException("Invalid delegate type for callback: " + delegateType.FullName);
            }

            ParameterInfo[] invokeParameters = invoke.GetParameters();
            ParameterExpression[] lambdaParams = invokeParameters
                .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                .ToArray();

            Expression callbackArg;
            if (lambdaParams.Length == 0)
            {
                callbackArg = Expression.Constant(null, typeof(object));
            }
            else if (lambdaParams.Length == 1)
            {
                callbackArg = Expression.Convert(lambdaParams[0], typeof(object));
            }
            else
            {
                callbackArg = Expression.Convert(lambdaParams[1], typeof(object));
            }

            MethodCallExpression call = Expression.Call(Expression.Constant(target), method, callbackArg);
            LambdaExpression lambda = Expression.Lambda(delegateType, call, lambdaParams);
            Delegate compiled = lambda.Compile();
            if (compiled != null)
            {
                return compiled;
            }

            throw new InvalidOperationException("Unable to create callback delegate for " + methodName + ".");
        }

        private static void EnsureRecoveryWatcher(TestRunState state, MainThreadActionInvoker mainThreadInvoker)
        {
            if (state == null || string.IsNullOrEmpty(state.runId) || mainThreadInvoker == null)
            {
                return;
            }

            lock (RecoveryLock)
            {
                if (RecoveryWatchers.ContainsKey(state.runId))
                {
                    return;
                }
            }

            RunTestsArgs args = ControlJson.ParseArgs(state.argumentsJson, new RunTestsArgs());
            EnsureRunId(args);
            if (!string.Equals(args.runId, state.runId, StringComparison.Ordinal))
            {
                args.runId = state.runId;
            }

            Type apiType = FindType(TestRunnerApiTypeName);
            if (apiType == null)
            {
                TestRunStore.MarkFailed(state.runId, state.mode, "unsupported", "Unity Test Framework API not found during test run recovery.", args.includeXmlReportPath);
                return;
            }

            object api = null;
            TestRunObserver observer = null;
            bool attached = mainThreadInvoker(
                () =>
                {
                    api = CreateApiInstance(apiType);
                    observer = new TestRunObserver(
                        args.includePassed,
                        finishedObserver => CompleteRecoveredRun(state.runId, state.mode, args.includeXmlReportPath, finishedObserver, api, apiType));
                    observer.TryAttach(api, apiType);
                },
                Math.Max(1000, RemainingRecoveryMs(state)),
                out string attachError);

            if (!attached || api == null || observer == null)
            {
                TestRunStore.MarkRecoveryMessage(
                    state.runId,
                    "Failed to attach test run recovery callbacks: " + attachError,
                    args.includeXmlReportPath);
                return;
            }

            lock (RecoveryLock)
            {
                if (!RecoveryWatchers.ContainsKey(state.runId))
                {
                    RecoveryWatchers[state.runId] = new RecoveryWatcher
                    {
                        RunId = state.runId,
                        Api = api,
                        ApiType = apiType,
                        Observer = observer,
                    };
                }
            }
        }

        private static int RemainingRecoveryMs(TestRunState state)
        {
            if (state == null || string.IsNullOrEmpty(state.deadlineUtc))
            {
                return 15000;
            }

            DateTime deadline;
            if (!DateTime.TryParse(
                    state.deadlineUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out deadline))
            {
                return 15000;
            }

            double remaining = (deadline.ToUniversalTime() - DateTime.UtcNow).TotalMilliseconds;
            if (remaining <= 0)
            {
                return 0;
            }

            return (int)Math.Min(int.MaxValue, remaining);
        }

        private static void CompleteRecoveredRun(
            string runId,
            string mode,
            bool includeXmlReportPath,
            TestRunObserver observer,
            object api,
            Type apiType)
        {
            try
            {
                if (observer.RunError != null)
                {
                    TestRunStore.MarkFailed(runId, mode, "tool_exception", observer.RunError, includeXmlReportPath);
                    return;
                }

                var result = new RunTestsResult
                {
                    runId = runId,
                    mode = mode,
                    summary = observer.BuildSummary(),
                    results = observer.Results.ToArray(),
                };
                result.artifacts = BuildArtifacts(includeXmlReportPath, result, isRecovered: true);
                TestRunStore.MarkCompleted(result, isRecovered: true);
            }
            finally
            {
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        observer.DetachDirect(api, apiType);
                    }
                    catch
                    {
                        // Best effort only during recovery cleanup.
                    }
                };

                lock (RecoveryLock)
                {
                    RecoveryWatchers.Remove(runId);
                }
            }
        }

        [Serializable]
        private sealed class TestRunState
        {
            public string status;
            public string runId;
            public string mode;
            public string message;
            public string errorStatus;
            public string startedUtc;
            public string completedUtc;
            public string deadlineUtc;
            public string phase;
            public string lastRecoveryMessage;
            public string argumentsJson;
            public string resultJson;
            public string statePath;
            public string xmlReportPath;
            public bool isRecovered;
        }

        private sealed class RecoveryWatcher
        {
            public string RunId;
            public object Api;
            public Type ApiType;
            public TestRunObserver Observer;
        }

        private static class TestRunStore
        {
            public static string NormalizeRunId(string runId)
            {
                if (string.IsNullOrWhiteSpace(runId))
                {
                    return Guid.NewGuid().ToString("N");
                }

                var builder = new StringBuilder(runId.Length);
                for (int i = 0; i < runId.Length; i++)
                {
                    char ch = runId[i];
                    if ((ch >= 'a' && ch <= 'z') ||
                        (ch >= 'A' && ch <= 'Z') ||
                        (ch >= '0' && ch <= '9') ||
                        ch == '-' ||
                        ch == '_')
                    {
                        builder.Append(ch);
                    }
                }

                string normalized = builder.ToString();
                return string.IsNullOrEmpty(normalized) ? Guid.NewGuid().ToString("N") : normalized;
            }

            public static void Begin(RunTestsArgs args, string mode, DateTime deadlineUtc)
            {
                var state = new TestRunState
                {
                    status = "starting",
                    runId = args.runId,
                    mode = mode,
                    message = "Test run is starting.",
                    startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    deadlineUtc = deadlineUtc.ToString("O", CultureInfo.InvariantCulture),
                    phase = "startup",
                    argumentsJson = JsonUtility.ToJson(args),
                    statePath = GetStatePath(args.runId),
                    xmlReportPath = args.includeXmlReportPath ? GetXmlReportPath(args.runId) : null,
                    isRecovered = false,
                };

                Save(state);
            }

            public static void MarkRunning(string runId, string mode, bool includeXmlReportPath)
            {
                runId = NormalizeRunId(runId);
                var state = LoadOrCreate(runId);
                state.status = "running";
                state.mode = string.IsNullOrEmpty(mode) ? "Unknown" : mode;
                state.message = "Test run is executing.";
                state.phase = "waiting_for_results";
                state.statePath = GetStatePath(runId);
                state.xmlReportPath = includeXmlReportPath ? GetXmlReportPath(runId) : null;
                Save(state);
            }

            public static void MarkCompleted(RunTestsResult result, bool isRecovered)
            {
                if (result == null)
                {
                    return;
                }

                var state = LoadOrCreate(result.runId);
                state.status = "completed";
                state.mode = result.mode;
                state.message = "Test run completed.";
                state.phase = "completed";
                state.completedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                state.resultJson = JsonUtility.ToJson(result);
                state.statePath = GetStatePath(result.runId);
                state.xmlReportPath = result.artifacts == null ? null : result.artifacts.xmlReportPath;
                state.isRecovered = isRecovered;
                Save(state);
            }

            public static void MarkFailed(string runId, string mode, string status, string message, bool includeXmlReportPath)
            {
                runId = NormalizeRunId(runId);
                var state = LoadOrCreate(runId);
                state.status = "failed";
                state.mode = string.IsNullOrEmpty(mode) ? "Unknown" : mode;
                state.errorStatus = string.IsNullOrEmpty(status) ? "tool_exception" : status;
                state.message = message;
                state.phase = status;
                state.completedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                state.statePath = GetStatePath(runId);
                state.xmlReportPath = includeXmlReportPath ? GetXmlReportPath(runId) : null;
                Save(state);
            }

            public static void MarkRecoveryMessage(string runId, string message, bool includeXmlReportPath)
            {
                runId = NormalizeRunId(runId);
                var state = LoadOrCreate(runId);
                state.lastRecoveryMessage = message;
                state.message = string.IsNullOrEmpty(state.message) ? message : state.message;
                state.statePath = GetStatePath(runId);
                state.xmlReportPath = includeXmlReportPath ? GetXmlReportPath(runId) : state.xmlReportPath;
                Save(state);
            }

            public static bool TryLoad(string runId, out TestRunState state)
            {
                state = null;
                string path = GetStatePath(runId);
                if (!File.Exists(path))
                {
                    return false;
                }

                try
                {
                    state = JsonUtility.FromJson<TestRunState>(File.ReadAllText(path));
                    if (state == null)
                    {
                        return false;
                    }

                    state.runId = NormalizeRunId(state.runId);
                    state.statePath = path;
                    if (string.IsNullOrEmpty(state.xmlReportPath))
                    {
                        state.xmlReportPath = GetXmlReportPath(state.runId);
                    }

                    ExpireIfNeeded(state);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            public static TestRunStateQueryResult ToQueryResult(TestRunState state)
            {
                if (state == null)
                {
                    return null;
                }

                return new TestRunStateQueryResult
                {
                    status = state.status,
                    runId = state.runId,
                    mode = state.mode,
                    message = state.message,
                    phase = state.phase,
                    deadlineUtc = state.deadlineUtc,
                    statePath = state.statePath,
                    xmlReportPath = state.xmlReportPath,
                    isRecovered = state.isRecovered,
                    resultJson = state.resultJson,
                };
            }

            public static string GetReportsRoot()
            {
                return Path.Combine(ControlUtil.GetProjectRootPath(), "Library", "McpReports");
            }

            public static string GetStatePath(string runId)
            {
                return Path.Combine(GetRunDirectory(runId), "state.json");
            }

            public static string GetXmlReportPath(string runId)
            {
                return Path.Combine(GetRunDirectory(runId), runId + ".xml");
            }

            private static string GetRunDirectory(string runId)
            {
                return Path.Combine(GetReportsRoot(), "test-runs", NormalizeRunId(runId));
            }

            private static TestRunState LoadOrCreate(string runId)
            {
                TestRunState state;
                if (TryLoad(runId, out state))
                {
                    return state;
                }

                runId = NormalizeRunId(runId);
                return new TestRunState
                {
                    runId = runId,
                    statePath = GetStatePath(runId),
                    xmlReportPath = GetXmlReportPath(runId),
                    startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                };
            }

            private static void ExpireIfNeeded(TestRunState state)
            {
                if (state == null ||
                    string.Equals(state.status, "completed", StringComparison.Ordinal) ||
                    string.Equals(state.status, "failed", StringComparison.Ordinal) ||
                    string.Equals(state.status, "expired", StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(state.deadlineUtc))
                {
                    return;
                }

                DateTime deadline;
                if (!DateTime.TryParse(
                        state.deadlineUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out deadline))
                {
                    return;
                }

                if (DateTime.UtcNow <= deadline.ToUniversalTime())
                {
                    return;
                }

                state.status = "expired";
                state.errorStatus = "test_run_timeout";
                state.phase = "expired";
                state.message = "Test run expired before Unity Control could report a final result.";
                state.completedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                Save(state);
            }

            private static void Save(TestRunState state)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(state.statePath));
                    File.WriteAllText(state.statePath, JsonUtility.ToJson(state, true));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[UnityMcpControl] Failed to write test run state: " + ex.Message);
                }
            }
        }

        private sealed class TestRunObserver
        {
            private readonly ManualResetEventSlim _finished = new ManualResetEventSlim(false);
            private readonly bool _includePassed;
            private readonly Action<TestRunObserver> _finishedCallback;
            private readonly List<EventSubscription> _subscriptions = new List<EventSubscription>();
            private readonly object _sync = new object();
            private object _rootResult;
            private object _registeredCallback;
            private Type _registeredCallbackType;
            private MethodInfo _unregisterCallbacks;

            public readonly List<RunTestCaseResult> Results = new List<RunTestCaseResult>();
            public string RunError { get; private set; }

            public TestRunObserver(bool includePassed, Action<TestRunObserver> finishedCallback = null)
            {
                _includePassed = includePassed;
                _finishedCallback = finishedCallback;
            }

            public void OnRunFinished(object runResult)
            {
                lock (_sync)
                {
                    _rootResult = runResult;
                    try
                    {
                        Results.Clear();
                        CollectLeafResults(runResult, Results, _includePassed);
                    }
                    catch (Exception ex)
                    {
                        RunError = "Failed to parse test results: " + ex.Message;
                    }
                    finally
                    {
                        _finished.Set();
                    }
                }

                _finishedCallback?.Invoke(this);
            }

            public void OnRunStarted(object _)
            {
            }

            public void OnTestStarted(object _)
            {
            }

            public void OnTestFinished(object _)
            {
            }

            public bool WaitForFinished(int timeoutMs)
            {
                return _finished.Wait(timeoutMs);
            }

            public RunTestsSummary BuildSummary()
            {
                if (_rootResult == null)
                {
                    return new RunTestsSummary
                    {
                        total = Results.Count,
                        passed = Results.Count(r => string.Equals(r.outcome, "Passed", StringComparison.OrdinalIgnoreCase)),
                        failed = Results.Count(r => string.Equals(r.outcome, "Failed", StringComparison.OrdinalIgnoreCase)),
                        skipped = Results.Count(r => string.Equals(r.outcome, "Skipped", StringComparison.OrdinalIgnoreCase)),
                        inconclusive = Results.Count(r => string.Equals(r.outcome, "Inconclusive", StringComparison.OrdinalIgnoreCase)),
                        durationMs = Results.Sum(r => Math.Max(0, r.durationMs)),
                    };
                }

                int total = ReadInt(_rootResult, "PassCount") + ReadInt(_rootResult, "FailCount") + ReadInt(_rootResult, "SkipCount") + ReadInt(_rootResult, "InconclusiveCount");
                if (total <= 0)
                {
                    total = Results.Count;
                }

                return new RunTestsSummary
                {
                    total = total,
                    passed = ReadInt(_rootResult, "PassCount"),
                    failed = ReadInt(_rootResult, "FailCount"),
                    skipped = ReadInt(_rootResult, "SkipCount"),
                    inconclusive = ReadInt(_rootResult, "InconclusiveCount"),
                    durationMs = ReadDurationMs(_rootResult),
                };
            }

            public void TryAttach(object api, Type apiType)
            {
                if (TryRegisterCallbacks(api, apiType))
                {
                    return;
                }

                string[] candidateNames = { "RunFinished", "RunStarted", "TestFinished", "TestStarted" };
                for (int i = 0; i < candidateNames.Length; i++)
                {
                    string eventName = candidateNames[i];
                    EventInfo eventInfo = apiType.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    if (eventInfo == null)
                    {
                        continue;
                    }

                    string handlerName = "On" + eventName;
                    Delegate callback = CreateDelegate(eventInfo.EventHandlerType, this, handlerName);
                    object eventTarget = GetEventTarget(api, eventInfo);
                    eventInfo.AddEventHandler(eventTarget, callback);
                    _subscriptions.Add(new EventSubscription(eventInfo, eventTarget, callback));
                }

                if (_subscriptions.Count == 0)
                {
                    throw new InvalidOperationException("No test runner callbacks available.");
                }
            }

            public void TryDetach(MainThreadActionInvoker invoker, object api, Type apiType)
            {
                if (_subscriptions.Count == 0 && _registeredCallback == null)
                {
                    return;
                }

                invoker(() => DetachDirect(api, apiType), 5000, out _);
            }

            public void DetachDirect(object api, Type apiType)
            {
                if (_registeredCallback != null && _unregisterCallbacks != null)
                {
                    MethodInfo unregister = _unregisterCallbacks.MakeGenericMethod(_registeredCallbackType);
                    unregister.Invoke(api, new[] { _registeredCallback });
                    _registeredCallback = null;
                    _registeredCallbackType = null;
                    _unregisterCallbacks = null;
                }

                for (int i = 0; i < _subscriptions.Count; i++)
                {
                    EventSubscription sub = _subscriptions[i];
                    sub.EventInfo.RemoveEventHandler(sub.Target, sub.Delegate);
                }

                _subscriptions.Clear();
            }

            private bool TryRegisterCallbacks(object api, Type apiType)
            {
                MethodInfo register = apiType
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                        string.Equals(m.Name, "RegisterCallbacks", StringComparison.Ordinal) &&
                        m.IsGenericMethodDefinition &&
                        m.GetParameters().Length >= 1);
                MethodInfo unregister = apiType
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                        string.Equals(m.Name, "UnregisterCallbacks", StringComparison.Ordinal) &&
                        m.IsGenericMethodDefinition &&
                        m.GetParameters().Length == 1);
                if (register == null || unregister == null)
                {
                    return false;
                }

                Type callbackType = register.GetGenericArguments()[0]
                    .GetGenericParameterConstraints()
                    .FirstOrDefault(type => type.IsInterface);
                if (callbackType == null)
                {
                    return false;
                }

                MethodInfo createProxy = typeof(DispatchProxy)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => string.Equals(m.Name, "Create", StringComparison.Ordinal) &&
                                m.IsGenericMethodDefinition &&
                                m.GetGenericArguments().Length == 2);
                object callback = createProxy
                    .MakeGenericMethod(callbackType, typeof(RunTestsCallbacksProxy))
                    .Invoke(null, null);
                ((RunTestsCallbacksProxy)callback).Callback = HandleCallback;

                MethodInfo closedRegister = register.MakeGenericMethod(callbackType);
                ParameterInfo[] parameters = closedRegister.GetParameters();
                object[] arguments = parameters.Length == 1
                    ? new[] { callback }
                    : new[] { callback, (object)0 };
                closedRegister.Invoke(api, arguments);

                _registeredCallback = callback;
                _registeredCallbackType = callbackType;
                _unregisterCallbacks = unregister;
                return true;
            }

            private void HandleCallback(string methodName, object value)
            {
                switch (methodName)
                {
                    case "RunFinished":
                        OnRunFinished(value);
                        break;
                    case "RunStarted":
                        OnRunStarted(value);
                        break;
                    case "TestFinished":
                        OnTestFinished(value);
                        break;
                    case "TestStarted":
                        OnTestStarted(value);
                        break;
                }
            }

            private static object GetEventTarget(object api, EventInfo eventInfo)
            {
                MethodInfo addMethod = eventInfo.GetAddMethod(true);
                return addMethod != null && addMethod.IsStatic ? null : api;
            }

            private static void CollectLeafResults(object node, List<RunTestCaseResult> output, bool includePassed)
            {
                if (node == null)
                {
                    return;
                }

                bool hasChildren = ReadBool(node, "HasChildren");
                if (!hasChildren)
                {
                    string outcome = ReadString(node, "TestStatus");
                    if (!includePassed && string.Equals(outcome, "Passed", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var item = new RunTestCaseResult
                    {
                        fullName = ReadString(node, "FullName"),
                        outcome = string.IsNullOrEmpty(outcome) ? "Unknown" : outcome,
                        durationMs = ReadDurationMs(node),
                        message = ReadString(node, "Message"),
                        stackTrace = ReadString(node, "StackTrace"),
                    };

                    TryReadSource(node, item);
                    output.Add(item);
                    return;
                }

                IEnumerable children = ReadEnumerable(node, "Children");
                if (children == null)
                {
                    return;
                }

                foreach (object child in children)
                {
                    CollectLeafResults(child, output, includePassed);
                }
            }

            private static void TryReadSource(object result, RunTestCaseResult item)
            {
                object test = ReadObject(result, "Test");
                if (test == null)
                {
                    return;
                }

                item.filePath = ReadString(test, "SourceFilePath");
                int line = ReadInt(test, "LineNumber");
                if (line > 0)
                {
                    item.line = line;
                    item.hasLine = true;
                }
            }

            private static string ReadString(object target, string member)
            {
                object value = ReadObject(target, member);
                return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            private static int ReadInt(object target, string member)
            {
                object value = ReadObject(target, member);
                if (value == null)
                {
                    return 0;
                }

                if (value is int i)
                {
                    return i;
                }

                if (value is long l)
                {
                    return (int)l;
                }

                if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    return parsed;
                }

                return 0;
            }

            private static bool ReadBool(object target, string member)
            {
                object value = ReadObject(target, member);
                if (value is bool b)
                {
                    return b;
                }

                if (value == null)
                {
                    return false;
                }

                if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bool parsed))
                {
                    return parsed;
                }

                return false;
            }

            private static int ReadDurationMs(object target)
            {
                object value = ReadObject(target, "Duration");
                if (value == null)
                {
                    return 0;
                }

                double seconds;
                if (value is double d)
                {
                    seconds = d;
                }
                else if (value is float f)
                {
                    seconds = f;
                }
                else if (value is int i)
                {
                    seconds = i;
                }
                else if (value is long l)
                {
                    seconds = l;
                }
                else if (!double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                {
                    return 0;
                }

                if (seconds < 0)
                {
                    seconds = 0;
                }

                return (int)Math.Round(seconds * 1000d);
            }

            private static IEnumerable ReadEnumerable(object target, string member)
            {
                return ReadObject(target, member) as IEnumerable;
            }

            private static object ReadObject(object target, string member)
            {
                if (target == null)
                {
                    return null;
                }

                Type type = target.GetType();
                PropertyInfo property = type.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property != null)
                {
                    return property.GetValue(target, null);
                }

                FieldInfo field = type.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    return field.GetValue(target);
                }

                return null;
            }

            private sealed class EventSubscription
            {
                public readonly EventInfo EventInfo;
                public readonly object Target;
                public readonly Delegate Delegate;

                public EventSubscription(EventInfo eventInfo, object target, Delegate @delegate)
                {
                    EventInfo = eventInfo;
                    Target = target;
                    Delegate = @delegate;
                }
            }

        }
    }

    public class RunTestsCallbacksProxy : DispatchProxy
    {
        public Action<string, object> Callback;

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            object value = args != null && args.Length > 0 ? args[0] : null;
            Callback?.Invoke(targetMethod.Name, value);
            return null;
        }
    }
}
