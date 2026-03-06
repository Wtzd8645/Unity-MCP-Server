using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal static class AssetReadToolHandlers
    {
        public static BridgeToolCallResponse HandleAssetSearch(BridgeToolCallRequest request)
        {
            AssetSearchArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new AssetSearchArgs
                {
                    includePackages = false,
                    limit = 100,
                    offset = 0,
                    sortBy = "path",
                    sortOrder = "asc",
                    pathPrefixes = new[] { "Assets/" },
                });

            string[] folders = BridgeUtil.BuildSearchFolders(args.pathPrefixes, args.includePackages);
            string filter = BuildAssetSearchFilter(args);
            string[] guids = folders == null
                ? AssetDatabase.FindAssets(filter)
                : AssetDatabase.FindAssets(filter, folders);

            var items = new List<AssetSearchItem>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string guid = guids[i];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                items.Add(BuildAssetSearchItem(guid, path));
            }

            items = SortAssetSearchItems(items, args.sortBy, args.sortOrder);

            int total = items.Count;
            PaginationRange range = BridgeUtil.BuildPaginationRange(total, args.offset, args.limit, 500);
            List<AssetSearchItem> page = items.Skip(range.offset).Take(range.limit).ToList();

            var payload = new AssetSearchResult
            {
                total = total,
                items = page.ToArray(),
            };

            return BridgeResponses.Success("unity.asset_search completed.", payload);
        }

        public static BridgeToolCallResponse HandleAssetGet(BridgeToolCallRequest request)
        {
            AssetGetArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new AssetGetArgs
                {
                    includeDependencies = true,
                    includeDependents = false,
                    includeMeta = false,
                });

            string targetPath;
            string targetGuid;
            if (!TryResolveAssetRef(args.target, out targetPath, out targetGuid))
            {
                return BridgeResponses.Error("Asset target not found.", "not_found", request.name);
            }

            var payload = new AssetGetResult
            {
                asset = BuildAssetGetAsset(targetPath, targetGuid),
            };

            if (args.includeDependencies)
            {
                payload.dependencies = GetAssetDependencies(targetPath)
                    .Select(depPath => BuildAssetRefNode(depPath))
                    .ToArray();
            }

            if (args.includeDependents)
            {
                payload.dependents = FindAssetDependents(targetPath)
                    .Select(depPath => BuildAssetRefNode(depPath))
                    .ToArray();
            }

            if (args.includeMeta)
            {
                payload.meta = BuildAssetMeta(targetPath, targetGuid);
            }

            return BridgeResponses.Success("unity.asset_get completed.", payload);
        }

        public static BridgeToolCallResponse HandleAssetRefs(BridgeToolCallRequest request)
        {
            AssetRefsArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new AssetRefsArgs
                {
                    direction = "inbound",
                    recursive = false,
                    maxDepth = 1,
                });

            string targetPath;
            string targetGuid;
            if (!TryResolveAssetRef(args.target, out targetPath, out targetGuid))
            {
                return BridgeResponses.Error("Asset target not found.", "not_found", request.name);
            }

            int maxDepth = Math.Max(1, args.maxDepth);
            bool recursive = args.recursive;
            string direction = string.IsNullOrEmpty(args.direction) ? "inbound" : args.direction;

            var nodesByGuid = new Dictionary<string, AssetRefNode>(StringComparer.Ordinal);
            var edges = new List<AssetRefEdge>();
            AddAssetNode(nodesByGuid, targetPath);

            if (string.Equals(direction, "outbound", StringComparison.OrdinalIgnoreCase))
            {
                TraverseOutboundRefs(targetPath, 1, recursive, maxDepth, nodesByGuid, edges, new HashSet<string>(StringComparer.Ordinal));
            }
            else
            {
                TraverseInboundRefs(targetPath, 1, recursive, maxDepth, nodesByGuid, edges, new HashSet<string>(StringComparer.Ordinal));
            }

            if (args.filterTypes != null && args.filterTypes.Length > 0)
            {
                var allowedTypes = new HashSet<string>(args.filterTypes, StringComparer.OrdinalIgnoreCase);
                var filteredNodes = nodesByGuid.Values
                    .Where(node => node.guid == targetGuid || allowedTypes.Contains(node.type))
                    .ToList();
                var filteredGuids = new HashSet<string>(filteredNodes.Select(n => n.guid), StringComparer.Ordinal);
                edges = edges.Where(edge => filteredGuids.Contains(edge.fromGuid) && filteredGuids.Contains(edge.toGuid)).ToList();
                nodesByGuid = filteredNodes.ToDictionary(n => n.guid, n => n, StringComparer.Ordinal);
            }

            var payload = new AssetRefsResult
            {
                nodes = nodesByGuid.Values.OrderBy(n => n.path, StringComparer.OrdinalIgnoreCase).ToArray(),
                edges = edges.ToArray(),
            };

            return BridgeResponses.Success("unity.asset_refs completed.", payload);
        }

        private static void TraverseOutboundRefs(
            string fromPath,
            int depth,
            bool recursive,
            int maxDepth,
            Dictionary<string, AssetRefNode> nodesByGuid,
            List<AssetRefEdge> edges,
            HashSet<string> visited)
        {
            if (depth > maxDepth)
            {
                return;
            }

            if (!visited.Add(fromPath + "|" + depth))
            {
                return;
            }

            string fromGuid = AssetDatabase.AssetPathToGUID(fromPath);
            string[] dependencies = GetAssetDependencies(fromPath);
            for (int i = 0; i < dependencies.Length; i++)
            {
                string toPath = dependencies[i];
                if (string.IsNullOrEmpty(toPath))
                {
                    continue;
                }

                AssetRefNode toNode = AddAssetNode(nodesByGuid, toPath);
                edges.Add(new AssetRefEdge
                {
                    fromGuid = fromGuid,
                    toGuid = toNode.guid,
                    kind = "reference",
                });

                if (recursive)
                {
                    TraverseOutboundRefs(toPath, depth + 1, recursive, maxDepth, nodesByGuid, edges, visited);
                }
            }
        }

        private static void TraverseInboundRefs(
            string targetPath,
            int depth,
            bool recursive,
            int maxDepth,
            Dictionary<string, AssetRefNode> nodesByGuid,
            List<AssetRefEdge> edges,
            HashSet<string> visited)
        {
            if (depth > maxDepth)
            {
                return;
            }

            if (!visited.Add(targetPath + "|" + depth))
            {
                return;
            }

            string targetGuid = AssetDatabase.AssetPathToGUID(targetPath);
            string[] dependents = FindAssetDependents(targetPath);
            for (int i = 0; i < dependents.Length; i++)
            {
                string fromPath = dependents[i];
                AssetRefNode fromNode = AddAssetNode(nodesByGuid, fromPath);
                edges.Add(new AssetRefEdge
                {
                    fromGuid = fromNode.guid,
                    toGuid = targetGuid,
                    kind = "reference",
                });

                if (recursive)
                {
                    TraverseInboundRefs(fromPath, depth + 1, recursive, maxDepth, nodesByGuid, edges, visited);
                }
            }
        }

        private static AssetRefNode AddAssetNode(Dictionary<string, AssetRefNode> nodesByGuid, string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                guid = path;
            }

            AssetRefNode node;
            if (!nodesByGuid.TryGetValue(guid, out node))
            {
                node = BuildAssetRefNode(path);
                nodesByGuid[guid] = node;
            }

            return node;
        }

        private static string BuildAssetSearchFilter(AssetSearchArgs args)
        {
            var terms = new List<string>();
            if (!string.IsNullOrEmpty(args.query))
            {
                terms.Add(args.query);
            }

            if (args.types != null)
            {
                for (int i = 0; i < args.types.Length; i++)
                {
                    if (!string.IsNullOrEmpty(args.types[i]))
                    {
                        terms.Add("t:" + args.types[i]);
                    }
                }
            }

            if (args.labels != null)
            {
                for (int i = 0; i < args.labels.Length; i++)
                {
                    if (!string.IsNullOrEmpty(args.labels[i]))
                    {
                        terms.Add("l:" + args.labels[i]);
                    }
                }
            }

            return string.Join(" ", terms);
        }

        private static AssetSearchItem BuildAssetSearchItem(string guid, string path)
        {
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            string[] labels = mainAsset != null ? AssetDatabase.GetLabels(mainAsset) : new string[0];
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);

            return new AssetSearchItem
            {
                guid = guid,
                path = path,
                name = Path.GetFileNameWithoutExtension(path),
                type = assetType != null ? assetType.Name : "Unknown",
                labels = labels,
                isMainAsset = true,
                modifiedTimeUtc = TryGetAssetModifiedTimeUtc(path),
            };
        }

        private static List<AssetSearchItem> SortAssetSearchItems(List<AssetSearchItem> items, string sortBy, string sortOrder)
        {
            bool ascending = !string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);
            string key = string.IsNullOrEmpty(sortBy) ? "path" : sortBy;

            Func<AssetSearchItem, object> selector;
            switch (key)
            {
                case "name":
                    selector = item => item.name ?? string.Empty;
                    break;
                case "type":
                    selector = item => item.type ?? string.Empty;
                    break;
                case "modifiedTime":
                    selector = item => item.modifiedTimeUtc ?? string.Empty;
                    break;
                default:
                    selector = item => item.path ?? string.Empty;
                    break;
            }

            if (ascending)
            {
                return items.OrderBy(selector).ToList();
            }

            return items.OrderByDescending(selector).ToList();
        }

        private static string TryGetAssetModifiedTimeUtc(string assetPath)
        {
            try
            {
                string projectRoot = BridgeUtil.GetProjectRootPath();
                string relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
                string absolute = Path.Combine(projectRoot, relative);
                if (File.Exists(absolute))
                {
                    return File.GetLastWriteTimeUtc(absolute).ToString("O");
                }
            }
            catch
            {
                // Ignore.
            }

            return null;
        }

        private static bool TryResolveAssetRef(AssetRef target, out string path, out string guid)
        {
            path = null;
            guid = null;

            if (target == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(target.guid))
            {
                guid = target.guid;
                path = AssetDatabase.GUIDToAssetPath(guid);
            }
            else if (!string.IsNullOrEmpty(target.path))
            {
                if (!BridgeWriteSupport.TryNormalizeAssetPath(target.path, out string normalizedPath, out _))
                {
                    return false;
                }

                path = normalizedPath;
                guid = AssetDatabase.AssetPathToGUID(path);
            }

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (!BridgeWriteSupport.TryValidateAssetPathAllowed(path, out _))
            {
                return false;
            }

            return true;
        }

        private static AssetGetAsset BuildAssetGetAsset(string path, string guid)
        {
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            string[] labels = mainAsset != null ? AssetDatabase.GetLabels(mainAsset) : new string[0];
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            long fileSize = TryGetFileSize(path);

            return new AssetGetAsset
            {
                guid = guid,
                path = path,
                name = Path.GetFileNameWithoutExtension(path),
                type = assetType != null ? assetType.Name : "Unknown",
                fileSizeBytes = fileSize,
                hasFileSizeBytes = fileSize >= 0,
                labels = labels,
                importerType = importer != null ? importer.GetType().Name : null,
            };
        }

        private static long TryGetFileSize(string assetPath)
        {
            try
            {
                string projectRoot = BridgeUtil.GetProjectRootPath();
                string relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
                string absolute = Path.Combine(projectRoot, relative);
                if (File.Exists(absolute))
                {
                    return new FileInfo(absolute).Length;
                }
            }
            catch
            {
                // Ignore.
            }

            return -1;
        }

        private static AssetMeta BuildAssetMeta(string path, string guid)
        {
            string projectRoot = BridgeUtil.GetProjectRootPath();
            string metaRelativePath = path + ".meta";
            string metaAbsolutePath = Path.Combine(projectRoot, metaRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string metaContent = null;
            if (File.Exists(metaAbsolutePath))
            {
                metaContent = File.ReadAllText(metaAbsolutePath);
            }

            return new AssetMeta
            {
                guid = guid,
                assetPath = path,
                metaPath = metaRelativePath,
                metaContent = metaContent,
            };
        }

        private static AssetRefNode BuildAssetRefNode(string path)
        {
            return new AssetRefNode
            {
                guid = AssetDatabase.AssetPathToGUID(path),
                path = path,
                type = GetAssetTypeName(path),
            };
        }

        private static string GetAssetTypeName(string path)
        {
            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null ? type.Name : "Unknown";
        }

        private static string[] GetAssetDependencies(string path)
        {
            string[] deps = AssetDatabase.GetDependencies(path, false);
            return deps
                .Where(dep => !string.Equals(dep, path, StringComparison.OrdinalIgnoreCase))
                .Where(dep => dep.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] FindAssetDependents(string targetPath)
        {
            var dependents = new List<string>();
            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < allPaths.Length; i++)
            {
                string candidate = allPaths[i];
                if (!candidate.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(candidate, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] deps = AssetDatabase.GetDependencies(candidate, false);
                for (int j = 0; j < deps.Length; j++)
                {
                    if (string.Equals(deps[j], targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        dependents.Add(candidate);
                        break;
                    }
                }
            }

            return dependents.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
