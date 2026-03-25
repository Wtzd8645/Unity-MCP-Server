using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Editor.Control
{
    internal sealed class UnityMcpControlWindow : EditorWindow
    {
        private static UnityMcpControlWindow window;
        private UnityMcpGatewaySettings settings;

        [MenuItem("Tools/Unity MCP Control")]
        public static void ShowWindow()
        {
            window = GetWindow<UnityMcpControlWindow>("Unity MCP Control");
            window.minSize = new Vector2(560f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            window = this;
            settings = UnityMcpGatewaySettings.Instance;
            settings.EnsureDefaults();
            UnityMcpControlServer.StatusChanged += RepaintFromStatusChange;
        }

        private void OnDisable()
        {
            UnityMcpControlServer.StatusChanged -= RepaintFromStatusChange;
            if (ReferenceEquals(window, this))
            {
                window = null;
            }
        }

        private void RepaintFromStatusChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (settings == null)
            {
                settings = UnityMcpGatewaySettings.Instance;
                settings.EnsureDefaults();
            }

            EditorGUILayout.LabelField("Unity MCP Control + Gateway", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
            DrawStatusSection();
            EditorGUILayout.Space(8f);
            DrawSettingsSection();
        }

        private void DrawStatusSection()
        {
            bool controlRunning = UnityMcpControlServer.IsRunning;
            GatewayStatusSnapshot gatewaySnapshot = UnityMcpGatewayHost.GetStatusSnapshot();
            bool gatewayRunning = gatewaySnapshot.State == GatewayProcessState.Starting ||
                                 gatewaySnapshot.State == GatewayProcessState.Running ||
                                 gatewaySnapshot.ManagedPid.HasValue;
            bool stackRunning = controlRunning || gatewayRunning;
            string gatewayStatus = gatewaySnapshot.State.ToString();
            if (gatewaySnapshot.ManagedPid.HasValue)
            {
                gatewayStatus += $" (PID {gatewaySnapshot.ManagedPid.Value})";
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Control", EditorStyles.boldLabel, GUILayout.Width(60f));
                EditorGUILayout.LabelField(controlRunning ? "Running" : "Stopped", EditorStyles.boldLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Gateway", EditorStyles.boldLabel, GUILayout.Width(60f));
                EditorGUILayout.LabelField(gatewayStatus, EditorStyles.boldLabel);
            }

            if (gatewaySnapshot.LastExitCode.HasValue)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Exit", EditorStyles.boldLabel, GUILayout.Width(60f));
                    EditorGUILayout.LabelField(gatewaySnapshot.LastExitCode.Value.ToString(), EditorStyles.label);
                }
            }

            if (!string.IsNullOrWhiteSpace(gatewaySnapshot.LastError))
            {
                EditorGUILayout.HelpBox(gatewaySnapshot.LastError, MessageType.Warning);
            }

            bool toggled = GUILayout.Toggle(
                stackRunning,
                stackRunning ? "Stop Control + Gateway" : "Start Control + Gateway",
                "Button",
                GUILayout.Height(28f));

            if (toggled != stackRunning)
            {
                if (toggled)
                {
                    UnityMcpControlServer.Start();
                }
                else
                {
                    UnityMcpControlServer.Stop();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool wasEnabled = GUI.enabled;
                GUI.enabled = controlRunning;
                if (GUILayout.Button("Restart Gateway"))
                {
                    UnityMcpControlServer.RestartGateway();
                }

                GUI.enabled = wasEnabled;
            }
        }

        private void DrawSettingsSection()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUI.BeginChangeCheck();

                string dotnetExecutable = EditorGUILayout.TextField("Dotnet Executable", settings.DotnetExecutable);
                string gatewayProjectPath = EditorGUILayout.TextField("Gateway Project Path", settings.GatewayProjectPath);
                string enabledModules = EditorGUILayout.TextField("Enabled Modules", settings.EnabledModules);
                ControlTransportKind controlTransport = settings.ControlTransport;
                controlTransport = (ControlTransportKind)EditorGUILayout.EnumPopup("Control Transport", controlTransport);
                string controlHttpUrl = EditorGUILayout.TextField("Control HTTP URL", settings.ControlHttpUrl);
                string controlPipeName = EditorGUILayout.TextField("Control Pipe Name", settings.ControlPipeName);
                int controlTimeoutMs = EditorGUILayout.IntField("Control Timeout (ms)", settings.ControlTimeoutMs);
                int startupProbeTimeoutMs = EditorGUILayout.IntField("Gateway Startup Probe Timeout (ms)", settings.StartupProbeTimeoutMs);
                string allowedPathPrefixes = EditorGUILayout.TextField("Allowed Path Prefixes", settings.AllowedPathPrefixes);
                string allowedComponentTypes = EditorGUILayout.TextField("Allowed Component Types", settings.AllowedComponentTypes);
                bool autoStartControlOnLoad = EditorGUILayout.Toggle("Auto Start Control On Load", settings.AutoStartControlOnLoad);
                bool autoStartGatewayWithControl = EditorGUILayout.Toggle("Auto Start Gateway With Control", settings.AutoStartGatewayWithControl);

                if (EditorGUI.EndChangeCheck())
                {
                    settings.DotnetExecutable = dotnetExecutable;
                    settings.GatewayProjectPath = gatewayProjectPath;
                    settings.EnabledModules = enabledModules;
                    settings.ControlTransport = controlTransport;
                    settings.ControlHttpUrl = controlHttpUrl;
                    settings.ControlPipeName = controlPipeName;
                    settings.ControlTimeoutMs = controlTimeoutMs;
                    settings.StartupProbeTimeoutMs = startupProbeTimeoutMs;
                    settings.AllowedPathPrefixes = allowedPathPrefixes;
                    settings.AllowedComponentTypes = allowedComponentTypes;
                    settings.AutoStartControlOnLoad = autoStartControlOnLoad;
                    settings.AutoStartGatewayWithControl = autoStartGatewayWithControl;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save Settings"))
                {
                    settings.SaveToDisk();
                    AssetDatabase.SaveAssets();
                }
            }
        }
    }
}
