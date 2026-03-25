using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class EditorExecuteToolHandlers
    {
        public static ControlToolCallResponse HandleSetSelection(ControlToolCallRequest request)
        {
            SetSelectionArgs args = ControlJson.ParseArgs(request.argumentsJson, new SetSelectionArgs());
            bool hasGameObjects = args.gameObjects != null && args.gameObjects.Length > 0;
            bool hasAssets = args.assets != null && args.assets.Length > 0;
            if (!hasGameObjects && !hasAssets)
            {
                return ControlResponses.Error("gameObjects or assets is required.", "invalid_argument", request.name);
            }

            if (!TryResolveSelectionObjects(args, out List<UnityEngine.Object> objects, out string error))
            {
                return ControlResponses.Error(error, "not_found", request.name);
            }

            Selection.objects = objects.ToArray();
            if (objects.Count > 0)
            {
                Selection.activeObject = objects[0];
            }

            EditorSelectionResult payload = BuildSelectionResult();
            return ControlResponses.Success("unity_editor_set_selection completed.", payload);
        }

        public static ControlToolCallResponse HandleFrameSelection(ControlToolCallRequest request)
        {
            ControlJson.ParseArgs(request.argumentsJson, new FrameSelectionArgs());

            GameObject[] selectedGameObjects = Selection.gameObjects ?? Array.Empty<GameObject>();
            int sceneSelectionCount = selectedGameObjects.Count(gameObject => gameObject != null);
            if (sceneSelectionCount == 0)
            {
                return ControlResponses.Error("Selection does not include scene GameObjects.", "invalid_state", request.name);
            }

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                foreach (object view in SceneView.sceneViews)
                {
                    sceneView = view as SceneView;
                    if (sceneView != null)
                    {
                        break;
                    }
                }
            }

            if (sceneView == null)
            {
                return ControlResponses.Error("No active SceneView is available.", "invalid_state", request.name);
            }

            bool framed = sceneView.FrameSelected(false);
            sceneView.Repaint();

            var payload = new FrameSelectionResult
            {
                framed = framed,
                sceneSelectionCount = sceneSelectionCount,
                hasActiveSceneView = true,
            };

            return ControlResponses.Success("unity_editor_frame_selection completed.", payload);
        }

        private static bool TryResolveSelectionObjects(
            SetSelectionArgs args,
            out List<UnityEngine.Object> objects,
            out string error)
        {
            objects = new List<UnityEngine.Object>();
            error = null;
            var seenInstanceIds = new HashSet<int>();

            if (args.gameObjects != null)
            {
                for (int i = 0; i < args.gameObjects.Length; i++)
                {
                    if (!ControlWriteSupport.TryResolveGameObject(args.gameObjects[i], out GameObject gameObject, out string resolveError))
                    {
                        error = resolveError;
                        return false;
                    }

                    if (gameObject != null && seenInstanceIds.Add(gameObject.GetInstanceID()))
                    {
                        objects.Add(gameObject);
                    }
                }
            }

            if (args.assets != null)
            {
                for (int i = 0; i < args.assets.Length; i++)
                {
                    if (!ControlWriteSupport.TryResolveAssetRef(args.assets[i], out string path, out _))
                    {
                        error = "Asset target not found.";
                        return false;
                    }

                    UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset == null)
                    {
                        error = "Asset not found: " + path;
                        return false;
                    }

                    if (seenInstanceIds.Add(asset.GetInstanceID()))
                    {
                        objects.Add(asset);
                    }
                }
            }

            return true;
        }

        private static EditorSelectionResult BuildSelectionResult()
        {
            UnityEngine.Object[] selectedObjects = Selection.objects ?? Array.Empty<UnityEngine.Object>();
            var items = new List<EditorSelectionItem>(selectedObjects.Length);
            int gameObjectCount = 0;
            int assetCount = 0;

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                EditorSelectionItem item = BuildSelectionItem(selectedObjects[i]);
                if (item == null)
                {
                    continue;
                }

                if (string.Equals(item.kind, "gameObject", StringComparison.Ordinal))
                {
                    gameObjectCount++;
                }
                else if (string.Equals(item.kind, "asset", StringComparison.Ordinal))
                {
                    assetCount++;
                }

                items.Add(item);
            }

            return new EditorSelectionResult
            {
                count = items.Count,
                gameObjectCount = gameObjectCount,
                assetCount = assetCount,
                activeObject = BuildSelectionItem(Selection.activeObject),
                items = items.ToArray(),
            };
        }

        private static EditorSelectionItem BuildSelectionItem(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return null;
            }

            bool isPersistent = EditorUtility.IsPersistent(obj);
            var item = new EditorSelectionItem
            {
                kind = GetSelectionKind(obj, isPersistent),
                name = obj.name,
                objectType = obj.GetType().FullName ?? obj.GetType().Name,
                globalObjectId = TryGetGlobalObjectId(obj),
                isPersistent = isPersistent,
            };

            if (isPersistent)
            {
                item.assetPath = AssetDatabase.GetAssetPath(obj);
                item.guid = string.IsNullOrEmpty(item.assetPath) ? null : AssetDatabase.AssetPathToGUID(item.assetPath);
            }
            else if (obj is GameObject gameObject)
            {
                item.scenePath = gameObject.scene.path;
                item.hierarchyPath = BuildHierarchyPath(gameObject.transform);
            }
            else if (obj is Component component)
            {
                item.scenePath = component.gameObject.scene.path;
                item.hierarchyPath = BuildHierarchyPath(component.gameObject.transform);
            }

            return item;
        }

        private static string GetSelectionKind(UnityEngine.Object obj, bool isPersistent)
        {
            if (obj is GameObject)
            {
                return "gameObject";
            }

            if (obj is Component)
            {
                return "component";
            }

            if (isPersistent)
            {
                return "asset";
            }

            return "other";
        }

        private static string TryGetGlobalObjectId(UnityEngine.Object obj)
        {
            try
            {
                return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            string current = transform.name;
            Transform parent = transform.parent;
            while (parent != null)
            {
                current = parent.name + "/" + current;
                parent = parent.parent;
            }

            return current;
        }
    }
}
