using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class EditorReadToolHandlers
    {
        public static ControlToolCallResponse HandleGetConsoleLogs(ControlToolCallRequest request, UnityControlLogStore logStore)
        {
            GetConsoleLogsArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GetConsoleLogsArgs
                {
                    includeStackTrace = true,
                    limit = 200,
                    order = "desc",
                });

            long sinceId = ControlUtil.ParseLong(args.sinceId, 0);
            HashSet<string> levels = BuildLevelSet(args.levels);
            LogSnapshot snapshot = logStore.Snapshot();

            IEnumerable<ControlLogEntry> filtered = snapshot.entries
                .Where(entry => entry.id > sinceId && IsLevelAccepted(entry.level, levels));
            if (string.Equals(args.order, "asc", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.OrderBy(entry => entry.id);
            }
            else
            {
                filtered = filtered.OrderByDescending(entry => entry.id);
            }

            int limit = ControlUtil.Clamp(args.limit, 1, 2000, 200);
            List<ControlLogEntry> page = filtered.Take(limit).ToList();

            var items = new List<ConsoleLogItem>(page.Count);
            long nextSinceId = snapshot.maxId;
            for (int i = 0; i < page.Count; i++)
            {
                ControlLogEntry log = page[i];
                if (log.id > nextSinceId)
                {
                    nextSinceId = log.id;
                }

                items.Add(new ConsoleLogItem
                {
                    id = log.id.ToString(),
                    level = log.level,
                    message = log.message,
                    stackTrace = args.includeStackTrace ? log.stackTrace : null,
                    timestampUtc = log.timestampUtc,
                });
            }

            var payload = new ConsoleLogsResult
            {
                nextSinceId = nextSinceId.ToString(),
                returned = items.Count,
                items = items.ToArray(),
            };

            return ControlResponses.Success("unity_editor_get_console_logs completed.", payload);
        }

        public static ControlToolCallResponse HandleGetSelection(ControlToolCallRequest request)
        {
            ControlJson.ParseArgs(request.argumentsJson, new GetSelectionArgs());
            EditorSelectionResult payload = BuildSelectionResult();
            return ControlResponses.Success("unity_editor_get_selection completed.", payload);
        }

        private static HashSet<string> BuildLevelSet(string[] levels)
        {
            if (levels == null || levels.Length == 0)
            {
                return null;
            }

            return new HashSet<string>(levels, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsLevelAccepted(string level, HashSet<string> acceptedLevels)
        {
            if (acceptedLevels == null || acceptedLevels.Count == 0)
            {
                return true;
            }

            return acceptedLevels.Contains(level);
        }

        private static EditorSelectionResult BuildSelectionResult()
        {
            UnityEngine.Object[] selectedObjects = Selection.objects ?? Array.Empty<UnityEngine.Object>();
            var items = new List<EditorSelectionItem>(selectedObjects.Length);
            int gameObjectCount = 0;
            int assetCount = 0;

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                EditorSelectionItem item = BuildSelectionItem(selectedObjects[i]);
                if (item == null)
                {
                    continue;
                }

                if (string.Equals(item.kind, "gameObject", StringComparison.Ordinal))
                {
                    gameObjectCount++;
                }
                else if (string.Equals(item.kind, "asset", StringComparison.Ordinal))
                {
                    assetCount++;
                }

                items.Add(item);
            }

            return new EditorSelectionResult
            {
                count = items.Count,
                gameObjectCount = gameObjectCount,
                assetCount = assetCount,
                activeObject = BuildSelectionItem(Selection.activeObject),
                items = items.ToArray(),
            };
        }

        private static EditorSelectionItem BuildSelectionItem(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return null;
            }

            bool isPersistent = EditorUtility.IsPersistent(obj);
            var item = new EditorSelectionItem
            {
                kind = GetSelectionKind(obj, isPersistent),
                name = obj.name,
                objectType = obj.GetType().FullName ?? obj.GetType().Name,
                globalObjectId = TryGetGlobalObjectId(obj),
                isPersistent = isPersistent,
            };

            if (isPersistent)
            {
                item.assetPath = AssetDatabase.GetAssetPath(obj);
                item.guid = string.IsNullOrEmpty(item.assetPath) ? null : AssetDatabase.AssetPathToGUID(item.assetPath);
            }
            else if (obj is GameObject gameObject)
            {
                item.scenePath = gameObject.scene.path;
                item.hierarchyPath = BuildHierarchyPath(gameObject.transform);
            }
            else if (obj is Component component)
            {
                item.scenePath = component.gameObject.scene.path;
                item.hierarchyPath = BuildHierarchyPath(component.gameObject.transform);
            }

            return item;
        }

        private static string GetSelectionKind(UnityEngine.Object obj, bool isPersistent)
        {
            if (obj is GameObject)
            {
                return "gameObject";
            }

            if (obj is Component)
            {
                return "component";
            }

            if (isPersistent)
            {
                return "asset";
            }

            return "other";
        }

        private static string TryGetGlobalObjectId(UnityEngine.Object obj)
        {
            try
            {
                return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            string current = transform.name;
            Transform parent = transform.parent;
            while (parent != null)
            {
                current = parent.name + "/" + current;
                parent = parent.parent;
            }

            return current;
        }
    }
}
