using System;
using System.Collections.Generic;
using System.Linq;
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
            GameObjectGetResult payload = ControlReadSupport.BuildGameObjectGetResult(gameObject, args.includeChildren, childLimit);
            return ControlResponses.Success("unity_gameobject_get completed.", payload);
        }

        public static ControlToolCallResponse HandleGameObjectGetComponentFields(ControlToolCallRequest request)
        {
            ComponentGetFieldsArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ComponentGetFieldsArgs
                {
                    includePrivateSerialized = false,
                });

            if (args.target == null)
            {
                return ControlResponses.Error("target is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject gameObject, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            if (!ControlWriteSupport.TryResolveComponent(gameObject, args.componentType, args.componentId, out Component component))
            {
                return ControlResponses.Error("Component not found on target GameObject.", "not_found", request.name);
            }

            var payload = new ComponentGetFieldsResult
            {
                componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString(),
                componentType = component.GetType().FullName ?? component.GetType().Name,
                fields = ControlReadSupport.ExtractSerializedComponentFields(component, args.includePrivateSerialized).ToArray(),
            };

            return ControlResponses.Success("unity_gameobject_get_component_fields completed.", payload);
        }

        public static ControlToolCallResponse HandleGameObjectListComponents(ControlToolCallRequest request)
        {
            ComponentListArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ComponentListArgs());

            if (args.target == null)
            {
                return ControlResponses.Error("target is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject gameObject, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            var payload = new ComponentListResult
            {
                globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString(),
                scenePath = gameObject.scene.path,
                hierarchyPath = ControlReadSupport.BuildHierarchyPath(gameObject.transform),
                items = ControlReadSupport.BuildComponentSummaries(gameObject).ToArray(),
            };

            return ControlResponses.Success("unity_gameobject_list_components completed.", payload);
        }

        public static ControlToolCallResponse HandleGameObjectGetComponentFieldsBatch(ControlToolCallRequest request)
        {
            ComponentGetFieldsBatchArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ComponentGetFieldsBatchArgs
                {
                    includePrivateSerialized = false,
                });

            if (args.componentIds == null || args.componentIds.Length == 0)
            {
                return ControlResponses.Error("componentIds is required.", "invalid_argument", request.name);
            }

            if (args.componentIds.Length > 100)
            {
                return ControlResponses.Error("componentIds exceeds max batch size 100.", "invalid_argument", request.name);
            }

            var items = new List<ComponentGetFieldsBatchItem>();
            int succeeded = 0;
            for (int i = 0; i < args.componentIds.Length; i++)
            {
                string componentId = args.componentIds[i];
                if (!TryResolveComponentByGlobalId(componentId, out Component component, out string batchResolveError))
                {
                    items.Add(new ComponentGetFieldsBatchItem
                    {
                        componentId = componentId,
                        status = "failed",
                        message = batchResolveError,
                    });
                    continue;
                }

                GameObject target = component.gameObject;
                items.Add(new ComponentGetFieldsBatchItem
                {
                    componentId = componentId,
                    componentType = component.GetType().FullName ?? component.GetType().Name,
                    targetGlobalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(),
                    scenePath = target.scene.path,
                    hierarchyPath = ControlReadSupport.BuildHierarchyPath(target.transform),
                    status = "succeeded",
                    message = "Fields loaded.",
                    fields = ControlReadSupport.ExtractSerializedComponentFields(component, args.includePrivateSerialized).ToArray(),
                });
                succeeded++;
            }

            var payload = new ComponentGetFieldsBatchResult
            {
                requested = args.componentIds.Length,
                succeeded = succeeded,
                failed = args.componentIds.Length - succeeded,
                items = items.ToArray(),
            };

            return ControlResponses.Success("unity_gameobject_get_component_fields_batch completed.", payload);
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
                output.Add(new GoFindItem
                {
                    globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString(),
                    scenePath = gameObject.scene.path,
                    hierarchyPath = ControlReadSupport.BuildHierarchyPath(gameObject.transform),
                    name = gameObject.name,
                    activeSelf = gameObject.activeSelf,
                    tag = gameObject.tag,
                    layer = gameObject.layer,
                    componentTypes = ControlReadSupport.GetComponentTypeNames(gameObject),
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
                    if (!ControlReadSupport.HasComponentType(gameObject, args.hasComponents[i]))
                    {
                        return false;
                    }
                }
            }

            if (!string.IsNullOrEmpty(args.hierarchyPathPrefix))
            {
                string hierarchyPath = ControlReadSupport.BuildHierarchyPath(gameObject.transform);
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

        private static bool TryResolveComponentByGlobalId(string componentId, out Component component, out string error)
        {
            component = null;
            error = null;

            if (string.IsNullOrEmpty(componentId))
            {
                error = "componentId is required.";
                return false;
            }

            if (!GlobalObjectId.TryParse(componentId, out GlobalObjectId globalId))
            {
                error = "Invalid componentId.";
                return false;
            }

            component = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as Component;
            if (component == null)
            {
                error = "Component not found by componentId.";
                return false;
            }

            return true;
        }
    }
}
