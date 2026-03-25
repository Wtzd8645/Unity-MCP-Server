using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class GameObjectReadToolHandlers
    {
        public static ControlToolCallResponse HandleGoFind(ControlToolCallRequest request)
        {
            GoFindArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GoFindArgs
                {
                    limit = 200,
                    offset = 0,
                    inSelection = false,
                });

            Scene scene;
            if (string.IsNullOrEmpty(args.scenePath))
            {
                scene = SceneManager.GetActiveScene();
            }
            else
            {
                scene = SceneManager.GetSceneByPath(args.scenePath);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                return ControlResponses.Error("Scene is not loaded. Use unity_scene_open first.", "not_found", request.name);
            }

            bool filterIsActive = ControlJson.RawJsonContainsProperty(request.argumentsJson, "isActive");
            HashSet<int> selected = args.inSelection
                ? new HashSet<int>(Selection.gameObjects.Select(go => go.GetInstanceID()))
                : null;

            var matches = new List<GoFindItem>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                CollectGoMatches(roots[i], args, filterIsActive, selected, matches);
            }

            int total = matches.Count;
            PaginationRange range = ControlUtil.BuildPaginationRange(total, args.offset, args.limit, 1000);
            List<GoFindItem> page = matches.Skip(range.offset).Take(range.limit).ToList();

            var payload = new GoFindResult
            {
                total = total,
                items = page.ToArray(),
            };

            return ControlResponses.Success("unity_gameobject_find completed.", payload);
        }

        public static ControlToolCallResponse HandleGameObjectGet(ControlToolCallRequest request)
        {
            GameObjectGetArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new GameObjectGetArgs
                {
                    includeChildren = true,
                    childLimit = 100,
                });

            if (args.target == null)
            {
                return ControlResponses.Error("target is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject gameObject, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            int childLimit = ControlUtil.Clamp(args.childLimit, 1, 500, 100);
            GameObjectGetResult payload = BuildGameObjectGetResult(gameObject, args.includeChildren, childLimit);
            return ControlResponses.Success("unity_gameobject_get completed.", payload);
        }

        private static void CollectGoMatches(
            GameObject gameObject,
            GoFindArgs args,
            bool filterIsActive,
            HashSet<int> selectedInstanceIds,
            List<GoFindItem> output)
        {
            if (MatchesGo(gameObject, args, filterIsActive, selectedInstanceIds))
            {
                Component[] components = gameObject.GetComponents<Component>();
                string[] componentTypes = components
                    .Where(c => c != null)
                    .Select(c => c.GetType().FullName ?? c.GetType().Name)
                    .ToArray();

                output.Add(new GoFindItem
                {
                    globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString(),
                    scenePath = gameObject.scene.path,
                    hierarchyPath = BuildHierarchyPath(gameObject.transform),
                    name = gameObject.name,
                    activeSelf = gameObject.activeSelf,
                    tag = gameObject.tag,
                    layer = gameObject.layer,
                    componentTypes = componentTypes,
                });
            }

            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                Transform child = gameObject.transform.GetChild(i);
                CollectGoMatches(child.gameObject, args, filterIsActive, selectedInstanceIds, output);
            }
        }

        private static bool MatchesGo(
            GameObject gameObject,
            GoFindArgs args,
            bool filterIsActive,
            HashSet<int> selectedInstanceIds)
        {
            if (!string.IsNullOrEmpty(args.namePattern) &&
                gameObject.name.IndexOf(args.namePattern, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(args.tag) &&
                !string.Equals(gameObject.tag, args.tag, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (args.layer >= 0 && gameObject.layer != args.layer)
            {
                return false;
            }

            if (filterIsActive && gameObject.activeSelf != args.isActive)
            {
                return false;
            }

            if (args.hasComponents != null && args.hasComponents.Length > 0)
            {
                for (int i = 0; i < args.hasComponents.Length; i++)
                {
                    if (!HasComponentType(gameObject, args.hasComponents[i]))
                    {
                        return false;
                    }
                }
            }

            if (!string.IsNullOrEmpty(args.hierarchyPathPrefix))
            {
                string hierarchyPath = BuildHierarchyPath(gameObject.transform);
                if (!hierarchyPath.StartsWith(args.hierarchyPathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (args.inSelection)
            {
                if (selectedInstanceIds == null || !selectedInstanceIds.Contains(gameObject.GetInstanceID()))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasComponentType(GameObject gameObject, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return true;
            }

            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                if (string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObjectGetResult BuildGameObjectGetResult(GameObject gameObject, bool includeChildren, int childLimit)
        {
            Transform transform = gameObject.transform;
            var result = new GameObjectGetResult
            {
                globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString(),
                scenePath = gameObject.scene.path,
                hierarchyPath = BuildHierarchyPath(transform),
                name = gameObject.name,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                tag = gameObject.tag,
                layer = gameObject.layer,
                isStatic = gameObject.isStatic,
                parent = transform.parent == null ? null : BuildRelationItem(transform.parent.gameObject),
                children = Array.Empty<GameObjectRelationItem>(),
                childrenTruncated = false,
                components = BuildComponentSummaries(gameObject).ToArray(),
                transform = BuildTransformSnapshot(transform),
            };

            if (includeChildren)
            {
                int childCount = transform.childCount;
                int takeCount = Math.Min(childCount, childLimit);
                var children = new List<GameObjectRelationItem>(takeCount);
                for (int i = 0; i < takeCount; i++)
                {
                    children.Add(BuildRelationItem(transform.GetChild(i).gameObject));
                }

                result.children = children.ToArray();
                result.childrenTruncated = childCount > takeCount;
            }

            return result;
        }

        private static GameObjectRelationItem BuildRelationItem(GameObject gameObject)
        {
            return new GameObjectRelationItem
            {
                globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString(),
                hierarchyPath = BuildHierarchyPath(gameObject.transform),
                name = gameObject.name,
                activeSelf = gameObject.activeSelf,
            };
        }

        private static List<ComponentSummaryItem> BuildComponentSummaries(GameObject gameObject)
        {
            var items = new List<ComponentSummaryItem>();
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                ComponentSummaryItem item = BuildComponentSummary(components[i], i);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private static ComponentSummaryItem BuildComponentSummary(Component component, int index)
        {
            if (component == null)
            {
                return null;
            }

            bool hasEnabled = TryGetComponentEnabled(component, out bool enabled);
            return new ComponentSummaryItem
            {
                componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString(),
                componentType = component.GetType().FullName ?? component.GetType().Name,
                enabled = enabled,
                hasEnabled = hasEnabled,
                index = index,
            };
        }

        private static bool TryGetComponentEnabled(Component component, out bool enabled)
        {
            enabled = false;
            if (component is Behaviour behaviour)
            {
                enabled = behaviour.enabled;
                return true;
            }

            PropertyInfo property = component.GetType().GetProperty(
                "enabled",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || property.PropertyType != typeof(bool) || !property.CanRead || property.GetIndexParameters().Length != 0)
            {
                return false;
            }

            try
            {
                enabled = (bool)property.GetValue(component, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static GameObjectTransformSnapshot BuildTransformSnapshot(Transform transform)
        {
            return new GameObjectTransformSnapshot
            {
                localPosition = BuildVec3Value(transform.localPosition),
                localRotationEuler = BuildVec3Value(transform.localEulerAngles),
                localScale = BuildVec3Value(transform.localScale),
                worldPosition = BuildVec3Value(transform.position),
                worldRotationEuler = BuildVec3Value(transform.eulerAngles),
                lossyScale = BuildVec3Value(transform.lossyScale),
            };
        }

        private static Vec3Value BuildVec3Value(Vector3 value)
        {
            return new Vec3Value
            {
                x = value.x,
                y = value.y,
                z = value.z,
            };
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
