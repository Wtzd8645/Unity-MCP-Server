using System;
using UnityEngine;

namespace Blanketmen.UnityMcp.Control.Editor
{
    [Serializable]
    internal class ControlToolCallRequest
    {
        public string name;
        public string argumentsJson;
    }

    [Serializable]
    internal class ControlToolCallResponse
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
        public string controlVersion;
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
    internal class SceneListLoadedArgs
    {
    }

    [Serializable]
    internal class SceneGetActiveArgs
    {
    }

    [Serializable]
    internal class SceneInfoItem
    {
        public string path;
        public string name;
        public bool isLoaded;
        public bool isDirty;
        public bool isActive;
        public int rootCount;
        public int buildIndex;
        public bool hasBuildIndex;
    }

    [Serializable]
    internal class SceneListLoadedResult
    {
        public string activeScenePath;
        public SceneInfoItem[] items;
    }

    [Serializable]
    internal class SceneGetActiveResult
    {
        public string activeScenePath;
        public SceneInfoItem scene;
    }

    [Serializable]
    internal class SceneSetActiveArgs
    {
        public string scenePath;
    }

    [Serializable]
    internal class SceneSetActiveResult
    {
        public string activeScenePath;
        public string[] loadedScenes;
    }

    [Serializable]
    internal class SceneCloseArgs
    {
        public string scenePath;
        public bool removeScene = true;
        public bool saveModifiedScene;
    }

    [Serializable]
    internal class SceneCloseResult
    {
        public string closedScenePath;
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

    internal sealed class ControlLogEntry
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
    internal class GameObjectGetArgs
    {
        public GameObjectRef target;
        public bool includeChildren = true;
        public int childLimit = 100;
    }

    [Serializable]
    internal class Vec3Value
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    internal class GameObjectTransformSnapshot
    {
        public Vec3Value localPosition;
        public Vec3Value localRotationEuler;
        public Vec3Value localScale;
        public Vec3Value worldPosition;
        public Vec3Value worldRotationEuler;
        public Vec3Value lossyScale;
    }

    [Serializable]
    internal class GameObjectRelationItem
    {
        public string globalObjectId;
        public string hierarchyPath;
        public string name;
        public bool activeSelf;
    }

    [Serializable]
    internal class ComponentSummaryItem
    {
        public string componentId;
        public string componentType;
        public bool enabled;
        public bool hasEnabled;
        public int index;
    }

    [Serializable]
    internal class GameObjectGetResult
    {
        public string globalObjectId;
        public string scenePath;
        public string hierarchyPath;
        public string name;
        public bool activeSelf;
        public bool activeInHierarchy;
        public string tag;
        public int layer;
        public bool isStatic;
        public GameObjectRelationItem parent;
        public GameObjectRelationItem[] children;
        public bool childrenTruncated;
        public ComponentSummaryItem[] components;
        public GameObjectTransformSnapshot transform;
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
    internal class ComponentListArgs
    {
        public GameObjectRef target;
    }

    [Serializable]
    internal class ComponentListResult
    {
        public string globalObjectId;
        public string scenePath;
        public string hierarchyPath;
        public ComponentSummaryItem[] items;
    }

    [Serializable]
    internal class ComponentGetFieldsBatchArgs
    {
        public string[] componentIds;
        public bool includePrivateSerialized;
    }

    [Serializable]
    internal class ComponentGetFieldsBatchItem
    {
        public string componentId;
        public string componentType;
        public string targetGlobalObjectId;
        public string scenePath;
        public string hierarchyPath;
        public string status;
        public string message;
        public ComponentFieldItem[] fields;
    }

    [Serializable]
    internal class ComponentGetFieldsBatchResult
    {
        public int requested;
        public int succeeded;
        public int failed;
        public ComponentGetFieldsBatchItem[] items;
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
    internal class GameObjectSetTransformArgs
    {
        public GameObjectRef target;
        public TransformInput transform;
        public bool dryRun = true;
        public bool apply = false;
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
    internal class GameObjectSetTagArgs
    {
        public GameObjectRef[] targets;
        public string tag;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class GameObjectSetLayerArgs
    {
        public GameObjectRef[] targets;
        public int layer;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class GameObjectSetStaticArgs
    {
        public GameObjectRef[] targets;
        public bool isStatic;
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
    internal class SceneCreateArgs
    {
        public string outputPath;
        public string setup = "EmptyScene";
        public string openMode = "Additive";
        public bool setActive = true;
        public bool overwrite;
        public bool saveModifiedScenes;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class SceneSaveArgs
    {
        public string scenePath;
        public string outputPath;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class SceneSaveAllArgs
    {
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class PrefabGetArgs
    {
        public AssetRef prefab;
        public GameObjectRef instance;
    }

    [Serializable]
    internal class PrefabGetOverridesArgs
    {
        public GameObjectRef instance;
    }

    [Serializable]
    internal class PrefabAssetInfo
    {
        public string guid;
        public string path;
        public string name;
        public string assetType;
    }

    [Serializable]
    internal class PrefabInstanceInfo
    {
        public string globalObjectId;
        public string scenePath;
        public string hierarchyPath;
        public string name;
        public string status;
    }

    [Serializable]
    internal class PrefabOverrideSummary
    {
        public int propertyOverrideCount;
        public int addedComponentCount;
        public int removedComponentCount;
        public int addedGameObjectCount;
        public int removedGameObjectCount;
    }

    [Serializable]
    internal class PrefabGetResult
    {
        public string targetKind;
        public PrefabAssetInfo prefab;
        public PrefabInstanceInfo instance;
        public PrefabAssetInfo sourcePrefab;
        public PrefabOverrideSummary overrides;
    }

    [Serializable]
    internal class PrefabPropertyOverrideItem
    {
        public string targetGlobalObjectId;
        public string targetName;
        public string targetType;
        public string targetHierarchyPath;
        public string propertyPath;
        public string value;
    }

    [Serializable]
    internal class PrefabAddedComponentItem
    {
        public string componentId;
        public string componentType;
        public string gameObjectGlobalObjectId;
        public string gameObjectName;
        public string hierarchyPath;
    }

    [Serializable]
    internal class PrefabRemovedComponentItem
    {
        public string componentId;
        public string componentType;
        public string gameObjectGlobalObjectId;
        public string gameObjectName;
        public string hierarchyPath;
    }

    [Serializable]
    internal class PrefabAddedGameObjectItem
    {
        public string globalObjectId;
        public string name;
        public string hierarchyPath;
    }

    [Serializable]
    internal class PrefabRemovedGameObjectItem
    {
        public string globalObjectId;
        public string name;
        public string hierarchyPath;
    }

    [Serializable]
    internal class PrefabGetOverridesResult
    {
        public PrefabAssetInfo prefab;
        public PrefabInstanceInfo instance;
        public PrefabPropertyOverrideItem[] propertyOverrides;
        public PrefabAddedComponentItem[] addedComponents;
        public PrefabRemovedComponentItem[] removedComponents;
        public PrefabAddedGameObjectItem[] addedGameObjects;
        public PrefabRemovedGameObjectItem[] removedGameObjects;
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
    internal class AssetCopyArgs
    {
        public AssetRef[] targets;
        public string destinationFolder;
        public string conflictPolicy = "fail";
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetCreateFolderArgs
    {
        public string path;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetCreateMaterialArgs
    {
        public string outputPath;
        public string shaderName;
        public bool overwrite;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetCreateScriptableObjectArgs
    {
        public string typeName;
        public string outputPath;
        public bool overwrite;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetCreateTextArgs
    {
        public string outputPath;
        public string content;
        public bool overwrite;
        public bool dryRun = true;
        public bool apply = false;
    }

    [Serializable]
    internal class AssetImportArgs
    {
        public string[] paths;
        public bool recursive;
        public bool forceUpdate;
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
