using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Blanketmen.UnityMcp.Editor.Control
{
    [FilePath("ProjectSettings/UnityMcpGatewaySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class UnityMcpGatewaySettings : ScriptableSingleton<UnityMcpGatewaySettings>
    {
        private const string DefaultDotnetExecutable = "dotnet";
        private const string DefaultGatewayProjectRelativePath = "Gateway~/UnityMcpGateway.csproj";
        private const string PackageName = "com.blanketmen.unity-mcp-server";
        private const ControlTransportKind DefaultControlTransport = ControlTransportKind.Http;
        private const string DefaultControlHttpUrl = "http://127.0.0.1:38100/";
        private const string DefaultControlPipeName = "unity-mcp-control";
        private const int DefaultControlTimeoutMs = 5000;
        private const int DefaultStartupProbeTimeoutMs = 8000;
        private const string DefaultAllowedPathPrefixes = "Assets/";
        private const string DefaultAllowedComponentTypes = "*";

        public static UnityMcpGatewaySettings Instance => instance;

        [SerializeField] private string repositoryRootOverride = string.Empty;
        [SerializeField] private string dotnetExecutable = DefaultDotnetExecutable;
        [SerializeField] private string gatewayProjectPath = DefaultGatewayProjectRelativePath;
        [SerializeField] private string enabledModules = string.Empty;
        [SerializeField] private ControlTransportKind controlTransport = DefaultControlTransport;
        [SerializeField] private string controlHttpUrl = DefaultControlHttpUrl;
        [SerializeField] private string controlPipeName = DefaultControlPipeName;
        [SerializeField] private int controlTimeoutMs = DefaultControlTimeoutMs;
        [SerializeField] private int startupProbeTimeoutMs = DefaultStartupProbeTimeoutMs;
        [SerializeField] private string allowedPathPrefixes = DefaultAllowedPathPrefixes;
        [SerializeField] private string allowedComponentTypes = DefaultAllowedComponentTypes;
        [FormerlySerializedAs("autoStartGatewayOnLoad")]
        [SerializeField] private bool autoStartControlOnLoad = true;
        [SerializeField] private bool autoStartGatewayWithControl = true;

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

        public string GatewayProjectPath
        {
            get { return gatewayProjectPath ?? DefaultGatewayProjectRelativePath; }
            set { gatewayProjectPath = string.IsNullOrWhiteSpace(value) ? DefaultGatewayProjectRelativePath : value.Trim(); }
        }

        public string EnabledModules
        {
            get { return enabledModules ?? string.Empty; }
            set { enabledModules = value == null ? string.Empty : value.Trim(); }
        }

        public ControlTransportKind ControlTransport
        {
            get { return controlTransport; }
            set { controlTransport = value; }
        }

        public string ControlHttpUrl
        {
            get
            {
                string value = string.IsNullOrWhiteSpace(controlHttpUrl) ? DefaultControlHttpUrl : controlHttpUrl.Trim();
                return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
            }
            set
            {
                string trimmed = string.IsNullOrWhiteSpace(value) ? DefaultControlHttpUrl : value.Trim();
                controlHttpUrl = trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
            }
        }

        public string ControlPipeName
        {
            get { return string.IsNullOrWhiteSpace(controlPipeName) ? DefaultControlPipeName : controlPipeName.Trim(); }
            set { controlPipeName = string.IsNullOrWhiteSpace(value) ? DefaultControlPipeName : value.Trim(); }
        }

        public int ControlTimeoutMs
        {
            get { return Mathf.Clamp(controlTimeoutMs, 500, 120000); }
            set { controlTimeoutMs = Mathf.Clamp(value, 500, 120000); }
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

        public bool AutoStartControlOnLoad
        {
            get { return autoStartControlOnLoad; }
            set { autoStartControlOnLoad = value; }
        }

        public bool AutoStartGatewayWithControl
        {
            get { return autoStartGatewayWithControl; }
            set { autoStartGatewayWithControl = value; }
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

        public string ResolveGatewayProjectPath()
        {
            EnsureDefaults();
            string configured = GatewayProjectPath.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(configured))
            {
                return Path.GetFullPath(configured);
            }

            List<string> relativeCandidates = BuildRelativeGatewayProjectCandidates(configured);
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

        private static List<string> BuildRelativeGatewayProjectCandidates(string configured)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                candidates.Add(configured);
            }

            string defaultRelative = DefaultGatewayProjectRelativePath.Replace('/', Path.DirectorySeparatorChar);
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

        public void SaveToDisk()
        {
            Save(true);
        }

        public void EnsureDefaults()
        {
            DotnetExecutable = DotnetExecutable;
            GatewayProjectPath = GatewayProjectPath;
            EnabledModules = EnabledModules;
            ControlTransport = ControlTransport;
            ControlHttpUrl = ControlHttpUrl;
            ControlPipeName = ControlPipeName;
            ControlTimeoutMs = ControlTimeoutMs;
            StartupProbeTimeoutMs = StartupProbeTimeoutMs;
            AllowedPathPrefixes = AllowedPathPrefixes;
            AllowedComponentTypes = AllowedComponentTypes;
            AutoStartControlOnLoad = AutoStartControlOnLoad;
            AutoStartGatewayWithControl = AutoStartGatewayWithControl;
        }

        private static string TryResolvePackageRoot()
        {
            try
            {
                PackageInfo package = PackageInfo.FindForAssembly(typeof(UnityMcpControlServer).Assembly);
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

