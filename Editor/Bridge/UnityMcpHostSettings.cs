#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Blanketmen.UnityMcpBridge.Editor
{
    [FilePath("ProjectSettings/UnityMcpHostSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class UnityMcpHostSettings : ScriptableSingleton<UnityMcpHostSettings>
    {
        private const string DefaultDotnetExecutable = "dotnet";
        private const string DefaultHostProjectRelativePath = "Editor/Host~/UnityMcpServer.Host.csproj";
        private const string DefaultBridgeTransport = "http";
        private const string DefaultBridgeHttpUrl = "http://127.0.0.1:38100/";
        private const string DefaultBridgePipeName = "unity-mcp-bridge";
        private const int DefaultBridgeTimeoutMs = 5000;
        private const int DefaultStartupProbeTimeoutMs = 8000;
        private const string DefaultAllowedPathPrefixes = "Assets/";
        private const string DefaultAllowedComponentTypes = "*";

        [SerializeField] private string repositoryRootOverride = string.Empty;
        [SerializeField] private string dotnetExecutable = DefaultDotnetExecutable;
        [SerializeField] private string hostProjectPath = DefaultHostProjectRelativePath;
        [SerializeField] private string enabledModules = string.Empty;
        [SerializeField] private string bridgeTransport = DefaultBridgeTransport;
        [SerializeField] private string bridgeHttpUrl = DefaultBridgeHttpUrl;
        [SerializeField] private string bridgePipeName = DefaultBridgePipeName;
        [SerializeField] private int bridgeTimeoutMs = DefaultBridgeTimeoutMs;
        [SerializeField] private int startupProbeTimeoutMs = DefaultStartupProbeTimeoutMs;
        [SerializeField] private string allowedPathPrefixes = DefaultAllowedPathPrefixes;
        [SerializeField] private string allowedComponentTypes = DefaultAllowedComponentTypes;
        [SerializeField] private bool autoStartBridgeWithHost = true;
        [SerializeField] private bool autoStartHostOnLoad;

        public static UnityMcpHostSettings GetOrCreate()
        {
            UnityMcpHostSettings settings = instance;
            settings.EnsureDefaults();
            return settings;
        }

        public string RepositoryRootOverride
        {
            get { return repositoryRootOverride ?? string.Empty; }
            set { repositoryRootOverride = value ?? string.Empty; }
        }

        public string DotnetExecutable
        {
            get { return dotnetExecutable ?? DefaultDotnetExecutable; }
            set { dotnetExecutable = string.IsNullOrWhiteSpace(value) ? DefaultDotnetExecutable : value.Trim(); }
        }

        public string HostProjectPath
        {
            get { return hostProjectPath ?? DefaultHostProjectRelativePath; }
            set { hostProjectPath = string.IsNullOrWhiteSpace(value) ? DefaultHostProjectRelativePath : value.Trim(); }
        }

        public string EnabledModules
        {
            get { return enabledModules ?? string.Empty; }
            set { enabledModules = value == null ? string.Empty : value.Trim(); }
        }

        public string BridgeTransport
        {
            get { return NormalizeTransport(bridgeTransport); }
            set { bridgeTransport = NormalizeTransport(value); }
        }

        public string BridgeHttpUrl
        {
            get
            {
                string value = string.IsNullOrWhiteSpace(bridgeHttpUrl) ? DefaultBridgeHttpUrl : bridgeHttpUrl.Trim();
                return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
            }
            set
            {
                string trimmed = string.IsNullOrWhiteSpace(value) ? DefaultBridgeHttpUrl : value.Trim();
                bridgeHttpUrl = trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
            }
        }

        public string BridgePipeName
        {
            get { return string.IsNullOrWhiteSpace(bridgePipeName) ? DefaultBridgePipeName : bridgePipeName.Trim(); }
            set { bridgePipeName = string.IsNullOrWhiteSpace(value) ? DefaultBridgePipeName : value.Trim(); }
        }

        public int BridgeTimeoutMs
        {
            get { return Mathf.Clamp(bridgeTimeoutMs, 500, 120000); }
            set { bridgeTimeoutMs = Mathf.Clamp(value, 500, 120000); }
        }

        public int StartupProbeTimeoutMs
        {
            get { return Mathf.Clamp(startupProbeTimeoutMs, 1000, 120000); }
            set { startupProbeTimeoutMs = Mathf.Clamp(value, 1000, 120000); }
        }

        public string AllowedPathPrefixes
        {
            get { return string.IsNullOrWhiteSpace(allowedPathPrefixes) ? DefaultAllowedPathPrefixes : allowedPathPrefixes.Trim(); }
            set { allowedPathPrefixes = string.IsNullOrWhiteSpace(value) ? DefaultAllowedPathPrefixes : value.Trim(); }
        }

        public string AllowedComponentTypes
        {
            get { return string.IsNullOrWhiteSpace(allowedComponentTypes) ? DefaultAllowedComponentTypes : allowedComponentTypes.Trim(); }
            set { allowedComponentTypes = string.IsNullOrWhiteSpace(value) ? DefaultAllowedComponentTypes : value.Trim(); }
        }

        public bool AutoStartBridgeWithHost
        {
            get { return autoStartBridgeWithHost; }
            set { autoStartBridgeWithHost = value; }
        }

        public bool AutoStartHostOnLoad
        {
            get { return autoStartHostOnLoad; }
            set { autoStartHostOnLoad = value; }
        }

        public string ResolveRepositoryRoot()
        {
            EnsureDefaults();
            if (!string.IsNullOrWhiteSpace(RepositoryRootOverride))
            {
                return Path.GetFullPath(RepositoryRootOverride);
            }

            string packageRoot = TryResolvePackageRoot();
            if (!string.IsNullOrEmpty(packageRoot))
            {
                return packageRoot;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        public string ResolveHostProjectPath()
        {
            EnsureDefaults();
            string configured = HostProjectPath.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(configured))
            {
                return Path.GetFullPath(configured);
            }

            return Path.GetFullPath(Path.Combine(ResolveRepositoryRoot(), configured));
        }

        public void SaveSettingsToDisk()
        {
            Save(true);
        }

        private void EnsureDefaults()
        {
            DotnetExecutable = DotnetExecutable;
            HostProjectPath = HostProjectPath;
            EnabledModules = EnabledModules;
            BridgeTransport = BridgeTransport;
            BridgeHttpUrl = BridgeHttpUrl;
            BridgePipeName = BridgePipeName;
            BridgeTimeoutMs = BridgeTimeoutMs;
            StartupProbeTimeoutMs = StartupProbeTimeoutMs;
            AllowedPathPrefixes = AllowedPathPrefixes;
            AllowedComponentTypes = AllowedComponentTypes;
        }

        private static string NormalizeTransport(string value)
        {
            if (string.Equals(value, "pipe", StringComparison.OrdinalIgnoreCase))
            {
                return "pipe";
            }

            return "http";
        }

        private static string TryResolvePackageRoot()
        {
            try
            {
                PackageInfo package = PackageInfo.FindForAssembly(typeof(UnityMcpBridgeServer).Assembly);
                if (package != null && !string.IsNullOrWhiteSpace(package.resolvedPath))
                {
                    return Path.GetFullPath(package.resolvedPath);
                }
            }
            catch
            {
                // Best effort only.
            }

            return string.Empty;
        }
    }
}
#endif


