using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    [InitializeOnLoad]
    internal static class UnityMcpHostSupervisor
    {
        private const int MaxLogLines = 200;
        private const int HeaderReadLimitBytes = 16384;

        private static readonly object SyncRoot = new object();
        private static readonly Queue<string> RecentLogs = new Queue<string>();

        private static Process hostProcess;
        private static string lastStatus = "Host not started.";
        private static int nextRequestId;
        private static bool autoStartChecked;

        static UnityMcpHostSupervisor()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.update += TryAutoStartHost;
        }

        public static bool IsHostRunning
        {
            get
            {
                lock (SyncRoot)
                {
                    return hostProcess != null && !hostProcess.HasExited;
                }
            }
        }

        public static string LastStatus
        {
            get
            {
                lock (SyncRoot)
                {
                    return lastStatus;
                }
            }
        }

        [MenuItem("Tools/Unity MCP Bridge/Start Full Server")]
        public static void StartFullServerFromMenu()
        {
            StartFullServer();
        }

        [MenuItem("Tools/Unity MCP Bridge/Stop Full Server")]
        public static void StopFullServerFromMenu()
        {
            StopFullServer();
        }

        [MenuItem("Tools/Unity MCP Bridge/Start Full Server", true)]
        private static bool CanStartFullServer()
        {
            return !UnityMcpBridgeServer.IsRunning || !IsHostRunning;
        }

        [MenuItem("Tools/Unity MCP Bridge/Stop Full Server", true)]
        private static bool CanStopFullServer()
        {
            return UnityMcpBridgeServer.IsRunning || IsHostRunning;
        }

        public static bool StartFullServer()
        {
            UnityMcpHostSettings settings = UnityMcpHostSettings.GetOrCreate();
            ApplyCurrentProcessEnvironment(settings);

            if (settings.AutoStartBridgeWithHost && !UnityMcpBridgeServer.IsRunning)
            {
                UnityMcpBridgeServer.Start();
            }

            bool started = StartHostInternal(settings, runStartupProbe: true);
            if (started)
            {
                SetStatus("Full server running.");
            }

            return started;
        }

        public static bool StartHostOnly()
        {
            UnityMcpHostSettings settings = UnityMcpHostSettings.GetOrCreate();
            ApplyCurrentProcessEnvironment(settings);
            return StartHostInternal(settings, runStartupProbe: true);
        }

        public static void StartBridgeOnly()
        {
            UnityMcpHostSettings settings = UnityMcpHostSettings.GetOrCreate();
            ApplyCurrentProcessEnvironment(settings);
            if (!UnityMcpBridgeServer.IsRunning)
            {
                UnityMcpBridgeServer.Start();
            }

            SetStatus("Bridge running.");
        }

        public static void StopBridgeOnly()
        {
            if (UnityMcpBridgeServer.IsRunning)
            {
                UnityMcpBridgeServer.Stop();
            }

            SetStatus("Bridge stopped.");
        }

        public static void StopFullServer()
        {
            StopHost();
            if (UnityMcpBridgeServer.IsRunning)
            {
                UnityMcpBridgeServer.Stop();
            }

            SetStatus("Full server stopped.");
        }

        public static void StopHost()
        {
            StopHostProcess("manual stop");
        }

        public static string[] GetRecentLogs()
        {
            lock (SyncRoot)
            {
                return RecentLogs.ToArray();
            }
        }

        public static void ClearLogs()
        {
            lock (SyncRoot)
            {
                RecentLogs.Clear();
            }

            UnityMcpServerWindow.RepaintIfOpen();
        }

        private static bool StartHostInternal(UnityMcpHostSettings settings, bool runStartupProbe)
        {
            lock (SyncRoot)
            {
                if (hostProcess != null && !hostProcess.HasExited)
                {
                    SetStatusLocked("Host already running.");
                    return true;
                }
            }

            string hostProjectPath = settings.ResolveHostProjectPath();
            if (!File.Exists(hostProjectPath))
            {
                string message = "Host project not found: " + hostProjectPath;
                SetStatus(message);
                AppendLog(message);
                Debug.LogError("[Unity MCP Host] " + message);
                return false;
            }

            string workingDirectory = Path.GetDirectoryName(hostProjectPath);
            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                string message = "Invalid host working directory: " + workingDirectory;
                SetStatus(message);
                AppendLog(message);
                Debug.LogError("[Unity MCP Host] " + message);
                return false;
            }

            ProcessStartInfo startInfo = BuildStartInfo(settings, hostProjectPath, workingDirectory, workingDirectory);
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            process.Exited += OnHostExited;

            try
            {
                bool started = process.Start();
                if (!started)
                {
                    string message = "Host process failed to start.";
                    SetStatus(message);
                    AppendLog(message);
                    Debug.LogError("[Unity MCP Host] " + message);
                    process.Dispose();
                    return false;
                }
            }
            catch (Exception ex)
            {
                string message = "Host start threw exception: " + ex.Message;
                SetStatus(message);
                AppendLog(message);
                Debug.LogError("[Unity MCP Host] " + message);
                process.Dispose();
                return false;
            }

            lock (SyncRoot)
            {
                hostProcess = process;
                SetStatusLocked("Host process started. PID=" + process.Id.ToString(CultureInfo.InvariantCulture));
            }

            StartStderrPump(process);

            if (runStartupProbe)
            {
                string probeError;
                if (!TryRunStartupProbe(process, settings.StartupProbeTimeoutMs, out probeError))
                {
                    string message = "Host startup probe failed: " + probeError;
                    SetStatus(message);
                    AppendLog(message);
                    Debug.LogError("[Unity MCP Host] " + message);
                    StopHostProcess("startup probe failed");
                    return false;
                }
            }

            AppendLog("Host started: " + startInfo.FileName + " " + startInfo.Arguments);
            SetStatus("Host running. PID=" + process.Id.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        private static ProcessStartInfo BuildStartInfo(
            UnityMcpHostSettings settings,
            string hostProjectPath,
            string workingDirectory,
            string mcpRoot)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = settings.DotnetExecutable,
                Arguments = "run --project " + QuoteArgument(hostProjectPath),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            ApplyHostEnvironment(startInfo.EnvironmentVariables, settings, mcpRoot);
            return startInfo;
        }

        private static void ApplyCurrentProcessEnvironment(UnityMcpHostSettings settings)
        {
            string hostProjectPath = settings.ResolveHostProjectPath();
            string mcpRoot = Path.GetDirectoryName(hostProjectPath);
            if (string.IsNullOrEmpty(mcpRoot))
            {
                mcpRoot = settings.ResolveRepositoryRoot();
            }

            SetCurrentProcessEnvironment("UNITY_MCP_ROOT", mcpRoot);
            SetCurrentProcessEnvironment("UNITY_MCP_ENABLED_MODULES", settings.EnabledModules);
            SetCurrentProcessEnvironment("UNITY_MCP_BRIDGE_TRANSPORT", settings.BridgeTransport);
            SetCurrentProcessEnvironment("UNITY_MCP_BRIDGE_HTTP_URL", settings.BridgeHttpUrl);
            SetCurrentProcessEnvironment("UNITY_MCP_BRIDGE_PIPE_NAME", settings.BridgePipeName);
            SetCurrentProcessEnvironment(
                "UNITY_MCP_BRIDGE_TIMEOUT_MS",
                settings.BridgeTimeoutMs.ToString(CultureInfo.InvariantCulture));
            SetCurrentProcessEnvironment("UNITY_MCP_ALLOWED_PATH_PREFIXES", settings.AllowedPathPrefixes);
            SetCurrentProcessEnvironment("UNITY_MCP_ALLOWED_COMPONENT_TYPES", settings.AllowedComponentTypes);
        }

        private static void ApplyHostEnvironment(
            StringDictionary environmentVariables,
            UnityMcpHostSettings settings,
            string mcpRoot)
        {
            SetEnvironmentValue(environmentVariables, "UNITY_MCP_ROOT", mcpRoot);
            SetEnvironmentValue(environmentVariables, "UNITY_MCP_ENABLED_MODULES", settings.EnabledModules);
            SetEnvironmentValue(environmentVariables, "UNITY_MCP_BRIDGE_TRANSPORT", settings.BridgeTransport);
            SetEnvironmentValue(environmentVariables, "UNITY_MCP_BRIDGE_HTTP_URL", settings.BridgeHttpUrl);
            SetEnvironmentValue(environmentVariables, "UNITY_MCP_BRIDGE_PIPE_NAME", settings.BridgePipeName);
            SetEnvironmentValue(
                environmentVariables,
                "UNITY_MCP_BRIDGE_TIMEOUT_MS",
                settings.BridgeTimeoutMs.ToString(CultureInfo.InvariantCulture));
            SetEnvironmentValue(environmentVariables, "UNITY_MCP_ALLOWED_PATH_PREFIXES", settings.AllowedPathPrefixes);
            SetEnvironmentValue(environmentVariables, "UNITY_MCP_ALLOWED_COMPONENT_TYPES", settings.AllowedComponentTypes);
        }

        private static void SetEnvironmentValue(StringDictionary environmentVariables, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (environmentVariables[key] != null)
                {
                    environmentVariables.Remove(key);
                }

                return;
            }

            environmentVariables[key] = value;
        }

        private static void SetCurrentProcessEnvironment(string key, string value)
        {
            string envValue = string.IsNullOrWhiteSpace(value) ? null : value;
            Environment.SetEnvironmentVariable(key, envValue);
        }

        private static bool TryRunStartupProbe(Process process, int timeoutMs, out string error)
        {
            error = null;
            int perCallTimeoutMs = Mathf.Max(1000, timeoutMs / 2);

            Dictionary<string, object> initializeEnvelope;
            if (!TryInvokeRpc(process, "initialize", "{}", perCallTimeoutMs, out initializeEnvelope, out error))
            {
                return false;
            }

            Dictionary<string, object> toolsEnvelope;
            if (!TryInvokeRpc(process, "tools/list", "{}", perCallTimeoutMs, out toolsEnvelope, out error))
            {
                return false;
            }

            object resultObject;
            if (!toolsEnvelope.TryGetValue("result", out resultObject))
            {
                error = "tools/list response missing result.";
                return false;
            }

            var result = resultObject as Dictionary<string, object>;
            if (result == null)
            {
                error = "tools/list result has unexpected shape.";
                return false;
            }

            object toolsObject;
            if (!result.TryGetValue("tools", out toolsObject))
            {
                error = "tools/list result missing tools.";
                return false;
            }

            int toolCount = 0;
            var tools = toolsObject as List<object>;
            if (tools != null)
            {
                toolCount = tools.Count;
            }

            AppendLog("Startup probe OK. tools/list count=" + toolCount.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        private static bool TryInvokeRpc(
            Process process,
            string method,
            string paramsJson,
            int timeoutMs,
            out Dictionary<string, object> envelope,
            out string error)
        {
            envelope = null;
            error = null;
            if (process == null)
            {
                error = "Host process is null.";
                return false;
            }

            if (process.HasExited)
            {
                error = "Host process already exited.";
                return false;
            }

            int requestId = Interlocked.Increment(ref nextRequestId);
            string requestJson = BuildJsonRpcRequest(requestId, method, paramsJson);

            string writeError;
            if (!TryWriteRpcMessage(process.StandardInput.BaseStream, requestJson, timeoutMs, out writeError))
            {
                error = method + " write failed: " + writeError;
                return false;
            }

            string responseJson;
            if (!TryReadRpcMessage(process.StandardOutput.BaseStream, timeoutMs, out responseJson, out error))
            {
                error = method + " read failed: " + error;
                return false;
            }

            object parsed = BridgeMiniJson.Deserialize(responseJson);
            envelope = parsed as Dictionary<string, object>;
            if (envelope == null)
            {
                error = method + " response is not a JSON object.";
                return false;
            }

            int responseId;
            if (!TryReadRpcId(envelope, out responseId))
            {
                error = method + " response missing id.";
                return false;
            }

            if (responseId != requestId)
            {
                error = method + " response id mismatch. expected=" + requestId.ToString(CultureInfo.InvariantCulture) +
                    ", actual=" + responseId.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            object errorObject;
            if (envelope.TryGetValue("error", out errorObject) && errorObject != null)
            {
                error = method + " returned error: " + DescribeRpcError(errorObject);
                return false;
            }

            if (!envelope.ContainsKey("result"))
            {
                error = method + " response missing result.";
                return false;
            }

            return true;
        }

        private static bool TryWriteRpcMessage(Stream stream, string json, int timeoutMs, out string error)
        {
            error = null;
            if (stream == null)
            {
                error = "RPC stream is null.";
                return false;
            }

            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] header = Encoding.ASCII.GetBytes(
                "Content-Length: " + payload.Length.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n");

            Task task = Task.Run(() =>
            {
                stream.Write(header, 0, header.Length);
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            });

            if (!task.Wait(timeoutMs))
            {
                error = "RPC write timeout.";
                return false;
            }

            if (task.IsFaulted)
            {
                Exception baseException = task.Exception == null ? null : task.Exception.GetBaseException();
                error = baseException == null ? "RPC write failed." : baseException.Message;
                return false;
            }

            return true;
        }

        private static bool TryReadRpcMessage(Stream stream, int timeoutMs, out string json, out string error)
        {
            json = null;
            error = null;
            if (stream == null)
            {
                error = "RPC stream is null.";
                return false;
            }

            Task<string> task = Task.Run(() => ReadRpcMessageBlocking(stream));
            if (!task.Wait(timeoutMs))
            {
                error = "RPC read timeout.";
                return false;
            }

            try
            {
                json = task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                return false;
            }

            return true;
        }

        private static string ReadRpcMessageBlocking(Stream stream)
        {
            byte[] headerBytes = ReadHeaderBytes(stream);
            string header = Encoding.ASCII.GetString(headerBytes);
            int contentLength = ParseContentLength(header);
            if (contentLength < 0)
            {
                throw new InvalidDataException("Invalid Content-Length header.");
            }

            byte[] payload = new byte[contentLength];
            int offset = 0;
            while (offset < contentLength)
            {
                int read = stream.Read(payload, offset, contentLength - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Stream ended while reading RPC payload.");
                }

                offset += read;
            }

            return Encoding.UTF8.GetString(payload, 0, payload.Length);
        }

        private static byte[] ReadHeaderBytes(Stream stream)
        {
            var buffer = new List<byte>(128);
            int state = 0;

            while (buffer.Count < HeaderReadLimitBytes)
            {
                int value = stream.ReadByte();
                if (value < 0)
                {
                    throw new EndOfStreamException("Stream ended while reading RPC header.");
                }

                byte current = (byte)value;
                buffer.Add(current);

                if (state == 0)
                {
                    state = current == (byte)'\r' ? 1 : 0;
                }
                else if (state == 1)
                {
                    state = current == (byte)'\n' ? 2 : 0;
                }
                else if (state == 2)
                {
                    state = current == (byte)'\r' ? 3 : 0;
                }
                else if (state == 3)
                {
                    state = current == (byte)'\n' ? 4 : 0;
                }

                if (state == 4)
                {
                    return buffer.ToArray();
                }
            }

            throw new InvalidDataException("RPC header exceeds max size.");
        }

        private static int ParseContentLength(string header)
        {
            using (var reader = new StringReader(header))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                    {
                        break;
                    }

                    int separatorIndex = line.IndexOf(':');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim();
                    if (!key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string rawValue = line.Substring(separatorIndex + 1).Trim();
                    int parsed;
                    if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    {
                        return parsed;
                    }
                }
            }

            return -1;
        }

        private static string BuildJsonRpcRequest(int id, string method, string paramsJson)
        {
            return "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) +
                ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";
        }

        private static bool TryReadRpcId(Dictionary<string, object> envelope, out int id)
        {
            id = 0;
            if (envelope == null)
            {
                return false;
            }

            object idObject;
            if (!envelope.TryGetValue("id", out idObject) || idObject == null)
            {
                return false;
            }

            if (idObject is long)
            {
                id = (int)(long)idObject;
                return true;
            }

            if (idObject is double)
            {
                id = (int)(double)idObject;
                return true;
            }

            if (idObject is int)
            {
                id = (int)idObject;
                return true;
            }

            int parsed;
            if (int.TryParse(idObject.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                id = parsed;
                return true;
            }

            return false;
        }

        private static string DescribeRpcError(object errorObject)
        {
            if (errorObject == null)
            {
                return "unknown error";
            }

            var errorMap = errorObject as Dictionary<string, object>;
            if (errorMap == null)
            {
                return errorObject.ToString();
            }

            object codeObject;
            object messageObject;
            string code = errorMap.TryGetValue("code", out codeObject) && codeObject != null
                ? codeObject.ToString()
                : "unknown";
            string message = errorMap.TryGetValue("message", out messageObject) && messageObject != null
                ? messageObject.ToString()
                : "unknown message";

            return "code=" + code + ", message=" + message;
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void StartStderrPump(Process process)
        {
            var thread = new Thread(() => PumpStderr(process))
            {
                IsBackground = true,
                Name = "UnityMcpHost.Stderr",
            };

            thread.Start();
        }

        private static void PumpStderr(Process process)
        {
            try
            {
                while (process != null && !process.HasExited)
                {
                    string line = process.StandardError.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    AppendLog("stderr: " + line);
                }
            }
            catch (Exception ex)
            {
                AppendLog("stderr pump stopped: " + ex.Message);
            }
        }

        private static void StopHostProcess(string reason)
        {
            Process process = null;
            lock (SyncRoot)
            {
                process = hostProcess;
                hostProcess = null;
            }

            if (process == null)
            {
                SetStatus("Host not running.");
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.StandardInput.Close();
                    }
                    catch
                    {
                        // Ignore shutdown write failures.
                    }

                    if (!process.WaitForExit(1500))
                    {
                        process.Kill();
                        process.WaitForExit(1500);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Host stop failed: " + ex.Message);
            }
            finally
            {
                process.Dispose();
            }

            SetStatus("Host stopped (" + reason + ").");
        }

        private static void OnHostExited(object sender, EventArgs e)
        {
            var process = sender as Process;
            int exitCode = 0;
            if (process != null)
            {
                try
                {
                    exitCode = process.ExitCode;
                }
                catch
                {
                    exitCode = 0;
                }
            }

            lock (SyncRoot)
            {
                if (ReferenceEquals(hostProcess, process))
                {
                    hostProcess = null;
                    SetStatusLocked("Host exited. ExitCode=" + exitCode.ToString(CultureInfo.InvariantCulture));
                }
            }

            AppendLog("Host exited. ExitCode=" + exitCode.ToString(CultureInfo.InvariantCulture));

            if (process != null)
            {
                process.Dispose();
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            StopHostProcess("assembly reload");
        }

        private static void OnEditorQuitting()
        {
            StopHostProcess("editor quitting");
        }

        private static void TryAutoStartHost()
        {
            if (autoStartChecked)
            {
                EditorApplication.update -= TryAutoStartHost;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            autoStartChecked = true;
            EditorApplication.update -= TryAutoStartHost;

            UnityMcpHostSettings settings = UnityMcpHostSettings.GetOrCreate();
            if (settings.AutoStartHostOnLoad)
            {
                StartFullServer();
            }
        }

        private static void AppendLog(string line)
        {
            string timestamped = "[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] " + line;
            lock (SyncRoot)
            {
                if (RecentLogs.Count >= MaxLogLines)
                {
                    RecentLogs.Dequeue();
                }

                RecentLogs.Enqueue(timestamped);
            }

            UnityMcpServerWindow.RepaintIfOpen();
        }

        private static void SetStatus(string status)
        {
            lock (SyncRoot)
            {
                SetStatusLocked(status);
            }
        }

        private static void SetStatusLocked(string status)
        {
            lastStatus = status;
            UnityMcpServerWindow.RepaintIfOpen();
        }
    }
}
