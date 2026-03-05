#if UNITY_EDITOR
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
using UnityEngine;

namespace Blanketmen.UnityMcpBridge.Editor
{
    internal static class RunTestsReflectionRunner
    {
        private const string TestRunnerApiTypeName = "UnityEditor.TestTools.TestRunner.Api.TestRunnerApi";
        private const string ExecutionSettingsTypeName = "UnityEditor.TestTools.TestRunner.Api.ExecutionSettings";
        private const string FilterTypeName = "UnityEditor.TestTools.TestRunner.Api.Filter";
        private const string TestModeTypeName = "UnityEditor.TestTools.TestRunner.Api.TestMode";

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

            Type apiType = FindType(TestRunnerApiTypeName);
            Type settingsType = FindType(ExecutionSettingsTypeName);
            Type filterType = FindType(FilterTypeName);
            Type testModeType = FindType(TestModeTypeName);

            if (apiType == null || settingsType == null || filterType == null || testModeType == null)
            {
                errorStatus = "unsupported";
                errorMessage = "Unity Test Framework API not found. Ensure com.unity.test-framework is installed.";
                return false;
            }

            string requestedMode = NormalizeMode(args.mode);
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
                timeoutMs: 15000,
                out string setupError))
            {
                errorStatus = "tool_exception";
                errorMessage = "Failed to initialize test runner: " + setupError;
                return false;
            }

            if (api == null || settings == null)
            {
                errorStatus = "tool_exception";
                errorMessage = "Failed to initialize test runner objects.";
                return false;
            }

            MethodInfo execute = FindExecuteMethod(apiType, settingsType);
            if (execute == null)
            {
                observer.TryDetach(mainThreadInvoker, api, apiType);
                errorStatus = "unsupported";
                errorMessage = "TestRunnerApi.Execute method not found.";
                return false;
            }

            bool started = mainThreadInvoker(
                () => ExecuteRun(api, execute, settings, observer),
                timeoutMs: 30000,
                out string executeError);
            if (!started)
            {
                observer.TryDetach(mainThreadInvoker, api, apiType);
                errorStatus = "tool_exception";
                errorMessage = "Failed to start test run: " + executeError;
                return false;
            }

            int timeoutMs = Mathf.Clamp(args.timeoutMs, 5000, 3600000);
            if (!observer.WaitForFinished(timeoutMs))
            {
                CancelRun(mainThreadInvoker, api, apiType);
                observer.TryDetach(mainThreadInvoker, api, apiType);
                errorStatus = "tool_timeout";
                errorMessage = "Test run timed out after " + timeoutMs + " ms.";
                return false;
            }

            observer.TryDetach(mainThreadInvoker, api, apiType);
            if (observer.RunError != null)
            {
                errorStatus = "tool_exception";
                errorMessage = observer.RunError;
                return false;
            }

            var summary = observer.BuildSummary();
            result = new RunTestsResult
            {
                runId = Guid.NewGuid().ToString("N"),
                mode = requestedMode,
                summary = summary,
                results = observer.Results.ToArray(),
            };
            result.artifacts = BuildArtifacts(args.includeXmlReportPath, result);

            return true;
        }

        private static string NormalizeMode(string raw)
        {
            if (string.Equals(raw, "PlayMode", StringComparison.OrdinalIgnoreCase))
            {
                return "PlayMode";
            }

            return "EditMode";
        }

        private static RunTestsArtifacts BuildArtifacts(bool includeXmlReportPath, RunTestsResult result)
        {
            if (!includeXmlReportPath)
            {
                return null;
            }

            string root = BridgeUtil.GetProjectRootPath();
            string reportsDir = Path.Combine(root, "Library", "McpReports");
            string xmlPath = Path.Combine(reportsDir, "latest-test-results.xml");
            try
            {
                Directory.CreateDirectory(reportsDir);
                File.WriteAllText(xmlPath, BuildXmlReport(result));
            }
            catch
            {
                return null;
            }

            return new RunTestsArtifacts
            {
                xmlReportPath = xmlPath,
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

        private sealed class TestRunObserver
        {
            private readonly ManualResetEventSlim _finished = new ManualResetEventSlim(false);
            private readonly bool _includePassed;
            private readonly List<EventSubscription> _subscriptions = new List<EventSubscription>();
            private readonly object _sync = new object();
            private object _rootResult;

            public readonly List<RunTestCaseResult> Results = new List<RunTestCaseResult>();
            public string RunError { get; private set; }

            public TestRunObserver(bool includePassed)
            {
                _includePassed = includePassed;
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
                if (_subscriptions.Count == 0)
                {
                    return;
                }

                invoker(
                    () =>
                    {
                        for (int i = 0; i < _subscriptions.Count; i++)
                        {
                            EventSubscription sub = _subscriptions[i];
                            sub.EventInfo.RemoveEventHandler(sub.Target, sub.Delegate);
                        }

                        _subscriptions.Clear();
                    },
                    5000,
                    out _);
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
}
#endif

