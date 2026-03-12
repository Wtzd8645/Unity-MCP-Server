using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Blanketmen.UnityMcp.Control.Editor
{
    internal static class AssetWriteToolHandlers
    {
        public static ControlToolCallResponse HandleAssetMove(ControlToolCallRequest request)
        {
            AssetMoveArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetMoveArgs
                {
                    conflictPolicy = "fail",
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0 || string.IsNullOrEmpty(args.destinationFolder))
            {
                return ControlResponses.Error("targets and destinationFolder are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryValidateFolderPath(args.destinationFolder, out string folderError))
            {
                return ControlResponses.Error(folderError, "permission_denied", request.name);
            }

            if (shouldApply && !ControlWriteSupport.EnsureFolderPathExists(args.destinationFolder, out folderError))
            {
                return ControlResponses.Error(folderError, "invalid_argument", request.name);
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                AssetRef target = args.targets[i];
                if (!ControlWriteSupport.TryResolveAssetRef(target, out string sourcePath, out string sourceGuid))
                {
                    items.Add(new MutationItem
                    {
                        action = "asset_move",
                        status = "failed",
                        changed = false,
                        message = "Asset target not found.",
                    });
                    continue;
                }

                string fileName = Path.GetFileName(sourcePath);
                string destinationPath = args.destinationFolder.TrimEnd('/') + "/" + fileName;
                MutationItem item = ControlWriteSupport.BuildAssetMutationItem(sourcePath, "asset_move");
                item.guid = sourceGuid;
                item.path = destinationPath;
                items.Add(item);

                if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.status = "skipped";
                    item.changed = false;
                    item.message = "Source and destination are the same.";
                    continue;
                }

                if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
                {
                    string policy = string.IsNullOrEmpty(args.conflictPolicy) ? "fail" : args.conflictPolicy;
                    if (string.Equals(policy, "fail", StringComparison.OrdinalIgnoreCase))
                    {
                        item.status = "failed";
                        item.changed = false;
                        item.message = "Destination already exists: " + destinationPath;
                        continue;
                    }

                    if (string.Equals(policy, "rename", StringComparison.OrdinalIgnoreCase))
                    {
                        destinationPath = AssetDatabase.GenerateUniqueAssetPath(destinationPath);
                        item.path = destinationPath;
                    }
                    else if (string.Equals(policy, "overwrite", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!shouldApply)
                        {
                            item.message = "Overwrite planned for existing destination.";
                        }
                        else if (!AssetDatabase.DeleteAsset(destinationPath))
                        {
                            item.status = "failed";
                            item.changed = false;
                            item.message = "Failed to delete existing destination: " + destinationPath;
                            continue;
                        }
                    }
                }

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.changed = false;
                    if (string.IsNullOrEmpty(item.message))
                    {
                        item.message = "Move planned.";
                    }

                    continue;
                }

                string moveError = AssetDatabase.MoveAsset(sourcePath, destinationPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = moveError;
                    continue;
                }

                item.status = "succeeded";
                item.changed = true;
                item.path = destinationPath;
                item.guid = AssetDatabase.AssetPathToGUID(destinationPath);
                item.message = "Asset moved.";
            }

            if (shouldApply)
            {
                AssetDatabase.Refresh();
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleAssetRename(ControlToolCallRequest request)
        {
            AssetRenameArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetRenameArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null || string.IsNullOrEmpty(args.newName))
            {
                return ControlResponses.Error("target and newName are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveAssetRef(args.target, out string path, out string guid))
            {
                return ControlResponses.Error("Asset target not found.", "not_found", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildAssetMutationItem(path, "asset_rename");
            item.guid = guid;
            items.Add(item);

            string renamedPath = BuildRenamedPath(path, args.newName);
            item.path = renamedPath;

            if (!shouldApply)
            {
                item.status = "planned";
                item.changed = false;
                item.message = "Rename planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            string renameError = AssetDatabase.RenameAsset(path, args.newName);
            if (!string.IsNullOrEmpty(renameError))
            {
                item.status = "failed";
                item.changed = false;
                item.message = renameError;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            item.status = "succeeded";
            item.changed = true;
            item.path = renamedPath;
            item.guid = AssetDatabase.AssetPathToGUID(renamedPath);
            item.message = "Asset renamed.";
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static ControlToolCallResponse HandleAssetDeleteToTrash(ControlToolCallRequest request)
        {
            AssetDeleteToTrashArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetDeleteToTrashArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return ControlResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.targets.Length; i++)
            {
                AssetRef target = args.targets[i];
                if (!ControlWriteSupport.TryResolveAssetRef(target, out string path, out string guid))
                {
                    items.Add(new MutationItem
                    {
                        action = "asset_delete_to_trash",
                        status = "failed",
                        changed = false,
                        message = "Asset target not found.",
                    });
                    continue;
                }

                MutationItem item = ControlWriteSupport.BuildAssetMutationItem(path, "asset_delete_to_trash");
                item.guid = guid;
                items.Add(item);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.message = "Delete to trash planned.";
                    continue;
                }

                bool deleted = AssetDatabase.MoveAssetToTrash(path);
                item.status = deleted ? "succeeded" : "failed";
                item.changed = deleted;
                item.message = deleted ? "Asset moved to trash." : "Failed to move asset to trash.";
            }

            if (shouldApply)
            {
                AssetDatabase.Refresh();
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleAssetReimport(ControlToolCallRequest request)
        {
            AssetReimportArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetReimportArgs
                {
                    recursive = false,
                    forceUpdate = false,
                    dryRun = true,
                    apply = false,
                });

            if (args.targets == null || args.targets.Length == 0)
            {
                return ControlResponses.Error("targets is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            var items = new List<MutationItem>();
            var importPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.targets.Length; i++)
            {
                AssetRef target = args.targets[i];
                if (!ControlWriteSupport.TryResolveAssetRef(target, out string path, out string guid))
                {
                    items.Add(new MutationItem
                    {
                        action = "asset_reimport",
                        status = "failed",
                        changed = false,
                        message = "Asset target not found.",
                    });
                    continue;
                }

                MutationItem item = ControlWriteSupport.BuildAssetMutationItem(path, "asset_reimport");
                item.guid = guid;
                items.Add(item);

                var targetImportPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
                if (args.recursive && AssetDatabase.IsValidFolder(path))
                {
                    string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { path });
                    for (int g = 0; g < guids.Length; g++)
                    {
                        string childPath = AssetDatabase.GUIDToAssetPath(guids[g]);
                        if (!string.IsNullOrEmpty(childPath))
                        {
                            targetImportPaths.Add(childPath);
                        }
                    }
                }

                foreach (string importPath in targetImportPaths)
                {
                    importPaths.Add(importPath);
                }

                item.status = shouldApply ? "succeeded" : "planned";
                item.changed = shouldApply;
                item.message = (shouldApply ? "Reimported paths=" : "Reimport planned paths=") + targetImportPaths.Count;
            }

            if (shouldApply)
            {
                ImportAssetOptions options = ImportAssetOptions.Default;
                if (args.forceUpdate)
                {
                    options |= ImportAssetOptions.ForceUpdate;
                }

                foreach (string path in importPaths)
                {
                    AssetDatabase.ImportAsset(path, options);
                }
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

        public static ControlToolCallResponse HandleAssetSetLabels(ControlToolCallRequest request)
        {
            AssetSetLabelsArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetSetLabelsArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (args.target == null || string.IsNullOrEmpty(args.mode) || args.labels == null || args.labels.Length == 0)
            {
                return ControlResponses.Error("target, mode, labels are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryResolveAssetRef(args.target, out string path, out string guid))
            {
                return ControlResponses.Error("Asset target not found.", "not_found", request.name);
            }

            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
            {
                return ControlResponses.Error("Asset not found: " + path, "not_found", request.name);
            }

            var current = new HashSet<string>(AssetDatabase.GetLabels(asset), StringComparer.OrdinalIgnoreCase);
            var requested = new HashSet<string>(args.labels.Where(label => !string.IsNullOrEmpty(label)), StringComparer.OrdinalIgnoreCase);

            string mode = args.mode;
            if (string.Equals(mode, "set", StringComparison.OrdinalIgnoreCase))
            {
                current = requested;
            }
            else if (string.Equals(mode, "add", StringComparison.OrdinalIgnoreCase))
            {
                current.UnionWith(requested);
            }
            else if (string.Equals(mode, "remove", StringComparison.OrdinalIgnoreCase))
            {
                current.ExceptWith(requested);
            }
            else
            {
                return ControlResponses.Error("mode must be one of set/add/remove.", "invalid_argument", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildAssetMutationItem(path, "asset_set_labels");
            item.guid = guid;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.changed = false;
                item.message = "Label update planned. labels=" + current.Count;
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            AssetDatabase.SetLabels(asset, current.OrderBy(label => label, StringComparer.OrdinalIgnoreCase).ToArray());
            EditorUtility.SetDirty(asset);

            item.status = "succeeded";
            item.changed = true;
            item.message = "Labels updated. labels=" + current.Count;
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        private static string BuildRenamedPath(string path, string newName)
        {
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            string ext = Path.GetExtension(path);
            return folder + "/" + newName + ext;
        }
    }
}
