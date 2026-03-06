using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal sealed class UnityMcpServerWindow : EditorWindow
    {
        private static UnityMcpServerWindow window;

        private Vector2 settingsScroll;
        private Vector2 logsScroll;

        [MenuItem("Tools/Unity MCP Server")]
        public static void ShowWindow()
        {
            window = GetWindow<UnityMcpServerWindow>("Unity MCP Server");
            window.minSize = new Vector2(560f, 560f);
            window.Show();
        }

        internal static void RepaintIfOpen()
        {
            if (window != null)
            {
                EditorApplication.delayCall += () => window.Repaint();
            }
        }

        private void OnEnable()
        {
            window = this;
            UnityMcpHostSupervisor.StateChanged -= RepaintIfOpen;
            UnityMcpHostSupervisor.StateChanged += RepaintIfOpen;
        }

        private void OnDisable()
        {
            UnityMcpHostSupervisor.StateChanged -= RepaintIfOpen;
            if (ReferenceEquals(window, this))
            {
                window = null;
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            UnityMcpHostSettings settings = UnityMcpHostSettings.GetOrCreate();

            DrawStatusSection();
            EditorGUILayout.Space(8f);
            DrawActionSection();
            EditorGUILayout.Space(8f);
            DrawSettingsSection(settings);
            EditorGUILayout.Space(8f);
            DrawLogsSection();
        }

        private static void DrawStatusSection()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            DrawStatusLine("Bridge", UnityMcpBridgeServer.IsRunning ? "Running" : "Stopped");
            DrawStatusLine("Host", UnityMcpHostSupervisor.IsHostRunning ? "Running" : "Stopped");

            string status = UnityMcpHostSupervisor.LastStatus;
            MessageType type = ResolveStatusMessageType(status);
            EditorGUILayout.HelpBox(status, type);
        }

        private static void DrawStatusLine(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(90f));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            }
        }

        private static MessageType ResolveStatusMessageType(string status)
        {
            if (string.IsNullOrEmpty(status))
            {
                return MessageType.None;
            }

            if (status.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                status.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MessageType.Error;
            }

            if (status.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MessageType.Warning;
            }

            return MessageType.Info;
        }

        private static void DrawActionSection()
        {
            bool isRunning = UnityMcpBridgeServer.IsRunning || UnityMcpHostSupervisor.IsHostRunning;
            bool toggled = GUILayout.Toggle(isRunning, isRunning ? "Stop Server" : "Start Server", "Button", GUILayout.Height(28f));
            if (toggled != isRunning)
            {
                if (toggled)
                {
                    UnityMcpServerCoordinator.StartServer();
                }
                else
                {
                    UnityMcpServerCoordinator.StopServer("manual stop");
                }
            }
        }

        private void DrawSettingsSection(UnityMcpHostSettings settings)
        {
            EditorGUILayout.LabelField("Host Settings", EditorStyles.boldLabel);

            settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll, GUILayout.MinHeight(300f));
            EditorGUI.BeginChangeCheck();

            string repositoryRootOverride = EditorGUILayout.TextField("Package Root Override", settings.RepositoryRootOverride);
            string dotnetExecutable = EditorGUILayout.TextField("Dotnet Executable", settings.DotnetExecutable);
            string hostProjectPath = EditorGUILayout.TextField("Host Project Path", settings.HostProjectPath);
            string enabledModules = EditorGUILayout.TextField("Enabled Modules", settings.EnabledModules);

            string transport = settings.BridgeTransport;
            int transportIndex = transport == "pipe" ? 1 : 0;
            transportIndex = EditorGUILayout.Popup("Bridge Transport", transportIndex, new[] { "http", "pipe" });
            string bridgeTransport = transportIndex == 1 ? "pipe" : "http";

            string bridgeHttpUrl = EditorGUILayout.TextField("Bridge HTTP URL", settings.BridgeHttpUrl);
            string bridgePipeName = EditorGUILayout.TextField("Bridge Pipe Name", settings.BridgePipeName);
            int bridgeTimeoutMs = EditorGUILayout.IntField("Bridge Timeout (ms)", settings.BridgeTimeoutMs);
            int startupProbeTimeoutMs = EditorGUILayout.IntField("Startup Probe Timeout (ms)", settings.StartupProbeTimeoutMs);
            string allowedPathPrefixes = EditorGUILayout.TextField("Allowed Path Prefixes", settings.AllowedPathPrefixes);
            string allowedComponentTypes = EditorGUILayout.TextField("Allowed Component Types", settings.AllowedComponentTypes);
            bool autoStartHostOnLoad = EditorGUILayout.Toggle("Auto Start Full Server On Load", settings.AutoStartHostOnLoad);

            if (EditorGUI.EndChangeCheck())
            {
                settings.RepositoryRootOverride = repositoryRootOverride;
                settings.DotnetExecutable = dotnetExecutable;
                settings.HostProjectPath = hostProjectPath;
                settings.EnabledModules = enabledModules;
                settings.BridgeTransport = bridgeTransport;
                settings.BridgeHttpUrl = bridgeHttpUrl;
                settings.BridgePipeName = bridgePipeName;
                settings.BridgeTimeoutMs = bridgeTimeoutMs;
                settings.StartupProbeTimeoutMs = startupProbeTimeoutMs;
                settings.AllowedPathPrefixes = allowedPathPrefixes;
                settings.AllowedComponentTypes = allowedComponentTypes;
                settings.AutoStartHostOnLoad = autoStartHostOnLoad;
            }

            EditorGUILayout.Space(4f);
            string resolvedRepoRoot = settings.ResolveRepositoryRoot();
            string resolvedHostProject = settings.ResolveHostProjectPath();
            EditorGUILayout.LabelField("Resolved Package Root", resolvedRepoRoot, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Resolved Host Project", resolvedHostProject, EditorStyles.miniLabel);

            if (!File.Exists(resolvedHostProject))
            {
                EditorGUILayout.HelpBox(
                    "Host project path does not exist. Update Host Project Path or Package Root Override.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save Settings"))
                {
                    settings.SaveSettingsToDisk();
                    AssetDatabase.SaveAssets();
                }

                if (GUILayout.Button("Reload Defaults (Non-Destructive)"))
                {
                    UnityMcpHostSettings.GetOrCreate();
                }
            }
        }

        private void DrawLogsSection()
        {
            EditorGUILayout.LabelField("Host Logs", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear Logs", GUILayout.Width(100f)))
                {
                    UnityMcpHostSupervisor.ClearLogs();
                }

                EditorGUILayout.LabelField(
                    "Recent stderr and supervisor logs.",
                    EditorStyles.miniLabel,
                    GUILayout.ExpandWidth(true));
            }

            string[] logs = UnityMcpHostSupervisor.GetRecentLogs();
            logsScroll = EditorGUILayout.BeginScrollView(logsScroll, GUILayout.MinHeight(170f));
            if (logs.Length == 0)
            {
                EditorGUILayout.LabelField("No logs yet.", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < logs.Length; i++)
                {
                    EditorGUILayout.LabelField(logs[i], EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
