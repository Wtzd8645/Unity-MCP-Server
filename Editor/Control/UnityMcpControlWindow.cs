using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Editor.Control
{
    internal sealed class UnityMcpControlWindow : EditorWindow
    {
        [Serializable]
        private sealed class ToolModuleManifest
        {
            public ToolModuleItem[] modules;
        }

        [Serializable]
        private sealed class ToolModuleItem
        {
            public string name;
            public bool enabledByDefault;
        }

        private sealed class SettingsDraft
        {
            public string dotnetExecutable;
            public string gatewayProjectPath;
            public string enabledModules;
            public ControlTransportKind controlTransport;
            public string controlHttpUrl;
            public string controlPipeName;
            public int controlTimeoutMs;
            public int startupProbeTimeoutMs;
            public string allowedPathPrefixes;
            public string allowedComponentTypes;
            public bool autoStartControlOnLoad;
            public bool autoStartGatewayWithControl;
        }

        private enum ModulePresetKind
        {
            Default = 0,
            SafeEdit = 1,
            FullAutomation = 2,
            Custom = 3,
        }

        private static UnityMcpControlWindow window;
        private UnityMcpGatewaySettings settings;
        private bool modulePresetInitialized;
        private ModulePresetKind modulePresetSelection;
        private string customEnabledModulesDraft = string.Empty;

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
            modulePresetInitialized = false;
            customEnabledModulesDraft = settings.EnabledModules;
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

                SettingsDraft draft = CreateSettingsDraft();
                DrawGatewaySection(draft);
                DrawModuleSection(draft);
                DrawTransportSection(draft);
                DrawSafetySection(draft);
                DrawAutomationSection(draft);

                if (EditorGUI.EndChangeCheck())
                {
                    settings.DotnetExecutable = draft.dotnetExecutable;
                    settings.GatewayProjectPath = draft.gatewayProjectPath;
                    settings.EnabledModules = draft.enabledModules;
                    settings.ControlTransport = draft.controlTransport;
                    settings.ControlHttpUrl = draft.controlHttpUrl;
                    settings.ControlPipeName = draft.controlPipeName;
                    settings.ControlTimeoutMs = draft.controlTimeoutMs;
                    settings.StartupProbeTimeoutMs = draft.startupProbeTimeoutMs;
                    settings.AllowedPathPrefixes = draft.allowedPathPrefixes;
                    settings.AllowedComponentTypes = draft.allowedComponentTypes;
                    settings.AutoStartControlOnLoad = draft.autoStartControlOnLoad;
                    settings.AutoStartGatewayWithControl = draft.autoStartGatewayWithControl;
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

        private SettingsDraft CreateSettingsDraft()
        {
            return new SettingsDraft
            {
                dotnetExecutable = settings.DotnetExecutable,
                gatewayProjectPath = settings.GatewayProjectPath,
                enabledModules = settings.EnabledModules,
                controlTransport = settings.ControlTransport,
                controlHttpUrl = settings.ControlHttpUrl,
                controlPipeName = settings.ControlPipeName,
                controlTimeoutMs = settings.ControlTimeoutMs,
                startupProbeTimeoutMs = settings.StartupProbeTimeoutMs,
                allowedPathPrefixes = settings.AllowedPathPrefixes,
                allowedComponentTypes = settings.AllowedComponentTypes,
                autoStartControlOnLoad = settings.AutoStartControlOnLoad,
                autoStartGatewayWithControl = settings.AutoStartGatewayWithControl,
            };
        }

        private void DrawGatewaySection(SettingsDraft draft)
        {
            DrawSettingsGroup("Gateway", () =>
            {
                draft.dotnetExecutable = EditorGUILayout.TextField("Dotnet Executable", draft.dotnetExecutable);
                draft.gatewayProjectPath = EditorGUILayout.TextField("Gateway Project Path", draft.gatewayProjectPath);
                draft.startupProbeTimeoutMs = EditorGUILayout.IntField("Startup Probe Timeout (ms)", draft.startupProbeTimeoutMs);
            });
        }

        private void DrawModuleSection(SettingsDraft draft)
        {
            DrawSettingsGroup("Modules", () =>
            {
                draft.enabledModules = DrawEnabledModulesField();
            });
        }

        private void DrawTransportSection(SettingsDraft draft)
        {
            DrawSettingsGroup("Control Transport", () =>
            {
                draft.controlTransport = (ControlTransportKind)EditorGUILayout.EnumPopup("Transport", draft.controlTransport);
                switch (draft.controlTransport)
                {
                    case ControlTransportKind.Http:
                        draft.controlHttpUrl = EditorGUILayout.TextField("HTTP URL", draft.controlHttpUrl);
                        break;
                    case ControlTransportKind.Pipe:
                        draft.controlPipeName = EditorGUILayout.TextField("Pipe Name", draft.controlPipeName);
                        break;
                }

                draft.controlTimeoutMs = EditorGUILayout.IntField("Control Timeout (ms)", draft.controlTimeoutMs);
            });
        }

        private void DrawSafetySection(SettingsDraft draft)
        {
            DrawSettingsGroup("Safety", () =>
            {
                draft.allowedPathPrefixes = EditorGUILayout.TextField("Allowed Path Prefixes", draft.allowedPathPrefixes);
                draft.allowedComponentTypes = EditorGUILayout.TextField("Allowed Component Types", draft.allowedComponentTypes);
            });
        }

        private void DrawAutomationSection(SettingsDraft draft)
        {
            DrawSettingsGroup("Automation", () =>
            {
                draft.autoStartControlOnLoad = EditorGUILayout.Toggle("Auto Start Control On Load", draft.autoStartControlOnLoad);
                draft.autoStartGatewayWithControl = EditorGUILayout.Toggle("Auto Start Gateway With Control", draft.autoStartGatewayWithControl);
            });
        }

        private static void DrawSettingsGroup(string title, Action drawContent)
        {
            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                EditorGUILayout.Space(2f);
                drawContent();
            }
        }

        private string DrawEnabledModulesField()
        {
            ToolModuleManifest manifest = LoadToolModuleManifest();
            if (manifest == null || manifest.modules == null || manifest.modules.Length == 0)
            {
                return EditorGUILayout.TextField("Enabled Modules", settings.EnabledModules);
            }

            string[] availableModules = GetAvailableModuleNames(manifest);
            string[] defaultModules = GetDefaultModuleNames(manifest);
            ModulePresetKind resolvedPreset = ResolveModulePreset(ParseEnabledModules(settings.EnabledModules), availableModules, defaultModules);
            if (!modulePresetInitialized)
            {
                modulePresetSelection = resolvedPreset;
                customEnabledModulesDraft = settings.EnabledModules;
                modulePresetInitialized = true;
            }

            string[] presetLabels = { "Default", "Safe Edit", "Full Automation", "Custom" };
            ModulePresetKind previousPreset = modulePresetSelection;
            ModulePresetKind selectedPreset = (ModulePresetKind)EditorGUILayout.Popup("Module Preset", (int)modulePresetSelection, presetLabels);
            if (selectedPreset != modulePresetSelection)
            {
                if (selectedPreset == ModulePresetKind.Custom)
                {
                    string[] seedModules = previousPreset == ModulePresetKind.Custom
                        ? ParseEnabledModules(customEnabledModulesDraft)
                        : GetPresetModules(previousPreset, availableModules, defaultModules);
                    customEnabledModulesDraft = string.Join(", ", seedModules);
                }

                modulePresetSelection = selectedPreset;
            }

            string[] resolvedModules;
            switch (modulePresetSelection)
            {
                case ModulePresetKind.Default:
                    resolvedModules = defaultModules;
                    break;
                case ModulePresetKind.SafeEdit:
                    resolvedModules = BuildSafeEditModules(availableModules);
                    break;
                case ModulePresetKind.FullAutomation:
                    resolvedModules = availableModules;
                    break;
                default:
                    return DrawCustomEnabledModulesField(availableModules);
            }

            DrawEnabledModulesSummary(resolvedModules);
            return modulePresetSelection == ModulePresetKind.Default
                ? string.Empty
                : string.Join(", ", resolvedModules);
        }

        private string DrawCustomEnabledModulesField(string[] availableModules)
        {
            string[] configuredModules = ParseEnabledModules(customEnabledModulesDraft);
            int mask = BuildModuleMask(availableModules, configuredModules);
            int updatedMask = EditorGUILayout.MaskField("Enabled Modules", mask, availableModules);

            string[] unknownModules = CollectUnknownModules(configuredModules, availableModules);
            if (unknownModules.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    "Unknown enabled modules are configured and will be preserved: " + string.Join(", ", unknownModules),
                    MessageType.Info);
            }

            string formattedModules = FormatEnabledModules(availableModules, updatedMask, unknownModules);
            customEnabledModulesDraft = formattedModules;
            DrawEnabledModulesSummary(ParseEnabledModules(formattedModules));
            return formattedModules;
        }

        private ToolModuleManifest LoadToolModuleManifest()
        {
            try
            {
                string repositoryRoot = settings.ResolveRepositoryRoot();
                string manifestPath = Path.Combine(repositoryRoot, "Gateway~", "schemas", "mcp-tool-modules.json");
                if (!File.Exists(manifestPath))
                {
                    return null;
                }

                string json = File.ReadAllText(manifestPath);
                ToolModuleManifest manifest = JsonUtility.FromJson<ToolModuleManifest>(json);
                if (manifest == null || manifest.modules == null || manifest.modules.Length == 0)
                {
                    return null;
                }

                return manifest;
            }
            catch
            {
                return null;
            }
        }

        private static string[] ParseEnabledModules(string enabledModules)
        {
            if (string.IsNullOrWhiteSpace(enabledModules))
            {
                return Array.Empty<string>();
            }

            string[] parts = enabledModules.Split(',');
            var modules = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string value = parts[i].Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    modules.Add(value);
                }
            }

            return modules.ToArray();
        }

        private static int BuildModuleMask(string[] availableModules, string[] configuredModules)
        {
            int mask = 0;
            for (int i = 0; i < availableModules.Length; i++)
            {
                for (int j = 0; j < configuredModules.Length; j++)
                {
                    if (string.Equals(availableModules[i], configuredModules[j], StringComparison.OrdinalIgnoreCase))
                    {
                        mask |= 1 << i;
                        break;
                    }
                }
            }

            return mask;
        }

        private static string[] CollectUnknownModules(string[] configuredModules, string[] availableModules)
        {
            var unknownModules = new List<string>();
            for (int i = 0; i < configuredModules.Length; i++)
            {
                string configured = configuredModules[i];
                bool found = false;
                for (int j = 0; j < availableModules.Length; j++)
                {
                    if (string.Equals(configured, availableModules[j], StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    unknownModules.Add(configured);
                }
            }

            return unknownModules.ToArray();
        }

        private static string FormatEnabledModules(string[] availableModules, int mask, string[] unknownModules)
        {
            var modules = new List<string>(availableModules.Length + unknownModules.Length);
            for (int i = 0; i < availableModules.Length; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    modules.Add(availableModules[i]);
                }
            }

            for (int i = 0; i < unknownModules.Length; i++)
            {
                modules.Add(unknownModules[i]);
            }

            return string.Join(", ", modules);
        }

        private static string[] GetAvailableModuleNames(ToolModuleManifest manifest)
        {
            var modules = new List<string>(manifest.modules.Length);
            for (int i = 0; i < manifest.modules.Length; i++)
            {
                string name = manifest.modules[i] == null ? null : manifest.modules[i].name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    modules.Add(name.Trim());
                }
            }

            return modules.ToArray();
        }

        private static string[] GetDefaultModuleNames(ToolModuleManifest manifest)
        {
            var modules = new List<string>(manifest.modules.Length);
            for (int i = 0; i < manifest.modules.Length; i++)
            {
                ToolModuleItem item = manifest.modules[i];
                if (item == null || !item.enabledByDefault || string.IsNullOrWhiteSpace(item.name))
                {
                    continue;
                }

                modules.Add(item.name.Trim());
            }

            return modules.ToArray();
        }

        private static ModulePresetKind ResolveModulePreset(string[] configuredModules, string[] availableModules, string[] defaultModules)
        {
            if (configuredModules.Length == 0)
            {
                return ModulePresetKind.Default;
            }

            if (ModuleSetsEqual(configuredModules, BuildSafeEditModules(availableModules)))
            {
                return ModulePresetKind.SafeEdit;
            }

            if (ModuleSetsEqual(configuredModules, availableModules))
            {
                return ModulePresetKind.FullAutomation;
            }

            if (ModuleSetsEqual(configuredModules, defaultModules))
            {
                return ModulePresetKind.Default;
            }

            return ModulePresetKind.Custom;
        }

        private static string[] GetPresetModules(ModulePresetKind preset, string[] availableModules, string[] defaultModules)
        {
            switch (preset)
            {
                case ModulePresetKind.Default:
                    return defaultModules;
                case ModulePresetKind.SafeEdit:
                    return BuildSafeEditModules(availableModules);
                case ModulePresetKind.FullAutomation:
                    return availableModules;
                default:
                    return Array.Empty<string>();
            }
        }

        private static string[] BuildSafeEditModules(string[] availableModules)
        {
            string[] desiredModules =
            {
                "project_read",
                "editor_read",
                "editor_execute",
                "runtime_read",
                "scene_read",
                "scene_execute",
                "gameobject_read",
                "prefab_read",
                "gameobject_write",
                "asset_read",
            };

            var modules = new List<string>(desiredModules.Length);
            for (int i = 0; i < desiredModules.Length; i++)
            {
                string desired = desiredModules[i];
                for (int j = 0; j < availableModules.Length; j++)
                {
                    if (string.Equals(desired, availableModules[j], StringComparison.OrdinalIgnoreCase))
                    {
                        modules.Add(availableModules[j]);
                        break;
                    }
                }
            }

            return modules.ToArray();
        }

        private static bool ModuleSetsEqual(string[] left, string[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            var leftSet = new HashSet<string>(left, StringComparer.OrdinalIgnoreCase);
            return leftSet.SetEquals(right);
        }

        private static void DrawEnabledModulesSummary(string[] modules)
        {
            string summary = modules.Length != 0 ? string.Join(", ", modules) : "(none)";
            EditorGUILayout.HelpBox("Enabled Modules: " + summary, MessageType.None);
        }
    }
}
