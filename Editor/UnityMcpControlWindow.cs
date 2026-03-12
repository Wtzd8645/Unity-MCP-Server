using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Control.Editor
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
        }

        private void OnDisable()
        {
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
            if (settings == null)
            {
                settings = UnityMcpGatewaySettings.Instance;
                settings.EnsureDefaults();
            }

            EditorGUILayout.LabelField("Unity MCP Control", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
            DrawStatusSection();
            EditorGUILayout.Space(8f);
            DrawSettingsSection();
        }

        private static void DrawStatusSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Status", EditorStyles.boldLabel, GUILayout.Width(60f));
                EditorGUILayout.LabelField(UnityMcpControlServer.IsRunning ? "Running" : "Stopped", EditorStyles.boldLabel);
            }

            bool isRunning = UnityMcpControlServer.IsRunning;
            bool toggled = GUILayout.Toggle(isRunning, isRunning ? "Stop Server" : "Start Server", "Button", GUILayout.Height(28f));
            if (toggled != isRunning)
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
        }

        private void DrawSettingsSection()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUI.BeginChangeCheck();

                ControlTransportKind controlTransport = settings.ControlTransport;
                controlTransport = (ControlTransportKind)EditorGUILayout.EnumPopup("Control Transport", controlTransport);

                string controlHttpUrl = EditorGUILayout.TextField("Control HTTP URL", settings.ControlHttpUrl);
                string controlPipeName = EditorGUILayout.TextField("Control Pipe Name", settings.ControlPipeName);
                int controlTimeoutMs = EditorGUILayout.IntField("Control Timeout (ms)", settings.ControlTimeoutMs);
                string allowedPathPrefixes = EditorGUILayout.TextField("Allowed Path Prefixes", settings.AllowedPathPrefixes);
                string allowedComponentTypes = EditorGUILayout.TextField("Allowed Component Types", settings.AllowedComponentTypes);
                bool autoStartGatewayOnLoad = EditorGUILayout.Toggle("Auto Start Control On Load", settings.AutoStartGatewayOnLoad);

                if (EditorGUI.EndChangeCheck())
                {
                    settings.ControlTransport = controlTransport;
                    settings.ControlHttpUrl = controlHttpUrl;
                    settings.ControlPipeName = controlPipeName;
                    settings.ControlTimeoutMs = controlTimeoutMs;
                    settings.AllowedPathPrefixes = allowedPathPrefixes;
                    settings.AllowedComponentTypes = allowedComponentTypes;
                    settings.AutoStartGatewayOnLoad = autoStartGatewayOnLoad;
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
