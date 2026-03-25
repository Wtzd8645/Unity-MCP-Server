using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blanketmen.UnityMcp.Control.Editor
{
    internal static class SceneReadToolHandlers
    {
        public static ControlToolCallResponse HandleListScenes(ControlToolCallRequest request)
        {
            ListScenesArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ListScenesArgs
                {
                    source = "both",
                    includeDisabled = true,
                    limit = 200,
                    offset = 0,
                });

            var byPath = new Dictionary<string, SceneListItem>(StringComparer.OrdinalIgnoreCase);
            bool includeBuildSettings = args.source == "buildSettings" || args.source == "both" || string.IsNullOrEmpty(args.source);
            bool includeAssets = args.source == "assets" || args.source == "both" || string.IsNullOrEmpty(args.source);

            if (includeBuildSettings)
            {
                EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
                for (int i = 0; i < buildScenes.Length; i++)
                {
                    EditorBuildSettingsScene scene = buildScenes[i];
                    if (!args.includeDisabled && !scene.enabled)
                    {
                        continue;
                    }

                    byPath[scene.path] = new SceneListItem
                    {
                        path = scene.path,
                        name = System.IO.Path.GetFileNameWithoutExtension(scene.path),
                        guid = AssetDatabase.AssetPathToGUID(scene.path),
                        inBuildSettings = true,
                        enabledInBuildSettings = scene.enabled,
                        hasEnabledInBuildSettings = true,
                        buildIndex = i,
                        hasBuildIndex = true,
                    };
                }
            }

            if (includeAssets)
            {
                string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
                for (int i = 0; i < guids.Length; i++)
                {
                    string guid = guids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    SceneListItem item;
                    if (!byPath.TryGetValue(path, out item))
                    {
                        item = new SceneListItem
                        {
                            path = path,
                            name = System.IO.Path.GetFileNameWithoutExtension(path),
                            guid = guid,
                            inBuildSettings = false,
                            hasBuildIndex = false,
                            hasEnabledInBuildSettings = false,
                        };
                    }
                    else if (string.IsNullOrEmpty(item.guid))
                    {
                        item.guid = guid;
                    }

                    byPath[path] = item;
                }
            }

            List<SceneListItem> all = byPath.Values
                .OrderBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            PaginationRange range = ControlUtil.BuildPaginationRange(all.Count, args.offset, args.limit, 500);
            List<SceneListItem> page = all.Skip(range.offset).Take(range.limit).ToList();

            var payload = new ListScenesResult
            {
                total = all.Count,
                items = page.ToArray(),
            };

            return ControlResponses.Success("unity_scene_list completed.", payload);
        }

        public static ControlToolCallResponse HandleOpenScene(ControlToolCallRequest request)
        {
            OpenSceneArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new OpenSceneArgs
                {
                    openMode = "Single",
                    saveModifiedScenes = false,
                    setActive = true,
                });

            if (string.IsNullOrEmpty(args.scenePath))
            {
                return ControlResponses.Error("scenePath is required.", "invalid_argument", request.name);
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(args.scenePath) == null)
            {
                return ControlResponses.Error("Scene not found: " + args.scenePath, "not_found", request.name);
            }

            if (args.saveModifiedScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return ControlResponses.Error("Open scene cancelled by user.", "cancelled", request.name);
            }

            OpenSceneMode mode = ParseOpenSceneMode(args.openMode);
            Scene openedScene = EditorSceneManager.OpenScene(args.scenePath, mode);

            if (args.setActive && openedScene.IsValid())
            {
                SceneManager.SetActiveScene(openedScene);
            }

            var payload = new OpenSceneResult
            {
                openedScenePath = openedScene.path,
                activeScenePath = SceneManager.GetActiveScene().path,
                loadedScenes = GetLoadedScenePaths(),
            };

            return ControlResponses.Success("unity_scene_open completed.", payload);
        }

        public static ControlToolCallResponse HandleSceneListLoaded(ControlToolCallRequest request)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            var payload = new SceneListLoadedResult
            {
                activeScenePath = activeScene.IsValid() ? activeScene.path : null,
                items = GetLoadedSceneItems(),
            };

            return ControlResponses.Success("unity_scene_list_loaded completed.", payload);
        }

        public static ControlToolCallResponse HandleSceneGetActive(ControlToolCallRequest request)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return ControlResponses.Error("Active scene is not available.", "not_found", request.name);
            }

            var payload = new SceneGetActiveResult
            {
                activeScenePath = activeScene.path,
                scene = BuildSceneInfo(activeScene, true),
            };

            return ControlResponses.Success("unity_scene_get_active completed.", payload);
        }

        public static ControlToolCallResponse HandleSceneSetActive(ControlToolCallRequest request)
        {
            SceneSetActiveArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new SceneSetActiveArgs());

            if (string.IsNullOrEmpty(args.scenePath))
            {
                return ControlResponses.Error("scenePath is required.", "invalid_argument", request.name);
            }

            Scene scene = SceneManager.GetSceneByPath(args.scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return ControlResponses.Error("Scene is not loaded: " + args.scenePath, "not_found", request.name);
            }

            if (!SceneManager.SetActiveScene(scene))
            {
                return ControlResponses.Error("Failed to set active scene: " + args.scenePath, "tool_exception", request.name);
            }

            var payload = new SceneSetActiveResult
            {
                activeScenePath = SceneManager.GetActiveScene().path,
                loadedScenes = GetLoadedScenePaths(),
            };

            return ControlResponses.Success("unity_scene_set_active completed.", payload);
        }

        public static ControlToolCallResponse HandleSceneClose(ControlToolCallRequest request)
        {
            SceneCloseArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new SceneCloseArgs
                {
                    removeScene = true,
                    saveModifiedScene = false,
                });

            if (string.IsNullOrEmpty(args.scenePath))
            {
                return ControlResponses.Error("scenePath is required.", "invalid_argument", request.name);
            }

            Scene scene = SceneManager.GetSceneByPath(args.scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return ControlResponses.Error("Scene is not loaded: " + args.scenePath, "not_found", request.name);
            }

            if (args.saveModifiedScene && scene.isDirty)
            {
                if (string.IsNullOrEmpty(scene.path))
                {
                    return ControlResponses.Error("Cannot save a loaded untitled scene before close.", "invalid_argument", request.name);
                }

                if (!EditorSceneManager.SaveScene(scene))
                {
                    return ControlResponses.Error("Failed to save scene before close: " + scene.path, "tool_exception", request.name);
                }
            }

            if (!EditorSceneManager.CloseScene(scene, args.removeScene))
            {
                return ControlResponses.Error("Failed to close scene: " + args.scenePath, "tool_exception", request.name);
            }

            Scene activeScene = SceneManager.GetActiveScene();
            var payload = new SceneCloseResult
            {
                closedScenePath = args.scenePath,
                activeScenePath = activeScene.IsValid() ? activeScene.path : null,
                loadedScenes = GetLoadedScenePaths(),
            };

            return ControlResponses.Success("unity_scene_close completed.", payload);
        }

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

        public static ControlToolCallResponse HandleComponentGetFields(ControlToolCallRequest request)
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

            string resolveError;
            GameObject gameObject;
            if (!TryResolveGameObject(args.target, out gameObject, out resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            Component component;
            if (!TryResolveComponent(gameObject, args.componentType, args.componentId, out component))
            {
                return ControlResponses.Error("Component not found on target GameObject.", "not_found", request.name);
            }

            string componentGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            List<ComponentFieldItem> fields = ExtractComponentFields(component, args.includePrivateSerialized);

            var payload = new ComponentGetFieldsResult
            {
                componentId = componentGlobalId,
                componentType = component.GetType().FullName ?? component.GetType().Name,
                fields = fields.ToArray(),
            };

            return ControlResponses.Success("unity_component_get_fields completed.", payload);
        }

        public static ControlToolCallResponse HandleComponentList(ControlToolCallRequest request)
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
                hierarchyPath = BuildHierarchyPath(gameObject.transform),
                items = BuildComponentSummaries(gameObject).ToArray(),
            };

            return ControlResponses.Success("unity_component_list completed.", payload);
        }

        public static ControlToolCallResponse HandleComponentGetFieldsBatch(ControlToolCallRequest request)
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
                if (!TryResolveComponentByGlobalId(componentId, out Component component, out string resolveError))
                {
                    items.Add(new ComponentGetFieldsBatchItem
                    {
                        componentId = componentId,
                        status = "failed",
                        message = resolveError,
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
                    hierarchyPath = BuildHierarchyPath(target.transform),
                    status = "succeeded",
                    message = "Fields loaded.",
                    fields = ExtractComponentFields(component, args.includePrivateSerialized).ToArray(),
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

            return ControlResponses.Success("unity_component_get_fields_batch completed.", payload);
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

        private static bool TryResolveGameObject(GameObjectRef target, out GameObject gameObject, out string error)
        {
            gameObject = null;
            error = null;

            if (target == null)
            {
                error = "target is required.";
                return false;
            }

            if (!string.IsNullOrEmpty(target.globalObjectId))
            {
                GlobalObjectId globalId;
                if (GlobalObjectId.TryParse(target.globalObjectId, out globalId))
                {
                    gameObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as GameObject;
                    if (gameObject != null)
                    {
                        return true;
                    }
                }

                error = "GameObject not found by globalObjectId.";
                return false;
            }

            if (!string.IsNullOrEmpty(target.scenePath) && !string.IsNullOrEmpty(target.hierarchyPath))
            {
                Scene scene = SceneManager.GetSceneByPath(target.scenePath);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    error = "Scene not loaded: " + target.scenePath;
                    return false;
                }

                gameObject = FindGameObjectByHierarchyPath(scene, target.hierarchyPath);
                if (gameObject == null)
                {
                    error = "GameObject not found at hierarchy path: " + target.hierarchyPath;
                    return false;
                }

                return true;
            }

            error = "target must include globalObjectId or scenePath+hierarchyPath.";
            return false;
        }

        private static bool TryResolveComponent(GameObject gameObject, string componentType, string componentId, out Component component)
        {
            component = null;
            if (gameObject == null)
            {
                return false;
            }

            Component[] components = gameObject.GetComponents<Component>();
            if (!string.IsNullOrEmpty(componentId))
            {
                for (int i = 0; i < components.Length; i++)
                {
                    Component current = components[i];
                    if (current == null)
                    {
                        continue;
                    }

                    string globalId = GlobalObjectId.GetGlobalObjectIdSlow(current).ToString();
                    if (string.Equals(globalId, componentId, StringComparison.Ordinal))
                    {
                        component = current;
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(componentType))
            {
                for (int i = 0; i < components.Length; i++)
                {
                    Component current = components[i];
                    if (current == null)
                    {
                        continue;
                    }

                    Type type = current.GetType();
                    if (string.Equals(type.FullName, componentType, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(type.Name, componentType, StringComparison.OrdinalIgnoreCase))
                    {
                        component = current;
                        return true;
                    }
                }
            }

            return false;
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

        private static List<ComponentFieldItem> ExtractComponentFields(Component component, bool includePrivateSerialized)
        {
            var result = new List<ComponentFieldItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            Type currentType = component.GetType();
            while (currentType != null && currentType != typeof(object))
            {
                FieldInfo[] fields = currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic || field.Name.Contains("k__BackingField"))
                    {
                        continue;
                    }

                    bool hasSerializeField = field.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
                    bool serialized = field.IsPublic || hasSerializeField;
                    if (!serialized)
                    {
                        continue;
                    }

                    if (!includePrivateSerialized && !field.IsPublic)
                    {
                        continue;
                    }

                    if (!seen.Add(field.Name))
                    {
                        continue;
                    }

                    object value = null;
                    try
                    {
                        value = field.GetValue(component);
                    }
                    catch
                    {
                        // Ignore getter errors.
                    }

                    result.Add(new ComponentFieldItem
                    {
                        name = field.Name,
                        fieldType = field.FieldType.FullName ?? field.FieldType.Name,
                        value = FormatFieldValue(value),
                        serialized = serialized,
                        readOnly = field.IsInitOnly || field.IsLiteral,
                    });
                }

                currentType = currentType.BaseType;
            }

            return result;
        }

        private static string FormatFieldValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            UnityEngine.Object unityObj = value as UnityEngine.Object;
            if (unityObj != null)
            {
                return unityObj.name + " (" + unityObj.GetType().Name + ")";
            }

            if (value is string)
            {
                return (string)value;
            }

            if (value is bool || value is int || value is float || value is double || value is long)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return value.ToString();
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

        private static SceneInfoItem[] GetLoadedSceneItems()
        {
            var scenes = new List<SceneInfoItem>();
            Scene activeScene = SceneManager.GetActiveScene();
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                scenes.Add(BuildSceneInfo(scene, scene == activeScene));
            }

            return scenes.ToArray();
        }

        private static SceneInfoItem BuildSceneInfo(Scene scene, bool isActive)
        {
            int buildIndex = scene.buildIndex;
            return new SceneInfoItem
            {
                path = scene.path,
                name = scene.name,
                isLoaded = scene.isLoaded,
                isDirty = scene.isDirty,
                isActive = isActive,
                rootCount = scene.isLoaded ? scene.rootCount : 0,
                buildIndex = buildIndex,
                hasBuildIndex = buildIndex >= 0,
            };
        }

        private static GameObject FindGameObjectByHierarchyPath(Scene scene, string hierarchyPath)
        {
            if (string.IsNullOrEmpty(hierarchyPath))
            {
                return null;
            }

            string[] parts = hierarchyPath.Split('/');
            if (parts.Length == 0)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (!string.Equals(root.name, parts[0], StringComparison.Ordinal))
                {
                    continue;
                }

                Transform current = root.transform;
                bool found = true;
                for (int index = 1; index < parts.Length; index++)
                {
                    string part = parts[index];
                    Transform next = null;
                    for (int child = 0; child < current.childCount; child++)
                    {
                        Transform childTransform = current.GetChild(child);
                        if (string.Equals(childTransform.name, part, StringComparison.Ordinal))
                        {
                            next = childTransform;
                            break;
                        }
                    }

                    if (next == null)
                    {
                        found = false;
                        break;
                    }

                    current = next;
                }

                if (found)
                {
                    return current.gameObject;
                }
            }

            return null;
        }

        private static string[] GetLoadedScenePaths()
        {
            var scenes = new List<string>();
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                scenes.Add(scene.path);
            }

            return scenes.ToArray();
        }

        private static OpenSceneMode ParseOpenSceneMode(string mode)
        {
            if (string.Equals(mode, "Additive", StringComparison.OrdinalIgnoreCase))
            {
                return OpenSceneMode.Additive;
            }

            if (string.Equals(mode, "AdditiveWithoutLoading", StringComparison.OrdinalIgnoreCase))
            {
                return OpenSceneMode.AdditiveWithoutLoading;
            }

            return OpenSceneMode.Single;
        }
    }
}

