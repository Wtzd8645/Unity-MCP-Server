using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal static class SceneReadToolHandlers
    {
        public static BridgeToolCallResponse HandleListScenes(BridgeToolCallRequest request)
        {
            ListScenesArgs args = BridgeJson.ParseArgs(
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

            PaginationRange range = BridgeUtil.BuildPaginationRange(all.Count, args.offset, args.limit, 500);
            List<SceneListItem> page = all.Skip(range.offset).Take(range.limit).ToList();

            var payload = new ListScenesResult
            {
                total = all.Count,
                items = page.ToArray(),
            };

            return BridgeResponses.Success("unity_list_scenes completed.", payload);
        }

        public static BridgeToolCallResponse HandleOpenScene(BridgeToolCallRequest request)
        {
            OpenSceneArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new OpenSceneArgs
                {
                    openMode = "Single",
                    saveModifiedScenes = false,
                    setActive = true,
                });

            if (string.IsNullOrEmpty(args.scenePath))
            {
                return BridgeResponses.Error("scenePath is required.", "invalid_argument", request.name);
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(args.scenePath) == null)
            {
                return BridgeResponses.Error("Scene not found: " + args.scenePath, "not_found", request.name);
            }

            if (args.saveModifiedScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return BridgeResponses.Error("Open scene cancelled by user.", "cancelled", request.name);
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

            return BridgeResponses.Success("unity_open_scene completed.", payload);
        }

        public static BridgeToolCallResponse HandleGoFind(BridgeToolCallRequest request)
        {
            GoFindArgs args = BridgeJson.ParseArgs(
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
                return BridgeResponses.Error("Scene is not loaded. Use unity_open_scene first.", "not_found", request.name);
            }

            bool filterIsActive = BridgeJson.RawJsonContainsProperty(request.argumentsJson, "isActive");
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
            PaginationRange range = BridgeUtil.BuildPaginationRange(total, args.offset, args.limit, 1000);
            List<GoFindItem> page = matches.Skip(range.offset).Take(range.limit).ToList();

            var payload = new GoFindResult
            {
                total = total,
                items = page.ToArray(),
            };

            return BridgeResponses.Success("unity_go_find completed.", payload);
        }

        public static BridgeToolCallResponse HandleComponentGetFields(BridgeToolCallRequest request)
        {
            ComponentGetFieldsArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new ComponentGetFieldsArgs
                {
                    includePrivateSerialized = false,
                });

            if (args.target == null)
            {
                return BridgeResponses.Error("target is required.", "invalid_argument", request.name);
            }

            string resolveError;
            GameObject gameObject;
            if (!TryResolveGameObject(args.target, out gameObject, out resolveError))
            {
                return BridgeResponses.Error(resolveError, "not_found", request.name);
            }

            Component component;
            if (!TryResolveComponent(gameObject, args.componentType, args.componentId, out component))
            {
                return BridgeResponses.Error("Component not found on target GameObject.", "not_found", request.name);
            }

            string componentGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            List<ComponentFieldItem> fields = ExtractComponentFields(component, args.includePrivateSerialized);

            var payload = new ComponentGetFieldsResult
            {
                componentId = componentGlobalId,
                componentType = component.GetType().FullName ?? component.GetType().Name,
                fields = fields.ToArray(),
            };

            return BridgeResponses.Success("unity_component_get_fields completed.", payload);
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

