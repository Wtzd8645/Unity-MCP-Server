using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal sealed class UnityMcpServerWindow : EditorWindow
    {
        private static UnityMcpServerWindow window;
        private UnityMcpHostSettings settings;

        [MenuItem("Tools/Unity MCP Bridge")]
        public static void ShowWindow()
        {
            window = GetWindow<UnityMcpServerWindow>("Unity MCP Bridge");
            window.minSize = new Vector2(560f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            window = this;
            settings = UnityMcpHostSettings.Instance;
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
                settings = UnityMcpHostSettings.Instance;
                settings.EnsureDefaults();
            }

            EditorGUILayout.LabelField("Unity MCP Bridge", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
            DrawStatusSection();
            EditorGUILayout.Space(8f);
            DrawSettingsSection();
        }

        private static void DrawStatusSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Status", EditorStyles.boldLabel, GUILayout.Width(60f));
                EditorGUILayout.LabelField(UnityMcpBridgeServer.IsRunning ? "Running" : "Stopped", EditorStyles.boldLabel);
            }

            bool isRunning = UnityMcpBridgeServer.IsRunning;
            bool toggled = GUILayout.Toggle(isRunning, isRunning ? "Stop Server" : "Start Server", "Button", GUILayout.Height(28f));
            if (toggled != isRunning)
            {
                if (toggled)
                {
                    UnityMcpBridgeServer.Start();
                }
                else
                {
                    UnityMcpBridgeServer.Stop();
                }
            }
        }

        private void DrawSettingsSection()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUI.BeginChangeCheck();

                BridgeTransportKind bridgeTransport = settings.BridgeTransport;
                bridgeTransport = (BridgeTransportKind)EditorGUILayout.EnumPopup("Bridge Transport", bridgeTransport);

                string bridgeHttpUrl = EditorGUILayout.TextField("Bridge HTTP URL", settings.BridgeHttpUrl);
                string bridgePipeName = EditorGUILayout.TextField("Bridge Pipe Name", settings.BridgePipeName);
                int bridgeTimeoutMs = EditorGUILayout.IntField("Bridge Timeout (ms)", settings.BridgeTimeoutMs);
                string allowedPathPrefixes = EditorGUILayout.TextField("Allowed Path Prefixes", settings.AllowedPathPrefixes);
                string allowedComponentTypes = EditorGUILayout.TextField("Allowed Component Types", settings.AllowedComponentTypes);
                bool autoStartHostOnLoad = EditorGUILayout.Toggle("Auto Start Bridge On Load", settings.AutoStartHostOnLoad);

                if (EditorGUI.EndChangeCheck())
                {
                    settings.BridgeTransport = bridgeTransport;
                    settings.BridgeHttpUrl = bridgeHttpUrl;
                    settings.BridgePipeName = bridgePipeName;
                    settings.BridgeTimeoutMs = bridgeTimeoutMs;
                    settings.AllowedPathPrefixes = allowedPathPrefixes;
                    settings.AllowedComponentTypes = allowedComponentTypes;
                    settings.AutoStartHostOnLoad = autoStartHostOnLoad;
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
