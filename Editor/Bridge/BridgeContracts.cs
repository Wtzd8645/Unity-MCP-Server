using System;
using UnityEngine;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    [Serializable]
    internal class BridgeToolCallRequest
    {
        public string name;
        public string argumentsJson;
    }

    [Serializable]
    internal class BridgeToolCallResponse
    {
        public bool isError;
        public string contentText;
        public string structuredContentJson;
    }

    [Serializable]
    internal class ErrorStructuredContent
    {
        public string status;
        public string message;
        public string tool;
    }

    [Serializable]
    internal class NotImplementedStructuredContent
    {
        public string status;
        public string tool;
    }

    [Serializable]
    internal class PingStructuredContent
    {
        public bool connected;
        public string bridgeVersion;
        public string serverTimeUtc;
        public PingEditorState editor;
    }

    [Serializable]
    internal class PingEditorState
    {
        public bool isResponding;
        public bool isPlaying;
        public bool isPaused;
        public bool isCompiling;
    }

    [Serializable]
    internal class ProjectInfoArgs
    {
        public bool includePlatformMatrix;
    }

    [Serializable]
    internal class ProjectInfoStructuredContent
    {
        public string projectName;
        public string projectPath;
        public string unityVersion;
        public string companyName;
        public string productName;
        public string activeBuildTarget;
        public string activeBuildTargetGroup;
        public ProjectInfoEditorState editorState;
        public string[] supportedBuildTargets;
    }

    [Serializable]
    internal class ProjectInfoEditorState
    {
        public bool isPlaying;
        public bool isPaused;
        public bool isCompiling;
        public bool isUpdating;
    }

    [Serializable]
    internal class PlaymodeStatusStructuredContent
    {
        public bool isPlaying;
        public bool isPaused;
        public bool isChangingPlaymode;
    }

    [Serializable]
    internal class PlaymodeTransitionArgs
    {
        public bool waitForEntered;
        public bool waitForExited;
        public int timeoutMs;
    }

    [Serializable]
    internal class PlaymodeTransitionResult
    {
        public bool entered;
        public bool stopped;
        public string stateBefore;
        public string stateAfter;
        public int elapsedMs;
        public bool waitRequested;
        public int timeoutMs;
    }

    [Serializable]
    internal class ListScenesArgs
    {
        public string source;
        public bool includeDisabled;
        public int limit;
        public int offset;
    }

    [Serializable]
    internal class ListScenesResult
    {
        public int total;
        public SceneListItem[] items;
    }

    [Serializable]
    internal class SceneListItem
    {
        public string path;
        public string name;
        public string guid;
        public bool inBuildSettings;
        public bool enabledInBuildSettings;
        public bool hasEnabledInBuildSettings;
        public int buildIndex;
        public bool hasBuildIndex;
    }

    [Serializable]
    internal class OpenSceneArgs
    {
        public string scenePath;
        public string openMode;
        public bool saveModifiedScenes;
        public bool setActive;
    }

    [Serializable]
    internal class OpenSceneResult
    {
        public string openedScenePath;
        public string activeScenePath;
        public string[] loadedScenes;
    }

    [Serializable]
    internal class GetConsoleLogsArgs
    {
        public string[] levels;
        public string sinceId;
        public int limit;
        public string order;
        public bool includeStackTrace;
    }

    [Serializable]
    internal class ConsoleLogsResult
    {
        public string nextSinceId;
        public int returned;
        public ConsoleLogItem[] items;
    }

    [Serializable]
    internal class ConsoleLogItem
    {
        public string id;
        public string level;
        public string message;
        public string stackTrace;
        public string timestampUtc;
    }

    [Serializable]
    internal class ClearConsoleResult
    {
        public bool cleared;
        public bool clearedEditorConsole;
    }

    internal sealed class BridgeLogEntry
    {
        public long id;
        public string level;
        public string message;
        public string stackTrace;
        public string timestampUtc;
    }

    [Serializable]
    internal class GoFindArgs
    {
        public string scenePath;
        public string namePattern;
        public string tag;
        public int layer = -1;
        public bool isActive;
        public string[] hasComponents;
        public string hierarchyPathPrefix;
        public bool inSelection;
        public int limit;
        public int offset;
    }

    [Serializable]
    internal class GoFindResult
    {
        public int total;
        public GoFindItem[] items;
    }

    [Serializable]
    internal class GoFindItem
    {
        public string globalObjectId;
        public string scenePath;
        public string hierarchyPath;
        public string name;
        public bool activeSelf;
        public string tag;
        public int layer;
        public string[] componentTypes;
    }

    [Serializable]
    internal class ComponentGetFieldsArgs
    {
        public GameObjectRef target;
        public string componentType;
        public string componentId;
        public bool includePrivateSerialized;
    }

    [Serializable]
    internal class GameObjectRef
    {
        public string globalObjectId;
        public string scenePath;
        public string hierarchyPath;
    }

    [Serializable]
    internal class ComponentGetFieldsResult
    {
        public string componentId;
        public string componentType;
        public ComponentFieldItem[] fields;
    }

    [Serializable]
    internal class ComponentFieldItem
    {
        public string name;
        public string fieldType;
        public string value;
        public bool serialized;
        public bool readOnly;
    }

    [Serializable]
    internal class AssetRef
    {
        public string guid;
        public string path;
    }

    [Serializable]
    internal class AssetSearchArgs
    {
        public string query;
        public string[] types;
        public string[] labels;
        public string[] pathPrefixes;
        public bool includePackages;
        public int limit;
        public int offset;
        public string sortBy;
        public string sortOrder;
    }

    [Serializable]
    internal class AssetSearchResult
    {
        public int total;
        public AssetSearchItem[] items;
    }

    [Serializable]
    internal class AssetSearchItem
    {
        public string guid;
        public string path;
        public string name;
        public string type;
        public string[] labels;
        public bool isMainAsset;
        public string modifiedTimeUtc;
    }

    [Serializable]
    internal class AssetGetArgs
    {
        public AssetRef target;
        public bool includeDependencies;
        public bool includeDependents;
        public bool includeMeta;
    }

    [Serializable]
    internal class AssetGetResult
    {
        public AssetGetAsset asset;
        public AssetRefNode[] dependencies;
        public AssetRefNode[] dependents;
        public AssetMeta meta;
    }

    [Serializable]
    internal class AssetGetAsset
    {
        public string guid;
        public string path;
        public string name;
        public string type;
        public long fileSizeBytes;
        public bool hasFileSizeBytes;
        public string[] labels;
        public string importerType;
    }

    [Serializable]
    internal class AssetMeta
    {
        public string guid;
        public string assetPath;
        public string metaPath;
        public string metaContent;
    }

    [Serializable]
    internal class AssetRefNode
    {
        public string guid;
        public string path;
        public string type;
    }

    [Serializable]
    internal class AssetRefsArgs
    {
        public AssetRef target;
        public string direction;
        public bool recursive;
        public int maxDepth;
        public string[] filterTypes;
    }

    [Serializable]
    internal class AssetRefsResult
    {
        public AssetRefNode[] nodes;
        public AssetRefEdge[] edges;
    }

    [Serializable]
    internal class AssetRefEdge
    {
        public string fromGuid;
        public string toGuid;
        public string kind;
    }

    [Serializable]
    internal class Vec3Input
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    internal class TransformInput
    {
        public Vec3Input position;
        public Vec3Input rotationEuler;
        public Vec3Input scale;
        public bool local = true;
    }

    [Serializable]
    internal class GoCreateComponentInput
    {
        public string type;
    }

    [Serializable]
    internal class GoCreateArgs
    {
        public string scenePath;
        public string name;
        public GameObjectRef parent;
        public TransformInput transform;
        public GoCreateComponentInput[] components;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class GoDeleteArgs
    {
        public GameObjectRef[] targets;
        public string mode = "undoable";
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class GoDuplicateArgs
    {
        public GameObjectRef[] targets;
        public GameObjectRef parent;
        public string renamePattern;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class GoReparentArgs
    {
        public GameObjectRef[] targets;
        public GameObjectRef newParent;
        public bool worldPositionStays = true;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class GoRenameArgs
    {
        public GameObjectRef target;
        public string newName;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class GoSetActiveArgs
    {
        public GameObjectRef[] targets;
        public bool active;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class ComponentAddArgs
    {
        public GameObjectRef target;
        public string componentType;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class ComponentRemoveArgs
    {
        public GameObjectRef target;
        public string componentType;
        public string componentId;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class ComponentSetFieldsArgs
    {
        public GameObjectRef target;
        public string componentType;
        public string componentId;
        public bool strict = true;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class PrefabCreateArgs
    {
        public GameObjectRef source;
        public string outputPath;
        public bool connectToInstance = true;
        public bool overwrite;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class PrefabInstantiateArgs
    {
        public AssetRef prefab;
        public string scenePath;
        public GameObjectRef parent;
        public TransformInput transform;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class PrefabApplyOverridesArgs
    {
        public GameObjectRef[] instances;
        public bool includePropertyOverrides = true;
        public bool includeAddedComponents = true;
        public bool includeRemovedComponents = true;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class PrefabRevertOverridesArgs
    {
        public GameObjectRef[] instances;
        public bool includePropertyOverrides = true;
        public bool includeAddedComponents = true;
        public bool includeRemovedComponents = true;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class PrefabUnpackArgs
    {
        public GameObjectRef[] instances;
        public string mode = "OutermostRoot";
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class PrefabCreateVariantArgs
    {
        public AssetRef basePrefab;
        public string outputPath;
        public GameObjectRef sourceInstance;
        public bool overwrite;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetMoveArgs
    {
        public AssetRef[] targets;
        public string destinationFolder;
        public string conflictPolicy = "fail";
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetRenameArgs
    {
        public AssetRef target;
        public string newName;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetDeleteToTrashArgs
    {
        public AssetRef[] targets;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetReimportArgs
    {
        public AssetRef[] targets;
        public bool recursive;
        public bool forceUpdate;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetSetLabelsArgs
    {
        public AssetRef target;
        public string mode;
        public string[] labels;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class MutationItem
    {
        public string target;
        public string action;
        public string status;
        public bool changed;
        public string message;
        public string scenePath;
        public string hierarchyPath;
        public string path;
        public string guid;
        public string globalObjectId;
        public string componentType;
        public string componentId;
    }

    [Serializable]
    internal class MutationResult
    {
        public string tool;
        public bool dryRun;
        public bool applied;
        public int requested;
        public int succeeded;
        public int failed;
        public MutationItem[] items;
        public string[] warnings;
    }

    [Serializable]
    internal class RunTestsArgs
    {
        public string mode = "EditMode";
        public RunTestsFilter filter;
        public int timeoutMs = 600000;
        public bool includePassed;
        public bool includeXmlReportPath = true;
    }

    [Serializable]
    internal class RunTestsFilter
    {
        public string[] assemblyNames;
        public string[] testNames;
        public string[] categoryNames;
    }

    [Serializable]
    internal class RunTestsResult
    {
        public string runId;
        public string mode;
        public RunTestsSummary summary;
        public RunTestCaseResult[] results;
        public RunTestsArtifacts artifacts;
    }

    [Serializable]
    internal class RunTestsSummary
    {
        public int total;
        public int passed;
        public int failed;
        public int skipped;
        public int inconclusive;
        public int durationMs;
    }

    [Serializable]
    internal class RunTestCaseResult
    {
        public string fullName;
        public string outcome;
        public int durationMs;
        public string message;
        public string stackTrace;
        public string filePath;
        public int line;
        public bool hasLine;
    }

    [Serializable]
    internal class RunTestsArtifacts
    {
        public string xmlReportPath;
    }

    internal struct PaginationRange
    {
        public int offset;
        public int limit;
    }
}
