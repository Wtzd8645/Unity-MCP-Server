using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class ComponentReadToolHandlers
    {
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

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject gameObject, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            if (!ControlWriteSupport.TryResolveComponent(gameObject, args.componentType, args.componentId, out Component component))
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
