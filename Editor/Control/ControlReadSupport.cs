using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Editor.Control
{
    internal static class ControlReadSupport
    {
        private static readonly BindingFlags EnabledPropertyFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static GameObjectGetResult BuildGameObjectGetResult(GameObject gameObject, bool includeChildren, int childLimit)
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
                parent = transform.parent == null ? null : BuildGameObjectRelationItem(transform.parent.gameObject),
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
                    children.Add(BuildGameObjectRelationItem(transform.GetChild(i).gameObject));
                }

                result.children = children.ToArray();
                result.childrenTruncated = childCount > takeCount;
            }

            return result;
        }

        public static PrefabAssetGameObjectGetResult BuildPrefabGameObjectGetResult(
            string prefabPath,
            GameObject gameObject,
            bool includeChildren,
            int childLimit)
        {
            Transform transform = gameObject.transform;
            var result = new PrefabAssetGameObjectGetResult
            {
                prefabPath = prefabPath,
                hierarchyPath = BuildHierarchyPath(transform),
                name = gameObject.name,
                activeSelf = gameObject.activeSelf,
                tag = gameObject.tag,
                layer = gameObject.layer,
                isStatic = gameObject.isStatic,
                parent = transform.parent == null ? null : BuildPrefabGameObjectRelationItem(transform.parent.gameObject),
                children = Array.Empty<PrefabGameObjectRelationItem>(),
                childrenTruncated = false,
                components = BuildPrefabComponentSummaries(gameObject).ToArray(),
                transform = BuildPrefabLocalTransformSnapshot(transform),
            };

            if (includeChildren)
            {
                int childCount = transform.childCount;
                int takeCount = Math.Min(childCount, childLimit);
                var children = new List<PrefabGameObjectRelationItem>(takeCount);
                for (int i = 0; i < takeCount; i++)
                {
                    children.Add(BuildPrefabGameObjectRelationItem(transform.GetChild(i).gameObject));
                }

                result.children = children.ToArray();
                result.childrenTruncated = childCount > takeCount;
            }

            return result;
        }

        public static GameObjectRelationItem BuildGameObjectRelationItem(GameObject gameObject)
        {
            return new GameObjectRelationItem
            {
                globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString(),
                hierarchyPath = BuildHierarchyPath(gameObject.transform),
                name = gameObject.name,
                activeSelf = gameObject.activeSelf,
            };
        }

        public static PrefabGameObjectRelationItem BuildPrefabGameObjectRelationItem(GameObject gameObject)
        {
            return new PrefabGameObjectRelationItem
            {
                hierarchyPath = BuildHierarchyPath(gameObject.transform),
                name = gameObject.name,
                activeSelf = gameObject.activeSelf,
            };
        }

        public static string[] GetComponentTypeNames(GameObject gameObject)
        {
            Component[] components = gameObject.GetComponents<Component>();
            var componentTypes = new List<string>(components.Length);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                componentTypes.Add(component.GetType().FullName ?? component.GetType().Name);
            }

            return componentTypes.ToArray();
        }

        public static bool HasComponentType(GameObject gameObject, string typeName)
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

        public static List<ComponentSummaryItem> BuildComponentSummaries(GameObject gameObject)
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

        public static List<PrefabComponentSummaryItem> BuildPrefabComponentSummaries(GameObject gameObject)
        {
            var items = new List<PrefabComponentSummaryItem>();
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                PrefabComponentSummaryItem item = BuildPrefabComponentSummary(components[i], i);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        public static bool TryResolveComponentByIndex(GameObject gameObject, int componentIndex, out Component component, out string error)
        {
            component = null;
            error = null;

            if (gameObject == null)
            {
                error = "Target GameObject is required.";
                return false;
            }

            if (componentIndex < 0)
            {
                error = "componentIndex must be >= 0.";
                return false;
            }

            Component[] components = gameObject.GetComponents<Component>();
            if (componentIndex >= components.Length)
            {
                error = "componentIndex is out of range.";
                return false;
            }

            component = components[componentIndex];
            if (component == null)
            {
                error = "Component not found at componentIndex.";
                return false;
            }

            return true;
        }

        public static List<ComponentFieldItem> ExtractSerializedComponentFields(Component component, bool includePrivateSerialized)
        {
            var result = new List<ComponentFieldItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            Type currentType = component.GetType();
            while (currentType != null && currentType != typeof(object))
            {
                FieldInfo[] fields = currentType.GetFields(FieldFlags);
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

        public static GameObjectTransformSnapshot BuildTransformSnapshot(Transform transform)
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

        public static PrefabLocalTransformSnapshot BuildPrefabLocalTransformSnapshot(Transform transform)
        {
            return new PrefabLocalTransformSnapshot
            {
                localPosition = BuildVec3Value(transform.localPosition),
                localRotationEuler = BuildVec3Value(transform.localEulerAngles),
                localScale = BuildVec3Value(transform.localScale),
            };
        }

        public static string BuildHierarchyPath(UnityEngine.Object obj)
        {
            switch (obj)
            {
                case GameObject gameObject:
                    return BuildHierarchyPath(gameObject.transform);
                case Component component:
                    return BuildHierarchyPath(component.transform);
                default:
                    return null;
            }
        }

        public static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return null;
            }

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

        public static bool TryResolvePrefabAsset(
            AssetRef prefabRef,
            string toolName,
            out string prefabPath,
            out GameObject prefabAsset,
            out ControlToolCallResponse errorResponse)
        {
            prefabPath = null;
            prefabAsset = null;
            errorResponse = null;

            if (!ControlWriteSupport.TryResolveAssetRef(prefabRef, out prefabPath, out _))
            {
                errorResponse = ControlResponses.Error("Prefab target not found.", "not_found", toolName);
                return false;
            }

            prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null || !PrefabUtility.IsPartOfPrefabAsset(prefabAsset))
            {
                errorResponse = ControlResponses.Error("Target is not a prefab asset: " + prefabPath, "invalid_argument", toolName);
                return false;
            }

            return true;
        }

        public static bool TryLoadPrefabContents(
            AssetRef prefabRef,
            string toolName,
            out string prefabPath,
            out GameObject prefabRoot,
            out ControlToolCallResponse errorResponse)
        {
            prefabPath = null;
            prefabRoot = null;
            errorResponse = null;

            if (!TryResolvePrefabAsset(prefabRef, toolName, out prefabPath, out _, out errorResponse))
            {
                return false;
            }

            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (prefabRoot == null)
                {
                    errorResponse = ControlResponses.Error("Prefab contents could not be loaded.", "tool_exception", toolName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorResponse = ControlResponses.Error("Failed to load prefab contents: " + ex.Message, "tool_exception", toolName);
                return false;
            }

            return true;
        }

        public static bool TryResolvePrefabContentGameObject(
            GameObject prefabRoot,
            string hierarchyPath,
            out GameObject gameObject,
            out string error)
        {
            gameObject = null;
            error = null;

            if (prefabRoot == null)
            {
                error = "Prefab contents are not loaded.";
                return false;
            }

            if (string.IsNullOrEmpty(hierarchyPath))
            {
                error = "hierarchyPath is required.";
                return false;
            }

            string[] parts = hierarchyPath.Split('/');
            if (parts.Length == 0 || !string.Equals(prefabRoot.name, parts[0], StringComparison.Ordinal))
            {
                error = "GameObject not found by hierarchyPath.";
                return false;
            }

            Transform current = prefabRoot.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                Transform next = null;
                for (int childIndex = 0; childIndex < current.childCount; childIndex++)
                {
                    Transform child = current.GetChild(childIndex);
                    if (string.Equals(child.name, part, StringComparison.Ordinal))
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                {
                    error = "GameObject not found by hierarchyPath.";
                    return false;
                }

                current = next;
            }

            gameObject = current.gameObject;
            return true;
        }

        public static void CollectGameObjectsDepthFirst(GameObject root, List<GameObject> output)
        {
            if (root == null || output == null)
            {
                return;
            }

            output.Add(root);
            Transform transform = root.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                CollectGameObjectsDepthFirst(transform.GetChild(i).gameObject, output);
            }
        }

        public static string TryGetGlobalObjectId(UnityEngine.Object obj)
        {
            return obj == null ? null : GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
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

        private static PrefabComponentSummaryItem BuildPrefabComponentSummary(Component component, int index)
        {
            if (component == null)
            {
                return null;
            }

            bool hasEnabled = TryGetComponentEnabled(component, out bool enabled);
            return new PrefabComponentSummaryItem
            {
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

            PropertyInfo property = component.GetType().GetProperty("enabled", EnabledPropertyFlags);
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

        private static Vec3Value BuildVec3Value(Vector3 value)
        {
            return new Vec3Value
            {
                x = value.x,
                y = value.y,
                z = value.z,
            };
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

            if (value is string stringValue)
            {
                return stringValue;
            }

            if (value is bool || value is int || value is float || value is double || value is long)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return value.ToString();
        }
    }
}
