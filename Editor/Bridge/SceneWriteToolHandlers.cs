using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal static class SceneWriteToolHandlers
    {
        public static BridgeToolCallResponse HandleGoCreate(BridgeToolCallRequest request)
        {
            GoCreateArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new GoCreateArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (string.IsNullOrEmpty(args.name))
            {
                return BridgeResponses.Error("name is required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            GameObject parentObject = null;
            if (args.parent != null)
            {
                if (!BridgeWriteSupport.TryResolveGameObject(args.parent, out parentObject, out string parentError))
                {
                    return BridgeResponses.Error(parentError, "not_found", request.name);
                }
            }

            Scene targetScene = default(Scene);
            if (!string.IsNullOrEmpty(args.scenePath))
            {
                if (!BridgeWriteSupport.TryGetSceneByPath(args.scenePath, out targetScene))
                {
                    return BridgeResponses.Error("Scene not loaded: " + args.scenePath, "not_found", request.name);
                }
            }

            List<GoComponentSpec> componentSpecs = BridgeWriteSupport.ExtractGoComponentSpecs(request.argumentsJson);
            for (int i = 0; i < componentSpecs.Count; i++)
            {
                string componentTypeName = componentSpecs[i].type;
                Type componentType = BridgeWriteSupport.FindTypeByName(componentTypeName);
                if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                {
                    return BridgeResponses.Error("Unknown component type: " + componentTypeName, "invalid_argument", request.name);
                }

                if (!BridgeWriteSupport.TryValidateComponentTypeAllowed(componentType, out string allowError))
                {
                    return BridgeResponses.Error(allowError, "permission_denied", request.name);
                }
            }

            var items = new List<MutationItem>();
            var item = new MutationItem
            {
                target = args.name,
                action = "go_create",
                status = "planned",
                changed = false,
                scenePath = !string.IsNullOrEmpty(args.scenePath)
                    ? args.scenePath
                    : (parentObject == null ? SceneManager.GetActiveScene().path : parentObject.scene.path),
                hierarchyPath = parentObject == null ? args.name : BuildChildHierarchyPath(parentObject.transform, args.name),
            };
            items.Add(item);

            if (!shouldApply)
            {
                item.message = "GameObject creation planned.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            GameObject created = null;
            try
            {
                created = new GameObject(args.name);
                Undo.RegisterCreatedObjectUndo(created, "MCP Create GameObject");

                if (parentObject != null)
                {
                    created.transform.SetParent(parentObject.transform, false);
                }
                else if (targetScene.IsValid() && created.scene != targetScene)
                {
                    SceneManager.MoveGameObjectToScene(created, targetScene);
                }

                BridgeWriteSupport.ApplyTransform(created.transform, args.transform);

                for (int i = 0; i < componentSpecs.Count; i++)
                {
                    GoComponentSpec spec = componentSpecs[i];
                    Type componentType = BridgeWriteSupport.FindTypeByName(spec.type);
                    Component component = Undo.AddComponent(created, componentType);
                    if (component == null)
                    {
                        throw new InvalidOperationException("Failed to add component: " + spec.type);
                    }

                    if (spec.fields != null && spec.fields.Count > 0)
                    {
                        if (!BridgeWriteSupport.TryApplyComponentFields(component, spec.fields, true, out _, out string fieldError))
                        {
                            throw new InvalidOperationException(fieldError);
                        }
                    }
                }

                item.status = "succeeded";
                item.changed = true;
                item.target = created.name;
                item.globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(created).ToString();
                item.scenePath = created.scene.path;
                item.hierarchyPath = BuildHierarchyPath(created.transform);
                item.message = "GameObject created.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
            catch (Exception ex)
            {
                if (created != null)
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }

                return BridgeResponses.Error("Failed to create GameObject: " + ex.Message, "tool_exception", request.name);
            }
        }

        public static BridgeToolCallResponse HandleGoDelete(BridgeToolCallRequest request)
        {
            GoDeleteArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new GoDeleteArgs
                {
                    mode = "undoable",
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return BridgeResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            bool immediate = string.Equals(args.mode, "immediate", StringComparison.OrdinalIgnoreCase);
            var items = new List<MutationItem>();

            for (int i = 0; i < args.targets.Length; i++)
            {
                GameObjectRef targetRef = args.targets[i];
                if (!BridgeWriteSupport.TryResolveGameObject(targetRef, out GameObject target, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "go_delete",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = BridgeWriteSupport.BuildGoMutationItem(target, "go_delete");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "Deletion planned.";
                    continue;
                }

                if (immediate)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
                else
                {
                    Undo.DestroyObjectImmediate(target);
                }

                item.status = "succeeded";
                item.changed = true;
                item.message = "GameObject deleted.";
            }

            return BridgeWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static BridgeToolCallResponse HandleGoDuplicate(BridgeToolCallRequest request)
        {
            GoDuplicateArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new GoDuplicateArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return BridgeResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            GameObject parentOverride = null;
            if (args.parent != null)
            {
                if (!BridgeWriteSupport.TryResolveGameObject(args.parent, out parentOverride, out string parentError))
                {
                    return BridgeResponses.Error(parentError, "not_found", request.name);
                }
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                GameObjectRef targetRef = args.targets[i];
                if (!BridgeWriteSupport.TryResolveGameObject(targetRef, out GameObject source, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "go_duplicate",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = BridgeWriteSupport.BuildGoMutationItem(source, "go_duplicate");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.changed = false;
                    item.message = "Duplication planned.";
                    item.target = BridgeWriteSupport.BuildRename(args.renamePattern, source.name, i + 1);
                    continue;
                }

                Transform duplicateParent = parentOverride == null ? source.transform.parent : parentOverride.transform;
                GameObject duplicate = duplicateParent == null
                    ? UnityEngine.Object.Instantiate(source)
                    : UnityEngine.Object.Instantiate(source, duplicateParent);
                Undo.RegisterCreatedObjectUndo(duplicate, "MCP Duplicate GameObject");

                duplicate.name = BridgeWriteSupport.BuildRename(args.renamePattern, source.name, i + 1);
                item.target = duplicate.name;
                item.status = "succeeded";
                item.changed = true;
                item.globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(duplicate).ToString();
                item.scenePath = duplicate.scene.path;
                item.hierarchyPath = BuildHierarchyPath(duplicate.transform);
                item.message = "GameObject duplicated.";
            }

            return BridgeWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static BridgeToolCallResponse HandleGoReparent(BridgeToolCallRequest request)
        {
            GoReparentArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new GoReparentArgs
                {
                    worldPositionStays = true,
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return BridgeResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            GameObject newParent = null;
            if (args.newParent != null)
            {
                if (!BridgeWriteSupport.TryResolveGameObject(args.newParent, out newParent, out string parentError))
                {
                    return BridgeResponses.Error(parentError, "not_found", request.name);
                }
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                GameObjectRef targetRef = args.targets[i];
                if (!BridgeWriteSupport.TryResolveGameObject(targetRef, out GameObject target, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "go_reparent",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = BridgeWriteSupport.BuildGoMutationItem(target, "go_reparent");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "Reparent planned.";
                    continue;
                }

                Undo.RecordObject(target.transform, "MCP Reparent GameObject");
                target.transform.SetParent(newParent == null ? null : newParent.transform, args.worldPositionStays);
                item.status = "succeeded";
                item.changed = true;
                item.scenePath = target.scene.path;
                item.hierarchyPath = BuildHierarchyPath(target.transform);
                item.message = "GameObject reparented.";
            }

            return BridgeWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static BridgeToolCallResponse HandleGoRename(BridgeToolCallRequest request)
        {
            GoRenameArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new GoRenameArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null || string.IsNullOrEmpty(args.newName))
            {
                return BridgeResponses.Error("target and newName are required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            if (!BridgeWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return BridgeResponses.Error(resolveError, "not_found", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = BridgeWriteSupport.BuildGoMutationItem(target, "go_rename");
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.target = args.newName;
                item.message = "Rename planned.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            Undo.RecordObject(target, "MCP Rename GameObject");
            target.name = args.newName;
            EditorUtility.SetDirty(target);

            item.status = "succeeded";
            item.changed = true;
            item.target = target.name;
            item.hierarchyPath = BuildHierarchyPath(target.transform);
            item.message = "GameObject renamed.";
            return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static BridgeToolCallResponse HandleGoSetActive(BridgeToolCallRequest request)
        {
            GoSetActiveArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new GoSetActiveArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return BridgeResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                GameObjectRef targetRef = args.targets[i];
                if (!BridgeWriteSupport.TryResolveGameObject(targetRef, out GameObject target, out string resolveError))
                {
                    items.Add(new MutationItem
                    {
                        action = "go_set_active",
                        status = "failed",
                        changed = false,
                        message = resolveError,
                    });
                    continue;
                }

                MutationItem item = BridgeWriteSupport.BuildGoMutationItem(target, "go_set_active");
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "SetActive planned: " + args.active;
                    continue;
                }

                Undo.RecordObject(target, "MCP Set Active");
                target.SetActive(args.active);
                item.status = "succeeded";
                item.changed = true;
                item.message = "Active state updated.";
            }

            return BridgeWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static BridgeToolCallResponse HandleComponentAdd(BridgeToolCallRequest request)
        {
            ComponentAddArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new ComponentAddArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null || string.IsNullOrEmpty(args.componentType))
            {
                return BridgeResponses.Error("target and componentType are required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            if (!BridgeWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return BridgeResponses.Error(resolveError, "not_found", request.name);
            }

            Type componentType = BridgeWriteSupport.FindTypeByName(args.componentType);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return BridgeResponses.Error("Unknown component type: " + args.componentType, "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryValidateComponentTypeAllowed(componentType, out string allowError))
            {
                return BridgeResponses.Error(allowError, "permission_denied", request.name);
            }

            Dictionary<string, object> fields = BridgeWriteSupport.ExtractObject(request.argumentsJson, "fields");

            var items = new List<MutationItem>();
            MutationItem item = BridgeWriteSupport.BuildGoMutationItem(target, "component_add");
            item.componentType = componentType.FullName ?? componentType.Name;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Add component planned.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            Component component = Undo.AddComponent(target, componentType);
            if (component == null)
            {
                item.status = "failed";
                item.changed = false;
                item.message = "Failed to add component.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            if (fields != null && fields.Count > 0)
            {
                if (!BridgeWriteSupport.TryApplyComponentFields(component, fields, false, out int appliedFields, out string fieldError))
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = "Component created but fields apply failed: " + fieldError;
                    item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
                    return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
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
            return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static BridgeToolCallResponse HandleComponentRemove(BridgeToolCallRequest request)
        {
            ComponentRemoveArgs args = BridgeJson.ParseArgs(
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
                return BridgeResponses.Error("target and exactly one of componentId/componentType are required.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            if (!BridgeWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return BridgeResponses.Error(resolveError, "not_found", request.name);
            }

            if (!BridgeWriteSupport.TryResolveComponent(target, args.componentType, args.componentId, out Component component))
            {
                return BridgeResponses.Error("Component not found on target GameObject.", "not_found", request.name);
            }


            if (!BridgeWriteSupport.TryValidateComponentTypeAllowed(component.GetType(), out string allowError))
            {
                return BridgeResponses.Error(allowError, "permission_denied", request.name);
            }
            if (component is Transform)
            {
                return BridgeResponses.Error("Transform component cannot be removed.", "invalid_argument", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = BridgeWriteSupport.BuildGoMutationItem(target, "component_remove");
            item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            item.componentType = component.GetType().FullName ?? component.GetType().Name;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Remove component planned.";
                return BridgeWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            Undo.DestroyObjectImmediate(component);
            item.status = "succeeded";
            item.changed = true;
            item.message = "Component removed.";
            return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static BridgeToolCallResponse HandleComponentSetFields(BridgeToolCallRequest request)
        {
            ComponentSetFieldsArgs args = BridgeJson.ParseArgs(
                request.argumentsJson,
                new ComponentSetFieldsArgs
                {
                    strict = true,
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null)
            {
                return BridgeResponses.Error("target is required.", "invalid_argument", request.name);
            }

            Dictionary<string, object> fields = BridgeWriteSupport.ExtractObject(request.argumentsJson, "fields");
            if (fields == null || fields.Count == 0)
            {
                return BridgeResponses.Error("fields is required and must contain at least one entry.", "invalid_argument", request.name);
            }

            if (!BridgeWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out BridgeToolCallResponse modeError))
            {
                return modeError;
            }

            if (!BridgeWriteSupport.TryResolveGameObject(args.target, out GameObject target, out string resolveError))
            {
                return BridgeResponses.Error(resolveError, "not_found", request.name);
            }

            if (!BridgeWriteSupport.TryResolveComponent(target, args.componentType, args.componentId, out Component component))
            {
                return BridgeResponses.Error("Component not found on target GameObject.", "not_found", request.name);
            }


            if (!BridgeWriteSupport.TryValidateComponentTypeAllowed(component.GetType(), out string allowError))
            {
                return BridgeResponses.Error(allowError, "permission_denied", request.name);
            }
            var items = new List<MutationItem>();
            MutationItem item = BridgeWriteSupport.BuildGoMutationItem(target, "component_set_fields");
            item.componentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            item.componentType = component.GetType().FullName ?? component.GetType().Name;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.message = "Set fields planned. fieldCount=" + fields.Count;
                return BridgeWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            if (!BridgeWriteSupport.TryApplyComponentFields(component, fields, args.strict, out int appliedCount, out string applyError))
            {
                item.status = "failed";
                item.changed = false;
                item.message = applyError;
                return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            item.status = "succeeded";
            item.changed = appliedCount > 0;
            item.message = "Fields updated. applied=" + appliedCount;
            return BridgeWriteSupport.BuildMutationResponse(request.name, true, 1, items);
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

        private static string BuildChildHierarchyPath(Transform parent, string childName)
        {
            string parentPath = BuildHierarchyPath(parent);
            if (string.IsNullOrEmpty(parentPath))
            {
                return childName;
            }

            return parentPath + "/" + childName;
        }
    }
}
