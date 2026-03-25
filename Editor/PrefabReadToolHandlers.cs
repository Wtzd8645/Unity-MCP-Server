using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Control.Editor
{
    internal static class PrefabReadToolHandlers
    {
        public static ControlToolCallResponse HandlePrefabGet(ControlToolCallRequest request)
        {
            PrefabGetArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new PrefabGetArgs());

            bool hasPrefab = HasAssetRef(args.prefab);
            bool hasInstance = HasGameObjectRef(args.instance);
            if (hasPrefab == hasInstance)
            {
                return ControlResponses.Error(
                    "Provide exactly one of prefab or instance.",
                    "invalid_argument",
                    request.name);
            }

            if (hasPrefab)
            {
                if (!TryResolvePrefabAsset(args.prefab, request.name, out GameObject prefabAsset, out ControlToolCallResponse errorResponse))
                {
                    return errorResponse;
                }

                var payload = new PrefabGetResult
                {
                    targetKind = "prefab",
                    prefab = BuildPrefabAssetInfo(prefabAsset),
                    sourcePrefab = BuildSourcePrefabInfo(prefabAsset),
                    overrides = BuildPrefabOverrideSummary(null),
                };

                return ControlResponses.Success("unity_prefab_get completed.", payload);
            }

            if (!TryResolvePrefabInstanceRoot(args.instance, request.name, out GameObject instanceRoot, out GameObject instancePrefabAsset, out ControlToolCallResponse instanceError))
            {
                return instanceError;
            }

            var instancePayload = new PrefabGetResult
            {
                targetKind = "instance",
                prefab = BuildPrefabAssetInfo(instancePrefabAsset),
                instance = BuildPrefabInstanceInfo(instanceRoot),
                sourcePrefab = BuildSourcePrefabInfo(instancePrefabAsset),
                overrides = BuildPrefabOverrideSummary(instanceRoot),
            };

            return ControlResponses.Success("unity_prefab_get completed.", instancePayload);
        }

        public static ControlToolCallResponse HandlePrefabGetOverrides(ControlToolCallRequest request)
        {
            PrefabGetOverridesArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new PrefabGetOverridesArgs());

            if (!HasGameObjectRef(args.instance))
            {
                return ControlResponses.Error("instance is required.", "invalid_argument", request.name);
            }

            if (!TryResolvePrefabInstanceRoot(args.instance, request.name, out GameObject instanceRoot, out GameObject prefabAsset, out ControlToolCallResponse errorResponse))
            {
                return errorResponse;
            }

            var payload = new PrefabGetOverridesResult
            {
                prefab = BuildPrefabAssetInfo(prefabAsset),
                instance = BuildPrefabInstanceInfo(instanceRoot),
                propertyOverrides = BuildPropertyOverrideItems(instanceRoot),
                addedComponents = BuildAddedComponentItems(instanceRoot),
                removedComponents = BuildRemovedComponentItems(instanceRoot),
                addedGameObjects = BuildAddedGameObjectItems(instanceRoot),
                removedGameObjects = BuildRemovedGameObjectItems(instanceRoot),
            };

            return ControlResponses.Success("unity_prefab_get_overrides completed.", payload);
        }

        private static bool TryResolvePrefabAsset(
            AssetRef prefabRef,
            string toolName,
            out GameObject prefabAsset,
            out ControlToolCallResponse errorResponse)
        {
            prefabAsset = null;
            errorResponse = null;

            if (!ControlWriteSupport.TryResolveAssetRef(prefabRef, out string prefabPath, out _))
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

        private static bool TryResolvePrefabInstanceRoot(
            GameObjectRef instanceRef,
            string toolName,
            out GameObject instanceRoot,
            out GameObject prefabAsset,
            out ControlToolCallResponse errorResponse)
        {
            instanceRoot = null;
            prefabAsset = null;
            errorResponse = null;

            if (!ControlWriteSupport.TryResolveGameObject(instanceRef, out GameObject resolved, out string resolveError))
            {
                errorResponse = ControlResponses.Error(resolveError, "not_found", toolName);
                return false;
            }

            instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(resolved);
            if (instanceRoot == null || !PrefabUtility.IsPartOfPrefabInstance(instanceRoot))
            {
                errorResponse = ControlResponses.Error("Target is not part of a prefab instance.", "invalid_argument", toolName);
                return false;
            }

            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            if (string.IsNullOrEmpty(prefabPath))
            {
                errorResponse = ControlResponses.Error("Prefab source asset could not be resolved.", "not_found", toolName);
                return false;
            }

            prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null || !PrefabUtility.IsPartOfPrefabAsset(prefabAsset))
            {
                errorResponse = ControlResponses.Error("Prefab source asset not found: " + prefabPath, "not_found", toolName);
                return false;
            }

            return true;
        }

        private static PrefabAssetInfo BuildPrefabAssetInfo(GameObject prefabAsset)
        {
            if (prefabAsset == null)
            {
                return null;
            }

            string path = AssetDatabase.GetAssetPath(prefabAsset);
            return new PrefabAssetInfo
            {
                guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path),
                path = path,
                name = prefabAsset.name,
                assetType = PrefabUtility.GetPrefabAssetType(prefabAsset).ToString(),
            };
        }

        private static PrefabAssetInfo BuildSourcePrefabInfo(GameObject prefabAsset)
        {
            if (prefabAsset == null)
            {
                return null;
            }

            GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(prefabAsset);
            if (sourcePrefab == null)
            {
                return null;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
            if (string.IsNullOrEmpty(sourcePath) ||
                string.Equals(prefabPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return BuildPrefabAssetInfo(sourcePrefab);
        }

        private static PrefabInstanceInfo BuildPrefabInstanceInfo(GameObject instanceRoot)
        {
            if (instanceRoot == null)
            {
                return null;
            }

            return new PrefabInstanceInfo
            {
                globalObjectId = TryGetGlobalObjectId(instanceRoot),
                scenePath = instanceRoot.scene.path,
                hierarchyPath = BuildHierarchyPath(instanceRoot.transform),
                name = instanceRoot.name,
                status = PrefabUtility.GetPrefabInstanceStatus(instanceRoot).ToString(),
            };
        }

        private static PrefabOverrideSummary BuildPrefabOverrideSummary(GameObject instanceRoot)
        {
            return new PrefabOverrideSummary
            {
                propertyOverrideCount = instanceRoot == null ? 0 : GetPropertyModifications(instanceRoot).Length,
                addedComponentCount = instanceRoot == null ? 0 : InvokePrefabUtilityList("GetAddedComponents", instanceRoot).Length,
                removedComponentCount = instanceRoot == null ? 0 : InvokePrefabUtilityList("GetRemovedComponents", instanceRoot).Length,
                addedGameObjectCount = instanceRoot == null ? 0 : InvokePrefabUtilityList("GetAddedGameObjects", instanceRoot).Length,
                removedGameObjectCount = instanceRoot == null ? 0 : InvokePrefabUtilityList("GetRemovedGameObjects", instanceRoot).Length,
            };
        }

        private static PrefabPropertyOverrideItem[] BuildPropertyOverrideItems(GameObject instanceRoot)
        {
            PropertyModification[] modifications = GetPropertyModifications(instanceRoot);
            var items = new List<PrefabPropertyOverrideItem>(modifications.Length);
            for (int i = 0; i < modifications.Length; i++)
            {
                PropertyModification modification = modifications[i];
                UnityEngine.Object target = modification.target;
                items.Add(new PrefabPropertyOverrideItem
                {
                    targetGlobalObjectId = TryGetGlobalObjectId(target),
                    targetName = GetObjectDisplayName(target),
                    targetType = target == null ? null : (target.GetType().FullName ?? target.GetType().Name),
                    targetHierarchyPath = BuildHierarchyPath(target),
                    propertyPath = modification.propertyPath,
                    value = FormatPropertyModificationValue(modification),
                });
            }

            return items.ToArray();
        }

        private static PrefabAddedComponentItem[] BuildAddedComponentItems(GameObject instanceRoot)
        {
            object[] rawItems = InvokePrefabUtilityList("GetAddedComponents", instanceRoot);
            var items = new List<PrefabAddedComponentItem>(rawItems.Length);
            for (int i = 0; i < rawItems.Length; i++)
            {
                object rawItem = rawItems[i];
                Component component = GetFirstMemberValue<Component>(rawItem, "instanceComponent", "component");
                GameObject gameObject = component == null
                    ? GetFirstMemberValue<GameObject>(rawItem, "instanceGameObject", "gameObject")
                    : component.gameObject;

                items.Add(new PrefabAddedComponentItem
                {
                    componentId = TryGetGlobalObjectId(component),
                    componentType = component == null ? null : (component.GetType().FullName ?? component.GetType().Name),
                    gameObjectGlobalObjectId = TryGetGlobalObjectId(gameObject),
                    gameObjectName = gameObject == null ? null : gameObject.name,
                    hierarchyPath = BuildHierarchyPath(gameObject),
                });
            }

            return items.ToArray();
        }

        private static PrefabRemovedComponentItem[] BuildRemovedComponentItems(GameObject instanceRoot)
        {
            object[] rawItems = InvokePrefabUtilityList("GetRemovedComponents", instanceRoot);
            var items = new List<PrefabRemovedComponentItem>(rawItems.Length);
            for (int i = 0; i < rawItems.Length; i++)
            {
                object rawItem = rawItems[i];
                Component component = GetFirstMemberValue<Component>(rawItem, "assetComponent", "component");
                GameObject gameObject = component == null
                    ? GetFirstMemberValue<GameObject>(rawItem, "assetGameObject", "gameObject")
                    : component.gameObject;

                items.Add(new PrefabRemovedComponentItem
                {
                    componentId = TryGetGlobalObjectId(component),
                    componentType = component == null ? null : (component.GetType().FullName ?? component.GetType().Name),
                    gameObjectGlobalObjectId = TryGetGlobalObjectId(gameObject),
                    gameObjectName = gameObject == null ? null : gameObject.name,
                    hierarchyPath = BuildHierarchyPath(gameObject),
                });
            }

            return items.ToArray();
        }

        private static PrefabAddedGameObjectItem[] BuildAddedGameObjectItems(GameObject instanceRoot)
        {
            object[] rawItems = InvokePrefabUtilityList("GetAddedGameObjects", instanceRoot);
            var items = new List<PrefabAddedGameObjectItem>(rawItems.Length);
            for (int i = 0; i < rawItems.Length; i++)
            {
                GameObject gameObject = GetFirstMemberValue<GameObject>(rawItems[i], "instanceGameObject", "gameObject");
                items.Add(new PrefabAddedGameObjectItem
                {
                    globalObjectId = TryGetGlobalObjectId(gameObject),
                    name = gameObject == null ? null : gameObject.name,
                    hierarchyPath = BuildHierarchyPath(gameObject),
                });
            }

            return items.ToArray();
        }

        private static PrefabRemovedGameObjectItem[] BuildRemovedGameObjectItems(GameObject instanceRoot)
        {
            object[] rawItems = InvokePrefabUtilityList("GetRemovedGameObjects", instanceRoot);
            var items = new List<PrefabRemovedGameObjectItem>(rawItems.Length);
            for (int i = 0; i < rawItems.Length; i++)
            {
                GameObject gameObject = GetFirstMemberValue<GameObject>(rawItems[i], "assetGameObject", "gameObject");
                items.Add(new PrefabRemovedGameObjectItem
                {
                    globalObjectId = TryGetGlobalObjectId(gameObject),
                    name = gameObject == null ? null : gameObject.name,
                    hierarchyPath = BuildHierarchyPath(gameObject),
                });
            }

            return items.ToArray();
        }

        private static PropertyModification[] GetPropertyModifications(GameObject instanceRoot)
        {
            if (instanceRoot == null)
            {
                return Array.Empty<PropertyModification>();
            }

            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instanceRoot);
            return modifications ?? Array.Empty<PropertyModification>();
        }

        private static object[] InvokePrefabUtilityList(string methodName, GameObject instanceRoot)
        {
            if (instanceRoot == null)
            {
                return Array.Empty<object>();
            }

            MethodInfo method = typeof(PrefabUtility).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(GameObject) },
                null);
            if (method == null)
            {
                return Array.Empty<object>();
            }

            object value;
            try
            {
                value = method.Invoke(null, new object[] { instanceRoot });
            }
            catch
            {
                return Array.Empty<object>();
            }

            if (value is IEnumerable enumerable)
            {
                var items = new List<object>();
                foreach (object item in enumerable)
                {
                    items.Add(item);
                }

                return items.ToArray();
            }

            return Array.Empty<object>();
        }

        private static T GetFirstMemberValue<T>(object source, params string[] memberNames) where T : class
        {
            if (source == null || memberNames == null)
            {
                return null;
            }

            for (int i = 0; i < memberNames.Length; i++)
            {
                if (TryGetMemberValue(source, memberNames[i], out T value))
                {
                    return value;
                }
            }

            return null;
        }

        private static bool TryGetMemberValue<T>(object source, string memberName, out T value) where T : class
        {
            value = null;
            if (source == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            Type sourceType = source.GetType();
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo property = sourceType.GetProperty(memberName, Flags);
            if (property != null)
            {
                object propertyValue = property.GetValue(source, null);
                if (propertyValue is T typedPropertyValue)
                {
                    value = typedPropertyValue;
                    return true;
                }
            }

            FieldInfo field = sourceType.GetField(memberName, Flags);
            if (field != null)
            {
                object fieldValue = field.GetValue(source);
                if (fieldValue is T typedFieldValue)
                {
                    value = typedFieldValue;
                    return true;
                }
            }

            return false;
        }

        private static string FormatPropertyModificationValue(PropertyModification modification)
        {
            if (modification == null)
            {
                return null;
            }

            if (modification.objectReference != null)
            {
                return FormatObjectReference(modification.objectReference);
            }

            return modification.value;
        }

        private static string FormatObjectReference(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return null;
            }

            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                return assetPath;
            }

            string globalObjectId = TryGetGlobalObjectId(obj);
            if (!string.IsNullOrEmpty(globalObjectId))
            {
                return globalObjectId;
            }

            return obj.name + " (" + obj.GetType().Name + ")";
        }

        private static string GetObjectDisplayName(UnityEngine.Object obj)
        {
            switch (obj)
            {
                case GameObject gameObject:
                    return gameObject.name;
                case Component component:
                    return component.gameObject.name;
                default:
                    return obj == null ? null : obj.name;
            }
        }

        private static string TryGetGlobalObjectId(UnityEngine.Object obj)
        {
            return obj == null ? null : GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        }

        private static string BuildHierarchyPath(UnityEngine.Object obj)
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

        private static string BuildHierarchyPath(Transform transform)
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

        private static bool HasAssetRef(AssetRef assetRef)
        {
            return assetRef != null &&
                   (!string.IsNullOrEmpty(assetRef.guid) || !string.IsNullOrEmpty(assetRef.path));
        }

        private static bool HasGameObjectRef(GameObjectRef gameObjectRef)
        {
            return gameObjectRef != null &&
                   (!string.IsNullOrEmpty(gameObjectRef.globalObjectId) ||
                    (!string.IsNullOrEmpty(gameObjectRef.scenePath) && !string.IsNullOrEmpty(gameObjectRef.hierarchyPath)));
        }
    }
}
