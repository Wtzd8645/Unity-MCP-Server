using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Blanketmen.UnityMcp.Control.Editor
{
    [InitializeOnLoad]
    internal static class UnityMcpGatewayHost
    {
        private const string SessionManagedPidKey = "Blanketmen.UnityMcp.Gateway.ManagedPid";
        private const string SessionManagedStartTicksKey = "Blanketmen.UnityMcp.Gateway.ManagedStartTicksUtc";
        private const int StopWaitTimeoutMs = 3000;

        private static Process managedProcess;
        private static int managedPid = -1;
        private static long managedStartTicks;
        private static bool stopRequested;

        public static GatewayProcessState State { get; private set; } = GatewayProcessState.Stopped;
        public static int? ManagedPid => managedPid > 0 ? managedPid : (int?)null;
        public static int? LastExitCode { get; private set; }
        public static string LastError { get; private set; } = string.Empty;

        public static bool IsRunning
        {
            get { return State == GatewayProcessState.Starting || State == GatewayProcessState.Running; }
        }

        static UnityMcpGatewayHost()
        {
            EditorApplication.update += MonitorManagedProcess;
            TryReattachManagedProcess();
        }

        public static bool Start(UnityMcpGatewaySettings settings, out string error)
        {
            error = string.Empty;
            if (settings == null)
            {
                error = "Gateway settings are not available.";
                LastError = error;
                State = GatewayProcessState.Error;
                return false;
            }

            if (TryEnsureManagedProcess())
            {
                State = GatewayProcessState.Running;
                LastError = string.Empty;
                return true;
            }

            string gatewayProjectPath = settings.ResolveGatewayProjectPath();
            if (!File.Exists(gatewayProjectPath))
            {
                error = "Gateway project not found: " + gatewayProjectPath;
                LastError = error;
                State = GatewayProcessState.Error;
                return false;
            }

            string gatewayProjectDirectory = Path.GetDirectoryName(gatewayProjectPath);
            if (string.IsNullOrWhiteSpace(gatewayProjectDirectory))
            {
                error = "Failed to resolve Gateway project directory.";
                LastError = error;
                State = GatewayProcessState.Error;
                return false;
            }

            ProcessStartInfo startInfo = BuildStartInfo(settings, gatewayProjectPath, gatewayProjectDirectory);

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            try
            {
                if (!process.Start())
                {
                    process.Dispose();
                    error = "dotnet process start returned false.";
                    LastError = error;
                    State = GatewayProcessState.Error;
                    return false;
                }
            }
            catch (Exception ex)
            {
                process.Dispose();
                error = "Failed to start Gateway process: " + ex.Message;
                LastError = error;
                State = GatewayProcessState.Error;
                return false;
            }

            stopRequested = false;
            LastExitCode = null;
            LastError = string.Empty;
            State = GatewayProcessState.Starting;

            AttachManagedProcess(process);
            BeginLogForwarding(process);
            MonitorManagedProcess();

            if (!IsRunning)
            {
                error = string.IsNullOrWhiteSpace(LastError)
                    ? "Gateway process exited immediately."
                    : LastError;
                return false;
            }

            return true;
        }

        public static bool Restart(UnityMcpGatewaySettings settings, out string error)
        {
            Stop();
            return Start(settings, out error);
        }

        public static void Stop()
        {
            stopRequested = true;
            LastError = string.Empty;
            TryEnsureManagedProcess();

            if (managedProcess == null)
            {
                ClearManagedIdentity();
                State = GatewayProcessState.Stopped;
                stopRequested = false;
                return;
            }

            bool stopFailed = false;

            try
            {
                if (!managedProcess.HasExited)
                {
                    managedProcess.Kill();
                    managedProcess.WaitForExit(StopWaitTimeoutMs);
                }
            }
            catch (Exception ex)
            {
                stopFailed = true;
                LastError = "Failed to stop Gateway process: " + ex.Message;
                State = GatewayProcessState.Error;
                Debug.LogWarning("[UnityMcpGateway] " + LastError);
            }

            CaptureExitCode(managedProcess);
            DetachManagedProcessHandle();
            ClearManagedIdentity();
            stopRequested = false;

            if (!stopFailed)
            {
                State = GatewayProcessState.Stopped;
                LastError = string.Empty;
            }
        }

        public static void PrepareForAssemblyReload()
        {
            if (!TryEnsureManagedProcess())
            {
                return;
            }

            PersistManagedIdentity();
            DetachManagedProcessHandle();
            State = GatewayProcessState.Running;
        }

        private static ProcessStartInfo BuildStartInfo(
            UnityMcpGatewaySettings settings,
            string gatewayProjectPath,
            string gatewayProjectDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = settings.DotnetExecutable,
                Arguments = BuildDotnetRunArguments(gatewayProjectPath),
                WorkingDirectory = gatewayProjectDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            startInfo.EnvironmentVariables["UNITY_MCP_ROOT"] = gatewayProjectDirectory;
            startInfo.EnvironmentVariables["UNITY_MCP_CONTROL_TRANSPORT"] =
                settings.ControlTransport == ControlTransportKind.Pipe ? "pipe" : "http";
            startInfo.EnvironmentVariables["UNITY_MCP_CONTROL_HTTP_URL"] = settings.ControlHttpUrl;
            startInfo.EnvironmentVariables["UNITY_MCP_CONTROL_PIPE_NAME"] = settings.ControlPipeName;
            startInfo.EnvironmentVariables["UNITY_MCP_CONTROL_TIMEOUT_MS"] =
                settings.ControlTimeoutMs.ToString(CultureInfo.InvariantCulture);

            string enabledModules = settings.EnabledModules;
            if (string.IsNullOrWhiteSpace(enabledModules))
            {
                startInfo.EnvironmentVariables.Remove("UNITY_MCP_ENABLED_MODULES");
            }
            else
            {
                startInfo.EnvironmentVariables["UNITY_MCP_ENABLED_MODULES"] = enabledModules;
            }

            return startInfo;
        }

        private static string BuildDotnetRunArguments(string gatewayProjectPath)
        {
            return "run --project " + QuoteArgument(gatewayProjectPath) + " --no-launch-profile";
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void BeginLogForwarding(Process process)
        {
            try
            {
                process.OutputDataReceived += OnGatewayOutputReceived;
                process.ErrorDataReceived += OnGatewayErrorReceived;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityMcpGateway] Failed to attach output streams: " + ex.Message);
            }
        }

        private static void OnGatewayOutputReceived(object sender, System.Diagnostics.DataReceivedEventArgs eventArgs)
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            Debug.Log("[UnityMcpGateway] " + eventArgs.Data);
        }

        private static void OnGatewayErrorReceived(object sender, System.Diagnostics.DataReceivedEventArgs eventArgs)
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            Debug.LogWarning("[UnityMcpGateway] " + eventArgs.Data);
        }

        private static bool TryEnsureManagedProcess()
        {
            if (managedProcess != null)
            {
                try
                {
                    if (!managedProcess.HasExited)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Best effort only.
                }

                CaptureExitCode(managedProcess);
                DetachManagedProcessHandle();
                ClearManagedIdentity();
                State = LastExitCode.HasValue && LastExitCode.Value == 0
                    ? GatewayProcessState.Exited
                    : GatewayProcessState.Error;
                if (LastExitCode.HasValue && LastExitCode.Value == 0)
                {
                    LastError = string.Empty;
                }
                else
                {
                    LastError = LastExitCode.HasValue
                        ? $"Gateway process exited with code {LastExitCode.Value}."
                        : "Gateway process exited unexpectedly.";
                }

                return false;
            }

            return TryReattachManagedProcess();
        }

        private static void MonitorManagedProcess()
        {
            if (managedProcess == null)
            {
                return;
            }

            bool hasExited;
            try
            {
                hasExited = managedProcess.HasExited;
            }
            catch (Exception ex)
            {
                LastError = "Failed to monitor Gateway process: " + ex.Message;
                State = GatewayProcessState.Error;
                return;
            }

            if (!hasExited)
            {
                if (State != GatewayProcessState.Running)
                {
                    State = GatewayProcessState.Running;
                }

                return;
            }

            CaptureExitCode(managedProcess);
            DetachManagedProcessHandle();
            ClearManagedIdentity();

            if (stopRequested)
            {
                State = GatewayProcessState.Stopped;
                LastError = string.Empty;
            }
            else
            {
                State = LastExitCode.HasValue && LastExitCode.Value == 0
                    ? GatewayProcessState.Exited
                    : GatewayProcessState.Error;
                LastError = LastExitCode.HasValue
                    ? $"Gateway process exited with code {LastExitCode.Value}."
                    : "Gateway process exited unexpectedly.";
                Debug.LogWarning("[UnityMcpGateway] " + LastError);
            }

            stopRequested = false;
        }

        private static bool TryReattachManagedProcess()
        {
            int sessionPid = SessionState.GetInt(SessionManagedPidKey, -1);
            if (sessionPid <= 0)
            {
                return false;
            }

            long expectedStartTicks = ParseTicks(SessionState.GetString(SessionManagedStartTicksKey, string.Empty));
            Process process;
            try
            {
                process = Process.GetProcessById(sessionPid);
            }
            catch
            {
                ClearManagedIdentity();
                return false;
            }

            try
            {
                if (process.HasExited)
                {
                    CaptureExitCode(process);
                    process.Dispose();
                    ClearManagedIdentity();
                    State = LastExitCode.HasValue && LastExitCode.Value == 0
                        ? GatewayProcessState.Exited
                        : GatewayProcessState.Error;
                    if (LastExitCode.HasValue && LastExitCode.Value == 0)
                    {
                        LastError = string.Empty;
                    }
                    else
                    {
                        LastError = LastExitCode.HasValue
                            ? $"Gateway process exited with code {LastExitCode.Value}."
                            : "Gateway process exited unexpectedly.";
                    }
                    return false;
                }

                long actualStartTicks = TryGetStartTicks(process);
                if (expectedStartTicks > 0 && actualStartTicks > 0 && expectedStartTicks != actualStartTicks)
                {
                    process.Dispose();
                    ClearManagedIdentity();
                    return false;
                }

                managedProcess = process;
                managedPid = process.Id;
                managedStartTicks = actualStartTicks > 0 ? actualStartTicks : expectedStartTicks;
                stopRequested = false;
                LastError = string.Empty;
                State = GatewayProcessState.Running;
                PersistManagedIdentity();
                return true;
            }
            catch
            {
                process.Dispose();
                ClearManagedIdentity();
                return false;
            }
        }

        private static long ParseTicks(string raw)
        {
            long ticks;
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
            {
                return 0;
            }

            return ticks;
        }

        private static long TryGetStartTicks(Process process)
        {
            try
            {
                return process.StartTime.ToUniversalTime().Ticks;
            }
            catch
            {
                return 0;
            }
        }

        private static void AttachManagedProcess(Process process)
        {
            DetachManagedProcessHandle();
            managedProcess = process;
            managedPid = process.Id;
            managedStartTicks = TryGetStartTicks(process);
            PersistManagedIdentity();
        }

        private static void PersistManagedIdentity()
        {
            if (managedPid <= 0)
            {
                return;
            }

            SessionState.SetInt(SessionManagedPidKey, managedPid);
            SessionState.SetString(
                SessionManagedStartTicksKey,
                managedStartTicks > 0 ? managedStartTicks.ToString(CultureInfo.InvariantCulture) : string.Empty);
        }

        private static void ClearManagedIdentity()
        {
            managedPid = -1;
            managedStartTicks = 0;
            SessionState.SetInt(SessionManagedPidKey, -1);
            SessionState.SetString(SessionManagedStartTicksKey, string.Empty);
        }

        private static void CaptureExitCode(Process process)
        {
            try
            {
                if (process != null && process.HasExited)
                {
                    LastExitCode = process.ExitCode;
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        private static void DetachManagedProcessHandle()
        {
            if (managedProcess == null)
            {
                return;
            }

            try
            {
                managedProcess.OutputDataReceived -= OnGatewayOutputReceived;
                managedProcess.ErrorDataReceived -= OnGatewayErrorReceived;
            }
            catch
            {
                // Best effort only.
            }

            try
            {
                managedProcess.CancelOutputRead();
            }
            catch
            {
                // Best effort only.
            }

            try
            {
                managedProcess.CancelErrorRead();
            }
            catch
            {
                // Best effort only.
            }

            managedProcess.Dispose();
            managedProcess = null;
        }
    }
}
