using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal sealed class GoComponentSpec
    {
        public string type;
        public Dictionary<string, object> fields;
    }

    internal static class BridgeWriteSupport
    {
        private static readonly BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string AllowedPathPrefixesEnv = "UNITY_MCP_ALLOWED_PATH_PREFIXES";
        private const string AllowedComponentTypesEnv = "UNITY_MCP_ALLOWED_COMPONENT_TYPES";


        public static bool TryResolveApplyMode(
            bool dryRun,
            bool apply,
            string toolName,
            out bool shouldApply,
            out BridgeToolCallResponse errorResponse)
        {
            shouldApply = false;
            errorResponse = null;

            if (dryRun == apply)
            {
                errorResponse = BridgeResponses.Error(
                    "dryRun/apply must be inverse boolean values.",
                    "invalid_argument",
                    toolName);
                return false;
            }

            shouldApply = apply;
            return true;
        }

        public static BridgeToolCallResponse BuildMutationResponse(
            string tool,
            bool shouldApply,
            int requested,
            List<MutationItem> items,
            List<string> warnings = null)
        {
            items = items ?? new List<MutationItem>();
            int failed = items.Count(item => string.Equals(item.status, "failed", StringComparison.OrdinalIgnoreCase));
            int succeeded = items.Count - failed;

            var payload = new MutationResult
            {
                tool = tool,
                dryRun = !shouldApply,
                applied = shouldApply,
                requested = requested,
                succeeded = succeeded,
                failed = failed,
                items = items.ToArray(),
                warnings = warnings == null ? null : warnings.ToArray(),
            };

            string state = shouldApply ? "completed" : "dry-run";
            string text = tool + " " + state + " (requested=" + requested + ", succeeded=" + succeeded + ", failed=" + failed + ").";
            return BridgeResponses.Success(text, payload);
        }

        public static MutationItem BuildGoMutationItem(GameObject gameObject, string action)
        {
            return new MutationItem
            {
                target = gameObject == null ? null : gameObject.name,
                action = action,
                status = "planned",
                changed = false,
                globalObjectId = gameObject == null ? null : GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString(),
                scenePath = gameObject == null ? null : gameObject.scene.path,
                hierarchyPath = gameObject == null ? null : BuildHierarchyPath(gameObject.transform),
            };
        }

        public static MutationItem BuildAssetMutationItem(string path, string action)
        {
            return new MutationItem
            {
                target = path,
                action = action,
                status = "planned",
                changed = false,
                path = path,
                guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path),
            };
        }

        public static bool TryValidateComponentTypeAllowed(Type componentType, out string error)
        {
            error = null;
            if (componentType == null)
            {
                error = "Component type is required.";
                return false;
            }

            string[] patterns = GetAllowedComponentPatterns();
            string fullName = componentType.FullName ?? componentType.Name;
            for (int i = 0; i < patterns.Length; i++)
            {
                string pattern = patterns[i];
                if (string.Equals(pattern, "*", StringComparison.Ordinal))
                {
                    return true;
                }

                if (IsPatternMatch(pattern, fullName) || IsPatternMatch(pattern, componentType.Name))
                {
                    return true;
                }
            }

            error = "Component type is not allowed: " + fullName;
            return false;
        }

        public static bool TryValidateAssetPathAllowed(string path, out string error)
        {
            if (!TryNormalizeAssetPath(path, out string normalized, out error))
            {
                return false;
            }

            string[] prefixes = GetAllowedPathPrefixes();
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];
                string prefixRoot = prefix.TrimEnd('/');
                if (string.Equals(normalized, prefixRoot, StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            error = "Path is outside allowed prefixes: " + normalized;
            return false;
        }

        public static bool TryValidateFolderPath(string folderPath, out string error)
        {
            if (!TryNormalizeAssetPath(folderPath, out string normalizedFolder, out error))
            {
                return false;
            }

            if (!TryValidateAssetPathAllowed(normalizedFolder, out error))
            {
                return false;
            }

            if (!string.Equals(normalizedFolder, "Assets", StringComparison.OrdinalIgnoreCase) &&
                !normalizedFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Folder path must start with Assets/.";
                return false;
            }

            error = null;
            return true;
        }

        private static string[] GetAllowedPathPrefixes()
        {
            string raw = Environment.GetEnvironmentVariable(AllowedPathPrefixesEnv);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new[] { "Assets/" };
            }

            string[] prefixes = raw
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim().Replace('\\', '/'))
                .Where(item => !string.IsNullOrEmpty(item))
                .Select(item => item.EndsWith("/", StringComparison.Ordinal) ? item : item + "/")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return prefixes.Length == 0 ? new[] { "Assets/" } : prefixes;
        }

        private static string[] GetAllowedComponentPatterns()
        {
            string raw = Environment.GetEnvironmentVariable(AllowedComponentTypesEnv);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new[] { "*" };
            }

            string[] patterns = raw
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrEmpty(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return patterns.Length == 0 ? new[] { "*" } : patterns;
        }

        private static bool IsPatternMatch(string pattern, string value)
        {
            if (string.Equals(pattern, "*", StringComparison.Ordinal))
            {
                return true;
            }

            if (pattern.EndsWith("*", StringComparison.Ordinal))
            {
                string prefix = pattern.Substring(0, pattern.Length - 1);
                return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryResolveGameObject(GameObjectRef target, out GameObject gameObject, out string error)
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

        public static bool TryResolveComponent(GameObject gameObject, string componentType, string componentId, out Component component)
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

        public static Type FindTypeByName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            Type direct = Type.GetType(typeName, false);
            if (direct != null)
            {
                return direct;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                for (int j = 0; j < types.Length; j++)
                {
                    Type candidate = types[j];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (string.Equals(candidate.FullName, typeName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.Name, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        public static void ApplyTransform(Transform transform, TransformInput input)
        {
            if (transform == null || input == null)
            {
                return;
            }

            bool local = input.local;
            if (input.position != null)
            {
                var position = new Vector3(input.position.x, input.position.y, input.position.z);
                if (local)
                {
                    transform.localPosition = position;
                }
                else
                {
                    transform.position = position;
                }
            }

            if (input.rotationEuler != null)
            {
                var rotation = Quaternion.Euler(input.rotationEuler.x, input.rotationEuler.y, input.rotationEuler.z);
                if (local)
                {
                    transform.localRotation = rotation;
                }
                else
                {
                    transform.rotation = rotation;
                }
            }

            if (input.scale != null)
            {
                transform.localScale = new Vector3(input.scale.x, input.scale.y, input.scale.z);
            }
        }

        public static string BuildRename(string pattern, string sourceName, int index)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return sourceName + " Copy";
            }

            return pattern
                .Replace("{name}", sourceName ?? string.Empty)
                .Replace("{index}", index.ToString(CultureInfo.InvariantCulture));
        }

        public static bool TryResolveAssetRef(AssetRef target, out string path, out string guid)
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
                path = target.path;
                guid = AssetDatabase.AssetPathToGUID(path);
            }

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (!TryValidateAssetPathAllowed(path, out _))
            {
                return false;
            }

            return true;
        }
        public static bool TryApplyComponentFields(
            Component component,
            Dictionary<string, object> fields,
            bool strict,
            out int appliedCount,
            out string error)
        {
            appliedCount = 0;
            error = null;

            if (component == null)
            {
                error = "Component is null.";
                return false;
            }

            if (fields == null || fields.Count == 0)
            {
                return true;
            }

            var assignments = new List<KeyValuePair<FieldInfo, object>>();
            foreach (KeyValuePair<string, object> entry in fields)
            {
                FieldInfo field = FindField(component.GetType(), entry.Key);
                if (field == null)
                {
                    if (strict)
                    {
                        error = "Field not found: " + entry.Key;
                        return false;
                    }

                    continue;
                }

                if (field.IsStatic || field.IsLiteral || field.IsInitOnly)
                {
                    if (strict)
                    {
                        error = "Field is not writable: " + entry.Key;
                        return false;
                    }

                    continue;
                }

                bool hasSerializeField = field.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
                bool serialized = field.IsPublic || hasSerializeField;
                if (!serialized)
                {
                    if (strict)
                    {
                        error = "Field is not serialized: " + entry.Key;
                        return false;
                    }

                    continue;
                }

                object converted;
                if (!TryConvertJsonValue(entry.Value, field.FieldType, out converted, out string convertError))
                {
                    if (strict)
                    {
                        error = "Failed to convert field '" + entry.Key + "': " + convertError;
                        return false;
                    }

                    continue;
                }

                assignments.Add(new KeyValuePair<FieldInfo, object>(field, converted));
            }

            for (int i = 0; i < assignments.Count; i++)
            {
                KeyValuePair<FieldInfo, object> assignment = assignments[i];
                assignment.Key.SetValue(component, assignment.Value);
                appliedCount++;
            }

            if (appliedCount > 0)
            {
                EditorUtility.SetDirty(component);
            }

            return true;
        }

        public static Dictionary<string, object> ExtractObject(string rawJson, string propertyName)
        {
            Dictionary<string, object> root = ParseRootObject(rawJson);
            if (root == null)
            {
                return null;
            }

            if (!root.TryGetValue(propertyName, out object value))
            {
                return null;
            }

            return value as Dictionary<string, object>;
        }

        public static List<GoComponentSpec> ExtractGoComponentSpecs(string rawJson)
        {
            var result = new List<GoComponentSpec>();
            Dictionary<string, object> root = ParseRootObject(rawJson);
            if (root == null || !root.TryGetValue("components", out object componentsObj))
            {
                return result;
            }

            var components = componentsObj as List<object>;
            if (components == null)
            {
                return result;
            }

            for (int i = 0; i < components.Count; i++)
            {
                var componentObj = components[i] as Dictionary<string, object>;
                if (componentObj == null)
                {
                    continue;
                }

                string type = componentObj.TryGetValue("type", out object typeObj)
                    ? typeObj as string
                    : null;
                Dictionary<string, object> fields = componentObj.TryGetValue("fields", out object fieldsObj)
                    ? fieldsObj as Dictionary<string, object>
                    : null;

                result.Add(new GoComponentSpec
                {
                    type = type,
                    fields = fields,
                });
            }

            return result;
        }

        public static bool TryGetSceneByPath(string scenePath, out Scene scene)
        {
            scene = default(Scene);
            if (string.IsNullOrEmpty(scenePath))
            {
                scene = SceneManager.GetActiveScene();
                return scene.IsValid();
            }

            if (!TryValidateAssetPathAllowed(scenePath, out _))
            {
                scene = default(Scene);
                return false;
            }

            scene = SceneManager.GetSceneByPath(scenePath);
            return scene.IsValid() && scene.isLoaded;
        }

        public static bool EnsureFolderPathExists(string folderPath, out string error)
        {
            if (!TryNormalizeAssetPath(folderPath, out string normalizedFolder, out error))
            {
                return false;
            }

            if (!TryValidateFolderPath(normalizedFolder, out error))
            {
                return false;
            }

            if (AssetDatabase.IsValidFolder(normalizedFolder))
            {
                return true;
            }

            string[] parts = normalizedFolder.Split('/');
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (AssetDatabase.IsValidFolder(next))
                {
                    current = next;
                    continue;
                }

                string guid = AssetDatabase.CreateFolder(current, parts[i]);
                if (string.IsNullOrEmpty(guid))
                {
                    error = "Failed to create folder: " + next;
                    return false;
                }

                current = next;
            }

            return true;
        }
        public static bool TryNormalizeAssetPath(string path, out string normalizedPath, out string error)
        {
            normalizedPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Asset path is required.";
                return false;
            }

            string candidate = path.Trim().Replace('\\', '/');
            while (candidate.StartsWith("./", StringComparison.Ordinal))
            {
                candidate = candidate.Substring(2);
            }

            if (Path.IsPathRooted(candidate) || candidate.StartsWith("/", StringComparison.Ordinal))
            {
                error = "Asset path must be project-relative.";
                return false;
            }

            string projectRoot;
            string absoluteCandidate;
            try
            {
                projectRoot = Path.GetFullPath(BridgeUtil.GetProjectRootPath());
                absoluteCandidate = Path.GetFullPath(Path.Combine(projectRoot, candidate.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex)
            {
                error = "Invalid asset path: " + ex.Message;
                return false;
            }

            if (!IsPathWithinRoot(projectRoot, absoluteCandidate))
            {
                error = "Path escapes project root: " + candidate;
                return false;
            }

            string normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string relative = absoluteCandidate.Substring(normalizedRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
            if (string.IsNullOrEmpty(relative))
            {
                error = "Asset path must resolve to a project-relative path.";
                return false;
            }

            normalizedPath = relative;
            return true;
        }

        private static bool IsPathWithinRoot(string rootPath, string candidatePath)
        {
            string normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = candidatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
        private static Dictionary<string, object> ParseRootObject(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson))
            {
                return null;
            }

            return BridgeMiniJson.Deserialize(rawJson) as Dictionary<string, object>;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            Type current = type;
            while (current != null && current != typeof(object))
            {
                FieldInfo field = current.GetField(name, FieldFlags);
                if (field != null)
                {
                    return field;
                }

                current = current.BaseType;
            }

            return null;
        }

        private static bool TryConvertJsonValue(object value, Type fieldType, out object converted, out string error)
        {
            converted = null;
            error = null;

            if (fieldType == typeof(string))
            {
                converted = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (value == null)
            {
                if (!fieldType.IsValueType || Nullable.GetUnderlyingType(fieldType) != null)
                {
                    converted = null;
                    return true;
                }

                error = "null is not valid for value type.";
                return false;
            }

            Type targetType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

            if (targetType.IsEnum)
            {
                if (value is string enumName)
                {
                    try
                    {
                        converted = Enum.Parse(targetType, enumName, true);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        return false;
                    }
                }

                if (TryConvertToLong(value, out long enumNumber))
                {
                    converted = Enum.ToObject(targetType, enumNumber);
                    return true;
                }

                error = "Invalid enum value.";
                return false;
            }

            if (targetType == typeof(bool))
            {
                if (value is bool boolValue)
                {
                    converted = boolValue;
                    return true;
                }

                if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bool parsedBool))
                {
                    converted = parsedBool;
                    return true;
                }

                error = "Invalid boolean value.";
                return false;
            }

            if (targetType == typeof(int))
            {
                if (TryConvertToLong(value, out long intValue))
                {
                    converted = (int)intValue;
                    return true;
                }

                error = "Invalid int value.";
                return false;
            }

            if (targetType == typeof(long))
            {
                if (TryConvertToLong(value, out long longValue))
                {
                    converted = longValue;
                    return true;
                }

                error = "Invalid long value.";
                return false;
            }

            if (targetType == typeof(float))
            {
                if (TryConvertToFloat(value, out float floatValue))
                {
                    converted = floatValue;
                    return true;
                }

                error = "Invalid float value.";
                return false;
            }

            if (targetType == typeof(double))
            {
                if (TryConvertToDouble(value, out double doubleValue))
                {
                    converted = doubleValue;
                    return true;
                }

                error = "Invalid double value.";
                return false;
            }

            if (targetType == typeof(Vector2))
            {
                if (TryReadVector2(value, out Vector2 vector2))
                {
                    converted = vector2;
                    return true;
                }

                error = "Invalid Vector2 value.";
                return false;
            }

            if (targetType == typeof(Vector3))
            {
                if (TryReadVector3(value, out Vector3 vector3))
                {
                    converted = vector3;
                    return true;
                }

                error = "Invalid Vector3 value.";
                return false;
            }

            if (targetType == typeof(Vector4))
            {
                if (TryReadVector4(value, out Vector4 vector4))
                {
                    converted = vector4;
                    return true;
                }

                error = "Invalid Vector4 value.";
                return false;
            }

            if (targetType == typeof(Color))
            {
                if (TryReadColor(value, out Color color))
                {
                    converted = color;
                    return true;
                }

                error = "Invalid Color value.";
                return false;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                if (TryReadUnityObject(value, targetType, out UnityEngine.Object unityObject, out string objectError))
                {
                    converted = unityObject;
                    return true;
                }

                error = objectError;
                return false;
            }

            if (targetType.IsAssignableFrom(value.GetType()))
            {
                converted = value;
                return true;
            }

            error = "Unsupported field type: " + targetType.FullName;
            return false;
        }

        private static bool TryReadUnityObject(object value, Type targetType, out UnityEngine.Object unityObject, out string error)
        {
            unityObject = null;
            error = null;

            if (value == null)
            {
                return true;
            }

            if (value is string text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return true;
                }

                if (text.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryValidateAssetPathAllowed(text, out error))
                    {
                        return false;
                    }
                    unityObject = AssetDatabase.LoadAssetAtPath(text, targetType);
                    if (unityObject == null)
                    {
                        error = "Asset reference not found: " + text;
                        return false;
                    }

                    return true;
                }
            }

            var map = value as Dictionary<string, object>;
            if (map != null)
            {
                string path = map.TryGetValue("path", out object pathObj) ? pathObj as string : null;
                string guid = map.TryGetValue("guid", out object guidObj) ? guidObj as string : null;
                if (!string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(path))
                {
                    path = AssetDatabase.GUIDToAssetPath(guid);
                }

                if (!string.IsNullOrEmpty(path))
                {
                    if (!TryValidateAssetPathAllowed(path, out error))
                    {
                        return false;
                    }
                    unityObject = AssetDatabase.LoadAssetAtPath(path, targetType);
                    if (unityObject == null)
                    {
                        error = "Asset reference not found: " + path;
                        return false;
                    }

                    return true;
                }
            }

            error = "Unsupported UnityEngine.Object reference format.";
            return false;
        }

        private static bool TryReadVector2(object value, out Vector2 vector)
        {
            vector = default(Vector2);
            var map = value as Dictionary<string, object>;
            if (map == null)
            {
                return false;
            }

            return TryReadNumber(map, "x", out float x) &&
                   TryReadNumber(map, "y", out float y) &&
                   SetVector2(out vector, x, y);
        }

        private static bool SetVector2(out Vector2 vector, float x, float y)
        {
            vector = new Vector2(x, y);
            return true;
        }

        private static bool TryReadVector3(object value, out Vector3 vector)
        {
            vector = default(Vector3);
            var map = value as Dictionary<string, object>;
            if (map == null)
            {
                return false;
            }

            if (!TryReadNumber(map, "x", out float x) ||
                !TryReadNumber(map, "y", out float y) ||
                !TryReadNumber(map, "z", out float z))
            {
                return false;
            }

            vector = new Vector3(x, y, z);
            return true;
        }

        private static bool TryReadVector4(object value, out Vector4 vector)
        {
            vector = default(Vector4);
            var map = value as Dictionary<string, object>;
            if (map == null)
            {
                return false;
            }

            if (!TryReadNumber(map, "x", out float x) ||
                !TryReadNumber(map, "y", out float y) ||
                !TryReadNumber(map, "z", out float z) ||
                !TryReadNumber(map, "w", out float w))
            {
                return false;
            }

            vector = new Vector4(x, y, z, w);
            return true;
        }

        private static bool TryReadColor(object value, out Color color)
        {
            color = default(Color);
            var map = value as Dictionary<string, object>;
            if (map == null)
            {
                return false;
            }

            if (!TryReadNumber(map, "r", out float r) ||
                !TryReadNumber(map, "g", out float g) ||
                !TryReadNumber(map, "b", out float b))
            {
                return false;
            }

            float a = 1f;
            if (map.ContainsKey("a") && !TryReadNumber(map, "a", out a))
            {
                return false;
            }

            color = new Color(r, g, b, a);
            return true;
        }

        private static bool TryReadNumber(Dictionary<string, object> map, string key, out float number)
        {
            number = 0f;
            if (!map.TryGetValue(key, out object value))
            {
                return false;
            }

            return TryConvertToFloat(value, out number);
        }

        private static bool TryConvertToLong(object value, out long result)
        {
            if (value is long longValue)
            {
                result = longValue;
                return true;
            }

            if (value is double doubleValue)
            {
                result = (long)doubleValue;
                return true;
            }

            if (value is int intValue)
            {
                result = intValue;
                return true;
            }

            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryConvertToDouble(object value, out double result)
        {
            if (value is double doubleValue)
            {
                result = doubleValue;
                return true;
            }

            if (value is long longValue)
            {
                result = longValue;
                return true;
            }

            if (value is int intValue)
            {
                result = intValue;
                return true;
            }

            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryConvertToFloat(object value, out float result)
        {
            if (value is float floatValue)
            {
                result = floatValue;
                return true;
            }

            if (value is double doubleValue)
            {
                result = (float)doubleValue;
                return true;
            }

            if (value is long longValue)
            {
                result = longValue;
                return true;
            }

            if (value is int intValue)
            {
                result = intValue;
                return true;
            }

            return float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
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
    }
}
