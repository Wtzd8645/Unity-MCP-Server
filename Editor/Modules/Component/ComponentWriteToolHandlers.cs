using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class ComponentWriteToolHandlers
    {
        public static ControlToolCallResponse HandleComponentAdd(ControlToolCallRequest request)
        {
            ComponentAddArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ComponentAddArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null || string.IsNullOrEmpty(args.componentType))
            {
                return ControlResponses.Error("target and componentType are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            Type componentType = ControlWriteSupport.FindTypeByName(args.componentType);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return ControlResponses.Error("Unknown component type: " + args.componentType, "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryValidateComponentTypeAllowed(componentType, out string allowError))
            {
                return ControlResponses.Error(allowError, "permission_denied", request.name);
            }

            Dictionary<string, object> fields = ControlWriteSupport.ExtractObject(request.argumentsJson, "fields");

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "component_add");
            item.componentType = componentType.FullName ?? componentType.Name;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Add component planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            Component component = Undo.AddComponent(target, componentType);
            if (component == null)
            {
                item.status = "failed";
                item.changed = false;
                item.message = "Failed to add component.";
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            if (fields != null && fields.Count > 0)
            {
                if (!ControlWriteSupport.TryApplyComponentFields(component, fields, false, out int appliedFields, out string fieldError))
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = "Component created but fields apply failed: " + fieldError;
                    item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
                    return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
                }

                item.message = "Component added. Applied fields=" + appliedFields;
            }
            else
            {
                item.message = "Component added.";
            }

            item.status = "succeeded";
            item.changed = true;
            item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            item.componentType = component.GetType().FullName ?? component.GetType().Name;
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static ControlToolCallResponse HandleComponentRemove(ControlToolCallRequest request)
        {
            ComponentRemoveArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ComponentRemoveArgs
                {
                    dryRun = true,
                    apply = false,
                });

            bool hasComponentId = !string.IsNullOrEmpty(args.componentId);
            bool hasComponentType = !string.IsNullOrEmpty(args.componentType);
            if (args.target == null || hasComponentId == hasComponentType)
            {
                return ControlResponses.Error("target and exactly one of componentId/componentType are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            if (!ControlWriteSupport.TryResolveComponent(target, args.componentType, args.componentId, out Component component))
            {
                return ControlResponses.Error("Component not found on target GameObject.", "not_found", request.name);
            }

            if (!ControlWriteSupport.TryValidateComponentTypeAllowed(component.GetType(), out string allowError))
            {
                return ControlResponses.Error(allowError, "permission_denied", request.name);
            }

            if (component is Transform)
            {
                return ControlResponses.Error("Transform component cannot be removed.", "invalid_argument", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "component_remove");
            item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            item.componentType = component.GetType().FullName ?? component.GetType().Name;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Remove component planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            Undo.DestroyObjectImmediate(component);
            item.status = "succeeded";
            item.changed = true;
            item.message = "Component removed.";
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static ControlToolCallResponse HandleComponentSetFields(ControlToolCallRequest request)
        {
            ComponentSetFieldsArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new ComponentSetFieldsArgs
                {
                    strict = true,
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null)
            {
                return ControlResponses.Error("target is required.", "invalid_argument", request.name);
            }

            Dictionary<string, object> fields = ControlWriteSupport.ExtractObject(request.argumentsJson, "fields");
            if (fields == null || fields.Count == 0)
            {
                return ControlResponses.Error("fields is required and must contain at least one entry.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return ControlResponses.Error(resolveError, "not_found", request.name);
            }

            if (!ControlWriteSupport.TryResolveComponent(target, args.componentType, args.componentId, out Component component))
            {
                return ControlResponses.Error("Component not found on target GameObject.", "not_found", request.name);
            }

            if (!ControlWriteSupport.TryValidateComponentTypeAllowed(component.GetType(), out string allowError))
            {
                return ControlResponses.Error(allowError, "permission_denied", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildGoMutationItem(target, "component_set_fields");
            item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            item.componentType = component.GetType().FullName ?? component.GetType().Name;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Set fields planned. fieldCount=" + fields.Count;
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            if (!ControlWriteSupport.TryApplyComponentFields(component, fields, args.strict, out int appliedCount, out string applyError))
            {
                item.status = "failed";
                item.changed = false;
                item.message = applyError;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            item.status = "succeeded";
            item.changed = appliedCount > 0;
            item.message = "Fields updated. applied=" + appliedCount;
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }
    }
}
