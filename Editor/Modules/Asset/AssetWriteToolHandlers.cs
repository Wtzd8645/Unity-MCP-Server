using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Blanketmen.UnityMcp.Editor.Control;

namespace Blanketmen.UnityMcp.Editor.Modules
{
    internal static class AssetWriteToolHandlers
    {
        public static ControlToolCallResponse HandleAssetCopy(ControlToolCallRequest request)
        {
            AssetCopyArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetCopyArgs
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

            if (!TryNormalizeConflictPolicy(args.conflictPolicy, request.name, out string conflictPolicy, out ControlToolCallResponse policyError))
            {
                return policyError;
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
            var reservedDestinationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.targets.Length; i++)
            {
                AssetRef target = args.targets[i];
                if (!ControlWriteSupport.TryResolveAssetRef(target, out string sourcePath, out string sourceGuid))
                {
                    items.Add(new MutationItem
                    {
                        action = "asset_copy",
                        status = "failed",
                        changed = false,
                        message = "Asset target not found.",
                    });
                    continue;
                }

                string destinationPath = args.destinationFolder.TrimEnd('/') + "/" + Path.GetFileName(sourcePath);
                MutationItem item = ControlWriteSupport.BuildAssetMutationItem(sourcePath, "asset_copy");
                item.guid = sourceGuid;
                item.path = destinationPath;
                items.Add(item);

                bool sameAsSource = string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase);
                bool destinationExists = AssetExistsAtPath(destinationPath) || reservedDestinationPaths.Contains(destinationPath);

                if (sameAsSource || destinationExists)
                {
                    if (string.Equals(conflictPolicy, "fail", StringComparison.OrdinalIgnoreCase))
                    {
                        item.status = "failed";
                        item.changed = false;
                        item.message = sameAsSource
                            ? "Destination resolves to the source asset. Use conflictPolicy=rename to create a copy beside the source."
                            : "Destination already exists: " + destinationPath;
                        continue;
                    }

                    if (string.Equals(conflictPolicy, "rename", StringComparison.OrdinalIgnoreCase))
                    {
                        destinationPath = GenerateUniqueDestinationPath(destinationPath, reservedDestinationPaths);
                        item.path = destinationPath;
                    }
                    else if (sameAsSource)
                    {
                        item.status = "failed";
                        item.changed = false;
                        item.message = "conflictPolicy=overwrite is not valid when destination resolves to the source asset.";
                        continue;
                    }
                    else if (!shouldApply)
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

                reservedDestinationPaths.Add(destinationPath);

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.changed = false;
                    if (string.IsNullOrEmpty(item.message))
                    {
                        item.message = "Copy planned.";
                    }

                    continue;
                }

                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = "Failed to copy asset.";
                    continue;
                }

                item.status = "succeeded";
                item.changed = true;
                item.path = destinationPath;
                item.guid = AssetDatabase.AssetPathToGUID(destinationPath);
                item.message = "Asset copied.";
            }

            if (shouldApply)
            {
                AssetDatabase.Refresh();
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.targets.Length, items);
        }

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

        public static ControlToolCallResponse HandleAssetCreateFolder(ControlToolCallRequest request)
        {
            AssetCreateFolderArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetCreateFolderArgs
                {
                    dryRun = true,
                    apply = false,
                });

            if (string.IsNullOrEmpty(args.path))
            {
                return ControlResponses.Error("path is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!ControlWriteSupport.TryNormalizeAssetPath(args.path, out string folderPath, out string normalizeError))
            {
                return ControlResponses.Error(normalizeError, "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryValidateFolderPath(folderPath, out string folderError))
            {
                return ControlResponses.Error(folderError, "permission_denied", request.name);
            }

            bool folderExists = AssetDatabase.IsValidFolder(folderPath);
            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildAssetMutationItem(folderPath, "asset_create_folder");
            item.path = folderPath;
            item.target = folderPath;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.changed = false;
                item.message = folderExists ? "Folder already exists." : "Folder create planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            if (folderExists)
            {
                item.status = "succeeded";
                item.changed = false;
                item.guid = AssetDatabase.AssetPathToGUID(folderPath);
                item.message = "Folder already exists.";
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            if (!ControlWriteSupport.EnsureFolderPathExists(folderPath, out folderError))
            {
                item.status = "failed";
                item.changed = false;
                item.message = folderError;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            item.status = "succeeded";
            item.changed = true;
            item.guid = AssetDatabase.AssetPathToGUID(folderPath);
            item.message = "Folder created.";
            AssetDatabase.Refresh();
            return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
        }

        public static ControlToolCallResponse HandleAssetCreateMaterial(ControlToolCallRequest request)
        {
            AssetCreateMaterialArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetCreateMaterialArgs
                {
                    overwrite = false,
                    dryRun = true,
                    apply = false,
                });

            if (string.IsNullOrEmpty(args.outputPath) || string.IsNullOrEmpty(args.shaderName))
            {
                return ControlResponses.Error("outputPath and shaderName are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!TryResolveOutputAssetPath(args.outputPath, ".mat", request.name, out string outputPath, out string folderPath, out ControlToolCallResponse pathError))
            {
                return pathError;
            }

            Shader shader = Shader.Find(args.shaderName);
            if (shader == null)
            {
                return ControlResponses.Error("Shader not found: " + args.shaderName, "not_found", request.name);
            }

            bool destinationExists = AssetExistsAtPath(outputPath);
            if (destinationExists && !args.overwrite)
            {
                return ControlResponses.Error("Asset already exists: " + outputPath, "already_exists", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildAssetMutationItem(outputPath, "asset_create_material");
            item.path = outputPath;
            item.target = outputPath;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.changed = false;
                item.message = destinationExists
                    ? "Material create planned with overwrite."
                    : "Material create planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            if (!ControlWriteSupport.EnsureFolderPathExists(folderPath, out string folderError))
            {
                item.status = "failed";
                item.changed = false;
                item.message = folderError;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            if (destinationExists && !AssetDatabase.DeleteAsset(outputPath))
            {
                item.status = "failed";
                item.changed = false;
                item.message = "Failed to delete existing destination: " + outputPath;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            Material material = null;
            try
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, outputPath);
                if (!AssetExistsAtPath(outputPath))
                {
                    if (material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
                    {
                        UnityEngine.Object.DestroyImmediate(material);
                    }

                    item.status = "failed";
                    item.changed = false;
                    item.message = "Material asset was not created at outputPath.";
                    return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
                }

                item.status = "succeeded";
                item.changed = true;
                item.guid = AssetDatabase.AssetPathToGUID(outputPath);
                item.message = "Material asset created.";
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
            catch (Exception ex)
            {
                if (material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }

                item.status = "failed";
                item.changed = false;
                item.message = "Failed to create material asset: " + ex.Message;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
        }

        public static ControlToolCallResponse HandleAssetCreateScriptableObject(ControlToolCallRequest request)
        {
            AssetCreateScriptableObjectArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetCreateScriptableObjectArgs
                {
                    overwrite = false,
                    dryRun = true,
                    apply = false,
                });

            if (string.IsNullOrEmpty(args.typeName) || string.IsNullOrEmpty(args.outputPath))
            {
                return ControlResponses.Error("typeName and outputPath are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!TryResolveOutputAssetPath(args.outputPath, ".asset", request.name, out string outputPath, out string folderPath, out ControlToolCallResponse pathError))
            {
                return pathError;
            }

            Type assetType = ControlWriteSupport.FindTypeByName(args.typeName);
            if (assetType == null)
            {
                return ControlResponses.Error("Unknown ScriptableObject type: " + args.typeName, "invalid_argument", request.name);
            }

            if (!typeof(ScriptableObject).IsAssignableFrom(assetType))
            {
                return ControlResponses.Error("typeName must inherit ScriptableObject: " + args.typeName, "invalid_argument", request.name);
            }

            if (assetType.IsAbstract || assetType.IsGenericTypeDefinition || assetType.ContainsGenericParameters)
            {
                return ControlResponses.Error("typeName must be a concrete ScriptableObject type: " + args.typeName, "invalid_argument", request.name);
            }

            bool destinationExists = AssetExistsAtPath(outputPath);
            if (destinationExists && !args.overwrite)
            {
                return ControlResponses.Error("Asset already exists: " + outputPath, "already_exists", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildAssetMutationItem(outputPath, "asset_create_scriptable_object");
            item.path = outputPath;
            item.target = outputPath;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.changed = false;
                item.message = destinationExists
                    ? "ScriptableObject create planned with overwrite."
                    : "ScriptableObject create planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            if (!ControlWriteSupport.EnsureFolderPathExists(folderPath, out string folderError))
            {
                item.status = "failed";
                item.changed = false;
                item.message = folderError;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            if (destinationExists && !AssetDatabase.DeleteAsset(outputPath))
            {
                item.status = "failed";
                item.changed = false;
                item.message = "Failed to delete existing destination: " + outputPath;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            ScriptableObject asset = null;
            try
            {
                asset = ScriptableObject.CreateInstance(assetType);
                if (asset == null)
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = "Failed to create ScriptableObject instance.";
                    return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
                }

                AssetDatabase.CreateAsset(asset, outputPath);
                if (!AssetExistsAtPath(outputPath))
                {
                    if (asset != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
                    {
                        UnityEngine.Object.DestroyImmediate(asset);
                    }

                    item.status = "failed";
                    item.changed = false;
                    item.message = "ScriptableObject asset was not created at outputPath.";
                    return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
                }

                item.status = "succeeded";
                item.changed = true;
                item.guid = AssetDatabase.AssetPathToGUID(outputPath);
                item.message = "ScriptableObject asset created.";
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
            catch (Exception ex)
            {
                if (asset != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }

                item.status = "failed";
                item.changed = false;
                item.message = "Failed to create ScriptableObject asset: " + ex.Message;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
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

        public static ControlToolCallResponse HandleAssetImport(ControlToolCallRequest request)
        {
            AssetImportArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetImportArgs
                {
                    recursive = false,
                    forceUpdate = false,
                    dryRun = true,
                    apply = false,
                });

            if (args.paths == null || args.paths.Length == 0)
            {
                return ControlResponses.Error("paths is required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            ImportAssetOptions options = ImportAssetOptions.Default;
            if (args.forceUpdate)
            {
                options |= ImportAssetOptions.ForceUpdate;
            }

            var items = new List<MutationItem>();
            for (int i = 0; i < args.paths.Length; i++)
            {
                string rawPath = args.paths[i];
                MutationItem item = new MutationItem
                {
                    target = rawPath,
                    action = "asset_import",
                    status = "planned",
                    changed = false,
                };
                items.Add(item);

                if (!TryResolveImportRequest(rawPath, args.recursive, request.name, out string normalizedPath, out _, out List<string> importPaths, out ControlToolCallResponse resolveError))
                {
                    item.path = normalizedPath ?? rawPath;
                    item.status = "failed";
                    item.changed = false;
                    item.message = resolveError.contentText;
                    continue;
                }

                item.path = normalizedPath;
                item.target = normalizedPath;

                if (!shouldApply)
                {
                    item.status = "planned";
                    item.changed = false;
                    item.message = "Import planned. paths=" + importPaths.Count;
                    continue;
                }

                try
                {
                    for (int p = 0; p < importPaths.Count; p++)
                    {
                        AssetDatabase.ImportAsset(importPaths[p], options);
                    }

                    item.status = "succeeded";
                    item.changed = true;
                    item.guid = AssetDatabase.AssetPathToGUID(normalizedPath);
                    item.message = "Imported paths=" + importPaths.Count;
                }
                catch (Exception ex)
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = "Failed to import path '" + normalizedPath + "': " + ex.Message;
                }
            }

            return ControlWriteSupport.BuildMutationResponse(request.name, shouldApply, args.paths.Length, items);
        }

        public static ControlToolCallResponse HandleAssetCreateText(ControlToolCallRequest request)
        {
            AssetCreateTextArgs args = ControlJson.ParseArgs(
                request.argumentsJson,
                new AssetCreateTextArgs
                {
                    overwrite = false,
                    dryRun = true,
                    apply = false,
                });

            if (string.IsNullOrEmpty(args.outputPath) || args.content == null)
            {
                return ControlResponses.Error("outputPath and content are required.", "invalid_argument", request.name);
            }

            if (!ControlWriteSupport.TryResolveApplyMode(args.dryRun, args.apply, request.name, out bool shouldApply, out ControlToolCallResponse modeError))
            {
                return modeError;
            }

            if (!TryResolveTextOutputPath(args.outputPath, request.name, out string outputPath, out string folderPath, out string absoluteOutputPath, out ControlToolCallResponse pathError))
            {
                return pathError;
            }

            bool destinationExists = File.Exists(absoluteOutputPath);
            if (destinationExists && !args.overwrite)
            {
                return ControlResponses.Error("Asset already exists: " + outputPath, "already_exists", request.name);
            }

            var items = new List<MutationItem>();
            MutationItem item = ControlWriteSupport.BuildAssetMutationItem(outputPath, "asset_create_text");
            item.path = outputPath;
            item.target = outputPath;
            items.Add(item);

            if (!shouldApply)
            {
                item.status = "planned";
                item.changed = false;
                item.message = destinationExists
                    ? "Text asset write planned with overwrite."
                    : "Text asset create planned.";
                return ControlWriteSupport.BuildMutationResponse(request.name, false, 1, items);
            }

            if (!ControlWriteSupport.EnsureFolderPathExists(folderPath, out string folderError))
            {
                item.status = "failed";
                item.changed = false;
                item.message = folderError;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }

            try
            {
                File.WriteAllText(absoluteOutputPath, args.content, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

                if (!AssetExistsAtPath(outputPath))
                {
                    item.status = "failed";
                    item.changed = false;
                    item.message = "Text asset was not imported at outputPath.";
                    return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
                }

                item.status = "succeeded";
                item.changed = true;
                item.guid = AssetDatabase.AssetPathToGUID(outputPath);
                item.message = destinationExists ? "Text asset overwritten." : "Text asset created.";
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
            catch (Exception ex)
            {
                item.status = "failed";
                item.changed = false;
                item.message = "Failed to write text asset: " + ex.Message;
                return ControlWriteSupport.BuildMutationResponse(request.name, true, 1, items);
            }
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

        private static bool TryNormalizeConflictPolicy(
            string rawPolicy,
            string toolName,
            out string policy,
            out ControlToolCallResponse errorResponse)
        {
            policy = string.IsNullOrWhiteSpace(rawPolicy) ? "fail" : rawPolicy.Trim();
            errorResponse = null;

            if (string.Equals(policy, "fail", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(policy, "overwrite", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(policy, "rename", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            errorResponse = ControlResponses.Error(
                "conflictPolicy must be one of fail/overwrite/rename.",
                "invalid_argument",
                toolName);
            return false;
        }

        private static bool TryResolveOutputAssetPath(
            string rawPath,
            string requiredExtension,
            string toolName,
            out string outputPath,
            out string folderPath,
            out ControlToolCallResponse errorResponse)
        {
            outputPath = null;
            folderPath = null;
            errorResponse = null;

            if (!ControlWriteSupport.TryNormalizeAssetPath(rawPath, out outputPath, out string normalizeError))
            {
                errorResponse = ControlResponses.Error(normalizeError, "invalid_argument", toolName);
                return false;
            }

            if (!string.IsNullOrEmpty(requiredExtension) &&
                !outputPath.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                errorResponse = ControlResponses.Error(
                    "outputPath must end with " + requiredExtension,
                    "invalid_argument",
                    toolName);
                return false;
            }

            if (!ControlWriteSupport.TryValidateAssetPathAllowed(outputPath, out string pathError))
            {
                errorResponse = ControlResponses.Error(pathError, "permission_denied", toolName);
                return false;
            }

            folderPath = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(folderPath))
            {
                errorResponse = ControlResponses.Error("outputPath must resolve under Assets/.", "invalid_argument", toolName);
                return false;
            }

            folderPath = folderPath.Replace('\\', '/');
            if (!ControlWriteSupport.TryValidateFolderPath(folderPath, out string folderError))
            {
                errorResponse = ControlResponses.Error(folderError, "permission_denied", toolName);
                return false;
            }

            if (AssetDatabase.IsValidFolder(outputPath))
            {
                errorResponse = ControlResponses.Error("outputPath points to an existing folder: " + outputPath, "invalid_argument", toolName);
                return false;
            }

            if (Directory.Exists(BuildAbsoluteProjectPath(outputPath)))
            {
                errorResponse = ControlResponses.Error("outputPath points to an existing folder: " + outputPath, "invalid_argument", toolName);
                return false;
            }

            return true;
        }

        private static bool TryResolveTextOutputPath(
            string rawPath,
            string toolName,
            out string outputPath,
            out string folderPath,
            out string absoluteOutputPath,
            out ControlToolCallResponse errorResponse)
        {
            absoluteOutputPath = null;
            if (!TryResolveOutputAssetPath(rawPath, null, toolName, out outputPath, out folderPath, out errorResponse))
            {
                return false;
            }

            if (outputPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                errorResponse = ControlResponses.Error("outputPath must not target a .meta file.", "invalid_argument", toolName);
                return false;
            }

            absoluteOutputPath = BuildAbsoluteProjectPath(outputPath);
            return true;
        }

        private static bool AssetExistsAtPath(string path)
        {
            return !string.IsNullOrEmpty(path) && AssetDatabase.LoadMainAssetAtPath(path) != null;
        }

        private static string GenerateUniqueDestinationPath(string destinationPath, HashSet<string> reservedPaths)
        {
            string folder = Path.GetDirectoryName(destinationPath).Replace('\\', '/');
            string fileName = Path.GetFileNameWithoutExtension(destinationPath);
            string extension = Path.GetExtension(destinationPath);

            string candidate = destinationPath;
            int index = 1;
            while (reservedPaths.Contains(candidate) || AssetExistsAtPath(candidate))
            {
                candidate = folder + "/" + fileName + " " + index.ToString(CultureInfo.InvariantCulture) + extension;
                index++;
            }

            return candidate;
        }

        private static bool TryResolveImportRequest(
            string rawPath,
            bool recursive,
            string toolName,
            out string normalizedPath,
            out string absolutePath,
            out List<string> importPaths,
            out ControlToolCallResponse errorResponse)
        {
            normalizedPath = null;
            absolutePath = null;
            importPaths = null;
            errorResponse = null;

            if (!ControlWriteSupport.TryNormalizeAssetPath(rawPath, out normalizedPath, out string normalizeError))
            {
                errorResponse = ControlResponses.Error(normalizeError, "invalid_argument", toolName);
                return false;
            }

            if (!ControlWriteSupport.TryValidateAssetPathAllowed(normalizedPath, out string pathError))
            {
                errorResponse = ControlResponses.Error(pathError, "permission_denied", toolName);
                return false;
            }

            if (normalizedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                errorResponse = ControlResponses.Error("paths must not target .meta files.", "invalid_argument", toolName);
                return false;
            }

            absolutePath = BuildAbsoluteProjectPath(normalizedPath);
            if (Directory.Exists(absolutePath))
            {
                if (!recursive)
                {
                    errorResponse = ControlResponses.Error(
                        "Folder import requires recursive=true: " + normalizedPath,
                        "invalid_argument",
                        toolName);
                    return false;
                }

                importPaths = ExpandImportFolder(normalizedPath, absolutePath);
                return true;
            }

            if (File.Exists(absolutePath))
            {
                importPaths = new List<string> { normalizedPath };
                return true;
            }

            errorResponse = ControlResponses.Error("Path not found under project: " + normalizedPath, "not_found", toolName);
            return false;
        }

        private static List<string> ExpandImportFolder(string normalizedFolderPath, string absoluteFolderPath)
        {
            var directories = new List<string> { normalizedFolderPath };
            string[] childDirectories = Directory.GetDirectories(absoluteFolderPath, "*", SearchOption.AllDirectories);
            for (int i = 0; i < childDirectories.Length; i++)
            {
                directories.Add(ToProjectRelativePath(childDirectories[i]));
            }

            directories = directories
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path.Count(ch => ch == '/'))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var files = new List<string>();
            string[] childFiles = Directory.GetFiles(absoluteFolderPath, "*", SearchOption.AllDirectories);
            for (int i = 0; i < childFiles.Length; i++)
            {
                string filePath = childFiles[i];
                if (filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                files.Add(ToProjectRelativePath(filePath));
            }

            files = files
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            directories.AddRange(files);
            return directories;
        }

        private static string BuildAbsoluteProjectPath(string relativePath)
        {
            string projectRoot = ControlUtil.GetProjectRootPath();
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            string projectRoot = Path.GetFullPath(ControlUtil.GetProjectRootPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(absolutePath);
            string relative = fullPath.Substring(projectRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
            return relative;
        }
    }
}
