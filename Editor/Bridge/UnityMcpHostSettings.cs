using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    [FilePath("ProjectSettings/UnityMcpHostSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class UnityMcpHostSettings : ScriptableSingleton<UnityMcpHostSettings>
    {
        private const string DefaultDotnetExecutable = "dotnet";
        private const string DefaultHostProjectRelativePath = "Editor/Host~/UnityMcpServer.Host.csproj";
        private const string PackageName = "com.blanketmen.unity-mcp-server";
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

            List<string> relativeCandidates = BuildRelativeHostProjectCandidates(configured);
            List<string> rootCandidates = EnumerateRootCandidates();

            for (int i = 0; i < relativeCandidates.Count; i++)
            {
                string relativePath = relativeCandidates[i];
                for (int j = 0; j < rootCandidates.Count; j++)
                {
                    string root = rootCandidates[j];
                    string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return Path.GetFullPath(Path.Combine(ResolveRepositoryRoot(), relativeCandidates[0]));
        }

        private static List<string> BuildRelativeHostProjectCandidates(string configured)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                candidates.Add(configured);
            }

            string defaultRelative = DefaultHostProjectRelativePath.Replace('/', Path.DirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(defaultRelative) &&
                !string.Equals(configured, defaultRelative, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(defaultRelative);
            }

            return candidates;
        }

        private List<string> EnumerateRootCandidates()
        {
            var roots = new List<string>();

            if (!string.IsNullOrWhiteSpace(RepositoryRootOverride))
            {
                AddDistinct(roots, Path.GetFullPath(RepositoryRootOverride));
            }

            string packageRoot = TryResolvePackageRoot();
            if (!string.IsNullOrEmpty(packageRoot))
            {
                AddDistinct(roots, packageRoot);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            AddDistinct(roots, projectRoot);

            string embeddedPath = Path.Combine(projectRoot, "Packages", PackageName);
            if (Directory.Exists(embeddedPath))
            {
                AddDistinct(roots, embeddedPath);
            }

            string packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
            if (Directory.Exists(packageCacheRoot))
            {
                string[] exactMatches = Directory.GetDirectories(packageCacheRoot, PackageName + "@*");
                for (int i = 0; i < exactMatches.Length; i++)
                {
                    AddDistinct(roots, exactMatches[i]);
                }

                string[] looseMatches = Directory.GetDirectories(packageCacheRoot, PackageName + "*");
                for (int i = 0; i < looseMatches.Length; i++)
                {
                    AddDistinct(roots, looseMatches[i]);
                }
            }

            return roots;
        }

        private static void AddDistinct(List<string> items, string value)
        {
            if (items == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string normalized = Path.GetFullPath(value);
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            items.Add(normalized);
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
            return string.Equals(value, "pipe", StringComparison.OrdinalIgnoreCase) ? "pipe" : "http";
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

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string embeddedPath = Path.Combine(projectRoot, "Packages", PackageName);
                if (Directory.Exists(embeddedPath))
                {
                    return Path.GetFullPath(embeddedPath);
                }

                string packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
                if (Directory.Exists(packageCacheRoot))
                {
                    string[] exactMatches = Directory.GetDirectories(packageCacheRoot, PackageName + "@*");
                    if (exactMatches.Length > 0)
                    {
                        return Path.GetFullPath(exactMatches[0]);
                    }

                    string[] looseMatches = Directory.GetDirectories(packageCacheRoot, PackageName + "*");
                    if (looseMatches.Length > 0)
                    {
                        return Path.GetFullPath(looseMatches[0]);
                    }
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

