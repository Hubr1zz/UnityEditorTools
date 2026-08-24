#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Plugins.zEditorTools
{
    /// <summary>
    /// 保存并恢复 Scene Hierarchy、Prefab Stage Hierarchy 和 Project Browser 文件夹树的展开状态。
    /// 本脚本保持独立：不依赖任何项目工具类，方便作为 zEditorTools 的共享编辑器功能。
    /// </summary>
    [InitializeOnLoad]
    public static class SceneHierarchyExpansionState
    {
        // 最大记录层级。默认 5 层可以覆盖多数编辑需求，同时避免深层大场景产生过多路径。
        private const int MaxDepth = 5;

        // 自动恢复失败后的重试上限。Hierarchy 窗口刚创建时，内部 TreeView 可能还没初始化完成。
        private const int RetryLimit = 5;

        // 单次最多保存的展开对象数量。用于限制大场景中展开节点过多导致的保存/恢复开销。
        private const int MaxStoredExpandedObjects = 250;

        // 自动恢复前额外等待的 Editor Update 次数，用于避开场景刚打开时 Hierarchy 尚未稳定的阶段。
        private const int RestoreDelayTicks = 2;

        // 分帧预算。候选对象/路径越多，需要的 Editor update 次数自然越多。
        private const int SaveTransformBudgetPerTick = 80;
        private const int RestorePathBudgetPerTick = 50;
        private const int ProjectFolderBudgetPerTick = 50;

        // 自动保存/加载开关。需要完全手动控制时，可以把这些常量改为 false。
        private const bool AutoSaveOnSceneSaving = true;
        private const bool AutoSaveBeforeSceneClosing = true;
        private const bool AutoLoadOnSceneOpened = true;
        private const bool AutoSaveBeforePrefabClosing = true;
        private const bool AutoLoadOnPrefabOpened = true;
        private const bool AutoSaveBeforeAssemblyReload = true;
        private const bool AutoRestoreAfterPlayMode = true;
        private const bool AutoSaveProjectFoldersOnProjectChange = true;
        private const bool AutoLoadProjectFoldersOnEditorStart = true;

        private const string MenuRoot = "Tools/EditorTools/Expansion State/";
        private const string StoragePrefix = "zEditorTools.ExpansionState.";
        private const string ProjectFoldersStorageSuffix = ".ProjectFolders";
        private static bool LogTiming => EditorPrefs.GetBool(StoragePrefix + "LogTiming", false);

        private static int restoreRetryCount;
        private static int pendingRestoreDelayTicks;
        private static bool restoreScheduled;
        private static int projectRestoreRetryCount;
        private static int pendingProjectRestoreDelayTicks;
        private static bool projectRestoreScheduled;
        private static ExpansionSaveJob saveJob;
        private static ExpansionRestoreJob restoreJob;
        private static ProjectFolderRestoreJob projectFolderRestoreJob;

        private static int GetLegacyInstanceId(this UnityEngine.Object target)
        {
#if UNITY_6000_5_OR_NEWER
            return unchecked((int)EntityId.ToULong(target.GetEntityId()));
#else
            return target.GetInstanceID();
#endif
        }

        private static UnityEngine.Object LegacyInstanceIdToObject(int instanceId)
        {
#if UNITY_6000_5_OR_NEWER
            return EditorUtility.EntityIdToObject(EntityId.FromULong(unchecked((uint)instanceId)));
#else
            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }

        static SceneHierarchyExpansionState()
        {
            if (AutoSaveOnSceneSaving)
                EditorSceneManager.sceneSaving += OnSceneSaving;

            if (AutoLoadOnSceneOpened)
                EditorSceneManager.sceneOpened += OnSceneOpened;

            if (AutoSaveBeforeSceneClosing)
                EditorSceneManager.sceneClosing += OnSceneClosing;

            if (AutoSaveBeforePrefabClosing)
                PrefabStage.prefabStageClosing += OnPrefabStageClosing;

            if (AutoLoadOnPrefabOpened)
                PrefabStage.prefabStageOpened += OnPrefabStageOpened;

            if (AutoSaveBeforeAssemblyReload)
                AssemblyReloadEvents.beforeAssemblyReload += SaveAllExpansionStateImmediate;

            if (AutoRestoreAfterPlayMode)
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            if (AutoSaveProjectFoldersOnProjectChange)
                EditorApplication.projectChanged += SaveProjectFolderExpansionState;

            EditorApplication.quitting += SaveAllExpansionStateImmediate;
            EditorApplication.delayCall += () =>
            {
                ScheduleRestoreLoadedScenes(RestoreDelayTicks);
                if (AutoLoadProjectFoldersOnEditorStart)
                    ScheduleRestoreProjectFolders(RestoreDelayTicks);
            };
        }

        [MenuItem(MenuRoot + "Save All")]
        public static void SaveAllExpansionState()
        {
            ScheduleSaveExpansionState(true, true, true, false);
        }

        private static void SaveAllExpansionStateImmediate()
        {
            ScheduleSaveExpansionState(true, true, true, true);
        }

        [MenuItem(MenuRoot + "Load All")]
        public static void LoadAllExpansionState()
        {
            LoadLoadedScenesExpansionState();
            LoadCurrentPrefabStageExpansionState();
            LoadProjectFolderExpansionState();
        }

        [MenuItem(MenuRoot + "Save Loaded Scenes Expansion")]
        public static void SaveLoadedScenesExpansionState()
        {
            ScheduleSaveExpansionState(true, false, false, false);
        }

        [MenuItem(MenuRoot + "Load Loaded Scenes Expansion")]
        public static void LoadLoadedScenesExpansionState()
        {
            restoreRetryCount = 0;
            ScheduleRestoreLoadedScenes(0);
        }

        [MenuItem(MenuRoot + "Clear Loaded Scenes Saved State")]
        public static void ClearLoadedScenesSavedState()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (CanStoreScene(scene))
                    EditorPrefs.DeleteKey(GetStorageKey(scene));
            }

            Debug.Log("[SceneHierarchyExpansionState] Cleared saved hierarchy expansion state for loaded scenes.");
        }

        [MenuItem(MenuRoot + "Save Current Prefab Expansion")]
        public static void SaveCurrentPrefabStageExpansionState()
        {
            ScheduleSaveExpansionState(false, true, false, false);
        }

        [MenuItem(MenuRoot + "Load Current Prefab Expansion")]
        public static void LoadCurrentPrefabStageExpansionState()
        {
            restoreRetryCount = 0;
            ScheduleRestoreLoadedScenes(0);
        }

        [MenuItem(MenuRoot + "Clear Current Prefab Saved State")]
        public static void ClearCurrentPrefabStageSavedState()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (CanStorePrefabStage(prefabStage))
                EditorPrefs.DeleteKey(GetPrefabStorageKey(prefabStage));

            Debug.Log("[SceneHierarchyExpansionState] Cleared saved hierarchy expansion state for current prefab.");
        }

        [MenuItem(MenuRoot + "Save Project Folder Expansion")]
        public static void SaveProjectFolderExpansionState()
        {
            var expandedFolderPaths = ProjectBrowserReflection.GetExpandedFolderPaths();
            expandedFolderPaths.Sort(StringComparer.Ordinal);
            EditorPrefs.SetString(GetProjectFoldersStorageKey(), JsonUtility.ToJson(new ProjectFolderExpansionData
            {
                expandedFolderPaths = expandedFolderPaths
            }));

            if (LogTiming)
                Debug.Log($"[SceneHierarchyExpansionState] Saved Project Browser folder expansion state. folders={expandedFolderPaths.Count}.");
        }

        [MenuItem(MenuRoot + "Load Project Folder Expansion")]
        public static void LoadProjectFolderExpansionState()
        {
            ScheduleRestoreProjectFolders(0);
        }

        [MenuItem(MenuRoot + "Clear Project Folder Saved State")]
        public static void ClearProjectFolderSavedState()
        {
            EditorPrefs.DeleteKey(GetProjectFoldersStorageKey());
            Debug.Log("[SceneHierarchyExpansionState] Cleared saved Project Browser folder expansion state.");
        }

        [MenuItem(MenuRoot + "Debug Logging")]
        public static void ToggleDebugLogging()
        {
            EditorPrefs.SetBool(StoragePrefix + "LogTiming", !LogTiming);
        }

        [MenuItem(MenuRoot + "Debug Logging", true)]
        public static bool ValidateDebugLogging()
        {
            Menu.SetChecked(MenuRoot + "Debug Logging", LogTiming);
            return true;
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            SaveLoadedScenesExpansionState();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            restoreRetryCount = 0;
            ScheduleRestoreLoadedScenes(RestoreDelayTicks);
        }

        private static void OnSceneClosing(Scene scene, bool removingScene)
        {
            ScheduleSaveExpansionState(true, false, false, true);
        }

        private static void OnPrefabStageOpened(PrefabStage prefabStage)
        {
            restoreRetryCount = 0;
            ScheduleRestoreLoadedScenes(RestoreDelayTicks);
        }

        private static void OnPrefabStageClosing(PrefabStage prefabStage)
        {
            ScheduleSaveExpansionState(false, true, false, true);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                SaveAllExpansionStateImmediate();
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                restoreRetryCount = 0;
                ScheduleRestoreLoadedScenes(RestoreDelayTicks);
            }
        }

        private static void ScheduleSaveExpansionState(bool includeScenes, bool includePrefab, bool includeProjectFolders, bool completeImmediately)
        {
            double totalStart = EditorApplication.timeSinceStartup;
            double readExpandedStart = EditorApplication.timeSinceStartup;
            var expandedIds = SceneHierarchyReflection.GetExpandedInstanceIds();
            double readExpandedMs = ToMilliseconds(EditorApplication.timeSinceStartup - readExpandedStart);

            saveJob = new ExpansionSaveJob
            {
                expandedIds = expandedIds,
                totalStart = totalStart,
                readExpandedMs = readExpandedMs,
                saveProjectFoldersWhenDone = includeProjectFolders
            };

            if (includeScenes)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!CanStoreScene(scene))
                        continue;

                    saveJob.targets.Add(new ExpansionSaveTarget
                    {
                        storageKey = GetStorageKey(scene),
                        isPrefab = false,
                        roots = scene.GetRootGameObjects()
                    });
                }
            }

            if (includePrefab)
            {
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (CanStorePrefabStage(prefabStage))
                {
                    saveJob.targets.Add(new ExpansionSaveTarget
                    {
                        storageKey = GetPrefabStorageKey(prefabStage),
                        isPrefab = true,
                        roots = new[] { prefabStage.prefabContentsRoot }
                    });
                }
            }

            EditorApplication.update -= ProcessSaveJob;

            if (completeImmediately)
            {
                while (saveJob != null)
                    ProcessSaveJob(int.MaxValue);
                return;
            }

            EditorApplication.update += ProcessSaveJob;
        }

        private static void ProcessSaveJob()
        {
            ProcessSaveJob(SaveTransformBudgetPerTick);
        }

        private static void ProcessSaveJob(int transformBudget)
        {
            if (saveJob == null)
            {
                EditorApplication.update -= ProcessSaveJob;
                return;
            }

            saveJob.tickCount++;
            int remainingBudget = Mathf.Max(1, transformBudget);
            while (remainingBudget > 0 && saveJob.targetIndex < saveJob.targets.Count)
            {
                var target = saveJob.targets[saveJob.targetIndex];

                if (target.completed)
                {
                    saveJob.targetIndex++;
                    continue;
                }

                if (target.stack.Count == 0)
                {
                    if (target.rootIndex >= target.roots.Length || target.data.expandedTransformPaths.Count >= MaxStoredExpandedObjects)
                    {
                        CompleteSaveTarget(target);
                        continue;
                    }

                    var root = target.roots[target.rootIndex++];
                    if (root != null)
                        target.stack.Push(root.transform);

                    continue;
                }

                var transform = target.stack.Pop();
                remainingBudget--;
                CollectExpandedPathNonRecursive(transform, saveJob.expandedIds, target);
            }

            if (saveJob.targetIndex < saveJob.targets.Count)
                return;

            if (saveJob.saveProjectFoldersWhenDone)
                SaveProjectFolderExpansionState();

            LogSaveTiming(
                saveJob.savedSceneCount + saveJob.savedPrefabCount,
                saveJob.scannedTransformCount,
                saveJob.storedExpandedCount + saveJob.storedPrefabExpandedCount,
                saveJob.expandedIds.Count,
                saveJob.readExpandedMs,
                ToMilliseconds(EditorApplication.timeSinceStartup - saveJob.totalStart));

            if (LogTiming)
                Debug.Log($"[SceneHierarchyExpansionState] Save split over {saveJob.tickCount} editor update(s).");

            saveJob = null;
            EditorApplication.update -= ProcessSaveJob;
        }

        private static void CollectExpandedPathNonRecursive(Transform transform, HashSet<int> expandedIds, ExpansionSaveTarget target)
        {
            if (transform == null || target.data.expandedTransformPaths.Count >= MaxStoredExpandedObjects)
                return;

            saveJob.scannedTransformCount++;
            int depth = GetDepth(transform);
            if (depth > MaxDepth)
                return;

            if (transform.childCount > 0 && expandedIds.Contains(transform.gameObject.GetLegacyInstanceId()))
                target.data.expandedTransformPaths.Add(BuildTransformPath(transform));

            if (depth >= MaxDepth || target.data.expandedTransformPaths.Count >= MaxStoredExpandedObjects)
                return;

            for (int i = transform.childCount - 1; i >= 0; i--)
                target.stack.Push(transform.GetChild(i));
        }

        private static void CompleteSaveTarget(ExpansionSaveTarget target)
        {
            target.data.expandedTransformPaths.Sort(StringComparer.Ordinal);
            EditorPrefs.SetString(target.storageKey, JsonUtility.ToJson(target.data));
            target.completed = true;

            if (target.isPrefab)
            {
                saveJob.savedPrefabCount++;
                saveJob.storedPrefabExpandedCount += target.data.expandedTransformPaths.Count;
            }
            else
            {
                saveJob.savedSceneCount++;
                saveJob.storedExpandedCount += target.data.expandedTransformPaths.Count;
            }

            saveJob.targetIndex++;
        }

        private static void ScheduleRestoreLoadedScenes(int delayTicks)
        {
            // 合并短时间内的重复恢复请求，并从最后一次请求后重新等待。
            pendingRestoreDelayTicks = Mathf.Max(0, delayTicks);

            if (restoreScheduled)
                return;

            restoreScheduled = true;
            EditorApplication.update += RestoreLoadedScenesWhenReady;
        }

        private static void RestoreLoadedScenesWhenReady()
        {
            if (pendingRestoreDelayTicks > 0)
            {
                pendingRestoreDelayTicks--;
                return;
            }

            // 编译、资源刷新、播放模式切换期间不应用，避免和 Unity 自身重建 Hierarchy 的时机冲突。
            if (!IsEditorReadyForRestore())
            {
                RetryRestore();
                return;
            }

            // 如果 Hierarchy 内部对象还没准备好，延迟到后续 editor tick 再试。
            if (!SceneHierarchyReflection.CanSetExpandedState)
            {
                RetryRestore();
                return;
            }

            EditorApplication.update -= RestoreLoadedScenesWhenReady;
            restoreScheduled = false;
            BeginRestoreJob();
        }

        private static void BeginRestoreJob()
        {
            restoreJob = new ExpansionRestoreJob
            {
                totalStart = EditorApplication.timeSinceStartup
            };

            AddCurrentPrefabStageRestoreTarget(restoreJob);

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!CanStoreScene(scene))
                    continue;

                string json = EditorPrefs.GetString(GetStorageKey(scene), string.Empty);
                if (string.IsNullOrEmpty(json))
                    continue;

                var data = JsonUtility.FromJson<SceneExpansionData>(json);
                if (data == null || data.expandedTransformPaths == null)
                    continue;

                data.expandedTransformPaths.Sort(ComparePathDepthThenName);
                restoreJob.targets.Add(new ExpansionRestoreTarget
                {
                    scene = scene,
                    paths = data.expandedTransformPaths
                });
                restoreJob.loadedTargetCount++;
                restoreJob.candidatePathCount += data.expandedTransformPaths.Count;
            }

            EditorApplication.update -= ProcessRestoreJob;
            EditorApplication.update += ProcessRestoreJob;
        }

        private static void AddCurrentPrefabStageRestoreTarget(ExpansionRestoreJob job)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!CanStorePrefabStage(prefabStage))
                return;

            string json = EditorPrefs.GetString(GetPrefabStorageKey(prefabStage), string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            var data = JsonUtility.FromJson<SceneExpansionData>(json);
            if (data == null || data.expandedTransformPaths == null)
                return;

            data.expandedTransformPaths.Sort(ComparePathDepthThenName);
            job.targets.Add(new ExpansionRestoreTarget
            {
                isPrefab = true,
                prefabRoot = prefabStage.prefabContentsRoot.transform,
                paths = data.expandedTransformPaths
            });
            job.loadedTargetCount++;
            job.candidatePathCount += data.expandedTransformPaths.Count;
        }

        private static void ProcessRestoreJob()
        {
            if (restoreJob == null)
            {
                EditorApplication.update -= ProcessRestoreJob;
                return;
            }

            int remainingBudget = RestorePathBudgetPerTick;
            while (remainingBudget > 0 && restoreJob.targetIndex < restoreJob.targets.Count)
            {
                var target = restoreJob.targets[restoreJob.targetIndex];
                if (target.pathIndex >= target.paths.Count)
                {
                    restoreJob.targetIndex++;
                    continue;
                }

                string transformPath = target.paths[target.pathIndex++];
                remainingBudget--;

                double resolveStart = EditorApplication.timeSinceStartup;
                var transform = target.isPrefab
                    ? ResolveTransformPath(target.prefabRoot, transformPath)
                    : ResolveTransformPath(target.scene, transformPath);
                restoreJob.resolveMs += ToMilliseconds(EditorApplication.timeSinceStartup - resolveStart);
                if (transform == null)
                    continue;

                restoreJob.resolvedPathCount++;

                double applyStart = EditorApplication.timeSinceStartup;
                if (SceneHierarchyReflection.SetExpanded(transform.gameObject.GetLegacyInstanceId(), true))
                    restoreJob.restoredCount++;
                restoreJob.applyMs += ToMilliseconds(EditorApplication.timeSinceStartup - applyStart);
            }

            restoreJob.tickCount++;

            if (restoreJob.targetIndex < restoreJob.targets.Count)
                return;

            var completedJob = restoreJob;
            restoreJob = null;
            EditorApplication.update -= ProcessRestoreJob;

            if (completedJob.restoredCount == 0 && completedJob.candidatePathCount > 0 && restoreRetryCount < RetryLimit)
            {
                RetryRestore();
                return;
            }

            EditorApplication.RepaintHierarchyWindow();
            restoreRetryCount = 0;

            LogRestoreTiming(
                completedJob.loadedTargetCount,
                completedJob.candidatePathCount,
                completedJob.resolvedPathCount,
                completedJob.restoredCount,
                completedJob.resolveMs,
                completedJob.applyMs,
                ToMilliseconds(EditorApplication.timeSinceStartup - completedJob.totalStart));

            if (LogTiming)
                Debug.Log($"[SceneHierarchyExpansionState] Restore split over {completedJob.tickCount} editor update(s).");
        }

        private static void RetryRestore()
        {
            restoreRetryCount++;
            if (restoreRetryCount <= RetryLimit)
                ScheduleRestoreLoadedScenes(RestoreDelayTicks);
            else
            {
                EditorApplication.update -= RestoreLoadedScenesWhenReady;
                restoreScheduled = false;
                pendingRestoreDelayTicks = 0;
            }
        }

        private static void ScheduleRestoreProjectFolders(int delayTicks)
        {
            pendingProjectRestoreDelayTicks = Mathf.Max(0, delayTicks);

            if (projectRestoreScheduled)
                return;

            projectRestoreScheduled = true;
            EditorApplication.update += RestoreProjectFoldersWhenReady;
        }

        private static void RestoreProjectFoldersWhenReady()
        {
            if (pendingProjectRestoreDelayTicks > 0)
            {
                pendingProjectRestoreDelayTicks--;
                return;
            }

            if (!IsEditorReadyForRestore() || !ProjectBrowserReflection.CanSetExpandedState)
            {
                RetryProjectFolderRestore();
                return;
            }

            string json = EditorPrefs.GetString(GetProjectFoldersStorageKey(), string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                StopProjectFolderRestore();
                return;
            }

            var data = JsonUtility.FromJson<ProjectFolderExpansionData>(json);
            if (data == null || data.expandedFolderPaths == null)
            {
                StopProjectFolderRestore();
                return;
            }

            data.expandedFolderPaths.Sort(StringComparer.Ordinal);
            projectFolderRestoreJob = new ProjectFolderRestoreJob
            {
                paths = data.expandedFolderPaths,
                totalStart = EditorApplication.timeSinceStartup
            };

            EditorApplication.update -= ProcessProjectFolderRestoreJob;
            EditorApplication.update += ProcessProjectFolderRestoreJob;
            EditorApplication.update -= RestoreProjectFoldersWhenReady;
            projectRestoreScheduled = false;
            pendingProjectRestoreDelayTicks = 0;
        }

        private static void ProcessProjectFolderRestoreJob()
        {
            if (projectFolderRestoreJob == null)
            {
                EditorApplication.update -= ProcessProjectFolderRestoreJob;
                return;
            }

            int remainingBudget = ProjectFolderBudgetPerTick;
            while (remainingBudget > 0 && projectFolderRestoreJob.pathIndex < projectFolderRestoreJob.paths.Count)
            {
                string path = projectFolderRestoreJob.paths[projectFolderRestoreJob.pathIndex++];
                remainingBudget--;
                if (ProjectBrowserReflection.SetExpandedFolderPath(path))
                    projectFolderRestoreJob.restoredCount++;
            }

            projectFolderRestoreJob.tickCount++;

            if (projectFolderRestoreJob.pathIndex < projectFolderRestoreJob.paths.Count)
                return;

            var completedJob = projectFolderRestoreJob;
            projectFolderRestoreJob = null;
            EditorApplication.update -= ProcessProjectFolderRestoreJob;

            if (completedJob.restoredCount == 0 && completedJob.paths.Count > 0 && projectRestoreRetryCount < RetryLimit)
            {
                RetryProjectFolderRestore();
                return;
            }

            projectRestoreRetryCount = 0;
            EditorApplication.RepaintProjectWindow();

            if (LogTiming)
                Debug.Log($"[SceneHierarchyExpansionState] Restored Project Browser folder expansion state. restored={completedJob.restoredCount}, savedPaths={completedJob.paths.Count}, ticks={completedJob.tickCount}, total={ToMilliseconds(EditorApplication.timeSinceStartup - completedJob.totalStart):F2}ms.");
        }

        private static void RetryProjectFolderRestore()
        {
            projectRestoreRetryCount++;
            if (projectRestoreRetryCount <= RetryLimit)
                ScheduleRestoreProjectFolders(RestoreDelayTicks);
            else
                StopProjectFolderRestore();
        }

        private static void StopProjectFolderRestore()
        {
            EditorApplication.update -= RestoreProjectFoldersWhenReady;
            EditorApplication.update -= ProcessProjectFolderRestoreJob;
            projectRestoreScheduled = false;
            pendingProjectRestoreDelayTicks = 0;
            projectFolderRestoreJob = null;
        }

        private static bool IsEditorReadyForRestore()
        {
            return !EditorApplication.isCompiling
                   && !EditorApplication.isUpdating
                   && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void RestoreCurrentPrefabStageExpansion(
            ref int loadedSceneCount,
            ref int candidatePathCount,
            ref int resolvedPathCount,
            ref int restoredCount,
            ref double resolveMs,
            ref double applyMs)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!CanStorePrefabStage(prefabStage))
                return;

            string json = EditorPrefs.GetString(GetPrefabStorageKey(prefabStage), string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            var data = JsonUtility.FromJson<SceneExpansionData>(json);
            if (data == null || data.expandedTransformPaths == null)
                return;

            loadedSceneCount++;
            candidatePathCount += data.expandedTransformPaths.Count;

            data.expandedTransformPaths.Sort(ComparePathDepthThenName);
            foreach (string transformPath in data.expandedTransformPaths)
            {
                double resolveStart = EditorApplication.timeSinceStartup;
                var transform = ResolveTransformPath(prefabStage.prefabContentsRoot.transform, transformPath);
                resolveMs += ToMilliseconds(EditorApplication.timeSinceStartup - resolveStart);
                if (transform == null)
                    continue;

                resolvedPathCount++;

                double applyStart = EditorApplication.timeSinceStartup;
                if (SceneHierarchyReflection.SetExpanded(transform.gameObject.GetLegacyInstanceId(), true))
                    restoredCount++;
                applyMs += ToMilliseconds(EditorApplication.timeSinceStartup - applyStart);
            }
        }

        private static void CollectExpandedPaths(Transform transform, HashSet<int> expandedIds, List<string> paths, ref int scannedTransformCount)
        {
            if (transform == null)
                return;

            scannedTransformCount++;

            // 超过限制层级就不继续递归，控制保存和恢复成本。
            if (GetDepth(transform) > MaxDepth)
                return;

            if (transform.childCount > 0 && expandedIds.Contains(transform.gameObject.GetLegacyInstanceId()))
            {
                if (paths.Count >= MaxStoredExpandedObjects)
                    return;

                paths.Add(BuildTransformPath(transform));
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                if (paths.Count >= MaxStoredExpandedObjects)
                    break;

                CollectExpandedPaths(transform.GetChild(i), expandedIds, paths, ref scannedTransformCount);
            }
        }

        private static void LogSaveTiming(
            int targetCount,
            int scannedTransformCount,
            int storedExpandedCount,
            int editorExpandedIdCount,
            double readExpandedMs,
            double totalMs)
        {
            if (!LogTiming)
                return;

            Debug.Log(
                $"[SceneHierarchyExpansionState] Save timing: total={totalMs:F2}ms, readExpandedIds={readExpandedMs:F2}ms, " +
                $"targets={targetCount}, scannedObjects={scannedTransformCount}, savedExpanded={storedExpandedCount}, " +
                $"editorExpandedIds={editorExpandedIdCount}, maxSaved={MaxStoredExpandedObjects}, maxDepth={MaxDepth}.");
        }

        private static void LogRestoreTiming(
            int sceneCount,
            int candidatePathCount,
            int resolvedPathCount,
            int restoredCount,
            double resolveMs,
            double applyMs,
            double totalMs)
        {
            if (!LogTiming)
                return;

            Debug.Log(
                $"[SceneHierarchyExpansionState] Restore timing: total={totalMs:F2}ms, resolvePaths={resolveMs:F2}ms, applyExpanded={applyMs:F2}ms, " +
                $"scenes={sceneCount}, savedPaths={candidatePathCount}, resolvedPaths={resolvedPathCount}, restored={restoredCount}, " +
                $"retry={restoreRetryCount}/{RetryLimit}.");
        }

        private static void LogPrefabSaveTiming(
            int scannedTransformCount,
            int storedExpandedCount,
            int editorExpandedIdCount,
            double readExpandedMs,
            double totalMs)
        {
            if (!LogTiming)
                return;

            Debug.Log(
                $"[SceneHierarchyExpansionState] Prefab save timing: total={totalMs:F2}ms, readExpandedIds={readExpandedMs:F2}ms, " +
                $"scannedObjects={scannedTransformCount}, savedExpanded={storedExpandedCount}, " +
                $"editorExpandedIds={editorExpandedIdCount}, maxSaved={MaxStoredExpandedObjects}, maxDepth={MaxDepth}.");
        }

        private static double ToMilliseconds(double seconds)
        {
            return seconds * 1000d;
        }

        private static string BuildTransformPath(Transform transform)
        {
            var segments = new List<string>(MaxDepth + 1);
            var current = transform;

            while (current != null)
            {
                segments.Add(BuildPathSegment(current));
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string BuildPathSegment(Transform transform)
        {
            int sameNameIndex = GetSameNameIndex(transform);
            int siblingIndex = transform.GetSiblingIndex();

            // name + 同名序号 + siblingIndex 共同组成完整路径段。恢复时必须全部匹配，不做模糊降级。
            return Uri.EscapeDataString(transform.name) + "#" + sameNameIndex + "@" + siblingIndex;
        }

        private static Transform ResolveTransformPath(Scene scene, string transformPath)
        {
            if (string.IsNullOrEmpty(transformPath))
                return null;

            string[] segments = transformPath.Split('/');
            if (segments.Length == 0 || segments.Length > MaxDepth + 1)
                return null;

            Transform current = null;
            var roots = scene.GetRootGameObjects();

            for (int i = 0; i < segments.Length; i++)
            {
                if (!TryParsePathSegment(segments[i], out string name, out int sameNameIndex, out int siblingIndex))
                    return null;

                // 严格按完整路径段匹配，避免同名对象被误展开。
                current = i == 0
                    ? FindRoot(roots, name, sameNameIndex, siblingIndex)
                    : FindChild(current, name, sameNameIndex, siblingIndex);

                if (current == null)
                    return null;
            }

            return current;
        }

        private static Transform ResolveTransformPath(Transform root, string transformPath)
        {
            if (root == null || string.IsNullOrEmpty(transformPath))
                return null;

            string[] segments = transformPath.Split('/');
            if (segments.Length == 0 || segments.Length > MaxDepth + 1)
                return null;

            Transform current = null;
            for (int i = 0; i < segments.Length; i++)
            {
                if (!TryParsePathSegment(segments[i], out string name, out int sameNameIndex, out int siblingIndex))
                    return null;

                current = i == 0
                    ? FindPrefabRoot(root, name, sameNameIndex, siblingIndex)
                    : FindChild(current, name, sameNameIndex, siblingIndex);

                if (current == null)
                    return null;
            }

            return current;
        }

        private static Transform FindPrefabRoot(Transform root, string name, int sameNameIndex, int siblingIndex)
        {
            return root != null
                   && root.name == name
                   && sameNameIndex == 0
                   && siblingIndex == root.GetSiblingIndex()
                ? root
                : null;
        }

        private static Transform FindRoot(GameObject[] roots, string name, int sameNameIndex, int siblingIndex)
        {
            if (siblingIndex < 0 || siblingIndex >= roots.Length)
                return null;

            var rootAtSibling = roots[siblingIndex];
            if (rootAtSibling == null || rootAtSibling.name != name)
                return null;

            int seenSameName = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null || root.name != name)
                    continue;

                if (root == rootAtSibling)
                    return seenSameName == sameNameIndex ? root.transform : null;

                seenSameName++;
            }

            return null;
        }

        private static Transform FindChild(Transform parent, string name, int sameNameIndex, int siblingIndex)
        {
            if (parent == null)
                return null;

            if (siblingIndex < 0 || siblingIndex >= parent.childCount)
                return null;

            var childAtSibling = parent.GetChild(siblingIndex);
            if (childAtSibling == null || childAtSibling.name != name)
                return null;

            int seenSameName = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name != name)
                    continue;

                if (child == childAtSibling)
                    return seenSameName == sameNameIndex ? child : null;

                seenSameName++;
            }

            return null;
        }

        private static bool TryParsePathSegment(string segment, out string name, out int sameNameIndex, out int siblingIndex)
        {
            name = string.Empty;
            sameNameIndex = 0;
            siblingIndex = -1;

            int hashIndex = segment.LastIndexOf('#');
            int atIndex = segment.LastIndexOf('@');
            if (hashIndex <= 0 || atIndex <= hashIndex)
                return false;

            name = Uri.UnescapeDataString(segment.Substring(0, hashIndex));
            return int.TryParse(segment.Substring(hashIndex + 1, atIndex - hashIndex - 1), out sameNameIndex)
                   && int.TryParse(segment.Substring(atIndex + 1), out siblingIndex);
        }

        private static int GetSameNameIndex(Transform transform)
        {
            int index = 0;

            if (transform.parent == null)
            {
                var roots = transform.gameObject.scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    var root = roots[i];
                    if (root == transform.gameObject)
                        return index;

                    if (root != null && root.name == transform.name)
                        index++;
                }

                return index;
            }

            for (int i = 0; i < transform.parent.childCount; i++)
            {
                var child = transform.parent.GetChild(i);
                if (child == transform)
                    return index;

                if (child.name == transform.name)
                    index++;
            }

            return index;
        }

        private static int GetDepth(Transform transform)
        {
            int depth = 0;
            var current = transform;
            while (current.parent != null)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        private static bool CanStoreScene(Scene scene)
        {
            return scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.path);
        }

        private static bool CanStorePrefabStage(PrefabStage prefabStage)
        {
            return prefabStage != null
                   && prefabStage.prefabContentsRoot != null
                   && !string.IsNullOrEmpty(GetPrefabAssetPath(prefabStage));
        }

        private static string GetStorageKey(Scene scene)
        {
            string sceneId = AssetDatabase.AssetPathToGUID(scene.path);
            if (string.IsNullOrEmpty(sceneId))
                sceneId = scene.path;

            // 同一个工程复制到不同目录时，project hash 可以避免 EditorPrefs 键冲突。
            return StoragePrefix + GetProjectHash() + "." + sceneId;
        }

        private static string GetPrefabStorageKey(PrefabStage prefabStage)
        {
            string prefabPath = GetPrefabAssetPath(prefabStage);
            string prefabId = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(prefabId))
                prefabId = prefabPath;

            return StoragePrefix + GetProjectHash() + ".Prefab." + prefabId;
        }

        private static string GetPrefabAssetPath(PrefabStage prefabStage)
        {
            if (prefabStage == null)
                return string.Empty;

#if UNITY_2021_2_OR_NEWER
            return prefabStage.assetPath;
#else
        return prefabStage.prefabAssetPath;
#endif
        }

        private static string GetProjectFoldersStorageKey()
        {
            return StoragePrefix + GetProjectHash() + ProjectFoldersStorageSuffix;
        }

        private static string GetProjectHash()
        {
            using (var md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(Application.dataPath));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (byte value in bytes)
                    builder.Append(value.ToString("x2"));

                return builder.ToString();
            }
        }

        private static int ComparePathDepthThenName(string a, string b)
        {
            int depthCompare = GetPathDepth(a).CompareTo(GetPathDepth(b));
            return depthCompare != 0 ? depthCompare : string.CompareOrdinal(a, b);
        }

        private static int GetPathDepth(string path)
        {
            if (string.IsNullOrEmpty(path))
                return 0;

            int depth = 0;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == '/')
                    depth++;
            }

            return depth;
        }

        [Serializable]
        private sealed class SceneExpansionData
        {
            public List<string> expandedTransformPaths = new List<string>();
        }

        [Serializable]
        private sealed class ProjectFolderExpansionData
        {
            public List<string> expandedFolderPaths = new List<string>();
        }

        private sealed class ExpansionSaveJob
        {
            public readonly List<ExpansionSaveTarget> targets = new List<ExpansionSaveTarget>();
            public HashSet<int> expandedIds = new HashSet<int>();
            public int targetIndex;
            public int tickCount;
            public int savedSceneCount;
            public int savedPrefabCount;
            public int scannedTransformCount;
            public int storedExpandedCount;
            public int storedPrefabExpandedCount;
            public bool saveProjectFoldersWhenDone;
            public double totalStart;
            public double readExpandedMs;
        }

        private sealed class ExpansionSaveTarget
        {
            public string storageKey;
            public bool isPrefab;
            public GameObject[] roots = new GameObject[0];
            public int rootIndex;
            public readonly Stack<Transform> stack = new Stack<Transform>();
            public readonly SceneExpansionData data = new SceneExpansionData();
            public bool completed;
        }

        private sealed class ExpansionRestoreJob
        {
            public readonly List<ExpansionRestoreTarget> targets = new List<ExpansionRestoreTarget>();
            public int targetIndex;
            public int loadedTargetCount;
            public int candidatePathCount;
            public int resolvedPathCount;
            public int restoredCount;
            public int tickCount;
            public double resolveMs;
            public double applyMs;
            public double totalStart;
        }

        private sealed class ExpansionRestoreTarget
        {
            public bool isPrefab;
            public Scene scene;
            public Transform prefabRoot;
            public List<string> paths = new List<string>();
            public int pathIndex;
        }

        private sealed class ProjectFolderRestoreJob
        {
            public List<string> paths = new List<string>();
            public int pathIndex;
            public int restoredCount;
            public int tickCount;
            public double totalStart;
        }

        private static class ProjectBrowserReflection
        {
            private const int ReflectionSearchDepth = 6;

            private static readonly Type ProjectBrowserType =
                typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");

            private static readonly BindingFlags InstanceFlags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            private static readonly BindingFlags StaticFlags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            public static bool CanSetExpandedState
            {
                get
                {
                    object treeObject = GetProjectFolderTreeObject();
                    return FindMethodOwner(treeObject, "SetExpanded", ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out _, out _)
                           || TryFindExpandedIds(treeObject, ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out _);
                }
            }

            public static List<string> GetExpandedFolderPaths()
            {
                var result = new List<string>();
                object treeObject = GetProjectFolderTreeObject();
                if (treeObject == null)
                    return result;

                if (!TryFindExpandedIds(treeObject, ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out IList expandedIds))
                    return result;

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (object item in expandedIds)
                {
                    if (!(item is int id))
                        continue;

                    var asset = LegacyInstanceIdToObject(id);
                    if (asset == null)
                        continue;

                    string path = AssetDatabase.GetAssetPath(asset);
                    if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                        continue;

                    if (seen.Add(path))
                        result.Add(path);
                }

                return result;
            }

            public static int SetExpandedFolderPaths(List<string> folderPaths)
            {
                object treeObject = GetProjectFolderTreeObject();
                if (treeObject == null || folderPaths == null)
                    return 0;

                int restoredCount = 0;
                foreach (string path in folderPaths)
                {
                    if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                        continue;

                    var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
                    if (folder == null)
                        continue;

                    int id = folder.GetLegacyInstanceId();
                    if (SetExpanded(treeObject, id, true))
                        restoredCount++;
                }

                SafeCall(() => treeObject.GetType().InvokeMember("ReloadData", BindingFlags.InvokeMethod | InstanceFlags, null, treeObject, null));
                return restoredCount;
            }

            public static bool SetExpandedFolderPath(string path)
            {
                object treeObject = GetProjectFolderTreeObject();
                if (treeObject == null || string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                    return false;

                var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
                if (folder == null)
                    return false;

                bool expanded = SetExpanded(treeObject, folder.GetLegacyInstanceId(), true);
                SafeCall(() => treeObject.GetType().InvokeMember("ReloadData", BindingFlags.InvokeMethod | InstanceFlags, null, treeObject, null));
                return expanded;
            }

            private static bool SetExpanded(object treeObject, int instanceId, bool expanded)
            {
                if (FindMethodOwner(treeObject, "SetExpanded", ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out object owner, out MethodInfo method))
                {
                    method.Invoke(owner, new object[] { instanceId, expanded });
                    return true;
                }

                return TrySetExpandedId(treeObject, instanceId, expanded);
            }

            private static object GetProjectFolderTreeObject()
            {
                if (ProjectBrowserType == null)
                    return null;

                var browsers = GetProjectBrowsers();
                if (browsers == null || browsers.Count == 0)
                {
                    EditorUtility.FocusProjectWindow();
                    browsers = GetProjectBrowsers();
                }

                if (browsers == null || browsers.Count == 0)
                    return null;

                object browser = browsers[0];
                object folderTree = GetFieldValue(browser, "m_FolderTree");
                if (folderTree != null)
                    return folderTree;

                return GetFieldValue(browser, "m_AssetTree");
            }

            private static IList GetProjectBrowsers()
            {
                var field = ProjectBrowserType.GetField("s_ProjectBrowsers", StaticFlags);
                return field != null ? field.GetValue(null) as IList : null;
            }

            private static object GetFieldValue(object source, string fieldName)
            {
                if (source == null)
                    return null;

                var field = source.GetType().GetField(fieldName, InstanceFlags);
                return field != null ? SafeGet(() => field.GetValue(source)) : null;
            }

            private static bool TryFindExpandedIds(object source, int depth, HashSet<object> visited, out IList expandedIds)
            {
                expandedIds = null;
                if (!CanInspect(source, depth, visited))
                    return false;

                Type type = source.GetType();
                foreach (var field in type.GetFields(InstanceFlags))
                {
                    object value = SafeGet(() => field.GetValue(source));
                    if (IsExpandedIdsMember(field.Name, value, out expandedIds))
                        return true;

                    if (ShouldTraverseMember(field.Name) && TryFindExpandedIds(value, depth - 1, visited, out expandedIds))
                        return true;
                }

                foreach (var property in type.GetProperties(InstanceFlags))
                {
                    if (property.GetIndexParameters().Length > 0)
                        continue;

                    object value = SafeGet(() => property.GetValue(source, null));
                    if (IsExpandedIdsMember(property.Name, value, out expandedIds))
                        return true;

                    if (ShouldTraverseMember(property.Name) && TryFindExpandedIds(value, depth - 1, visited, out expandedIds))
                        return true;
                }

                return false;
            }

            private static bool TrySetExpandedId(object treeObject, int instanceId, bool expanded)
            {
                if (!TryFindExpandedIds(treeObject, ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out IList expandedIds))
                    return false;

                bool contains = false;
                foreach (object item in expandedIds)
                {
                    if (item is int id && id == instanceId)
                    {
                        contains = true;
                        break;
                    }
                }

                if (expanded)
                {
                    if (!contains)
                        expandedIds.Add(instanceId);

                    return true;
                }

                if (contains)
                    expandedIds.Remove(instanceId);

                return true;
            }

            private static bool FindMethodOwner(object source, string methodName, int depth, HashSet<object> visited, out object owner, out MethodInfo method)
            {
                owner = null;
                method = null;
                if (!CanInspect(source, depth, visited))
                    return false;

                Type type = source.GetType();
                method = type.GetMethod(methodName, InstanceFlags, null, new[] { typeof(int), typeof(bool) }, null);
                if (method != null)
                {
                    owner = source;
                    return true;
                }

                foreach (var field in type.GetFields(InstanceFlags))
                {
                    if (!ShouldTraverseMember(field.Name))
                        continue;

                    object value = SafeGet(() => field.GetValue(source));
                    if (FindMethodOwner(value, methodName, depth - 1, visited, out owner, out method))
                        return true;
                }

                foreach (var property in type.GetProperties(InstanceFlags))
                {
                    if (!ShouldTraverseMember(property.Name) || property.GetIndexParameters().Length > 0)
                        continue;

                    object value = SafeGet(() => property.GetValue(source, null));
                    if (FindMethodOwner(value, methodName, depth - 1, visited, out owner, out method))
                        return true;
                }

                return false;
            }

            private static bool IsExpandedIdsMember(string memberName, object value, out IList expandedIds)
            {
                expandedIds = null;
                if (!string.Equals(memberName, "expandedIDs", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (value is IList list)
                {
                    expandedIds = list;
                    return true;
                }

                return false;
            }

            private static bool ShouldTraverseMember(string memberName)
            {
                if (string.IsNullOrEmpty(memberName))
                    return false;

                string lower = memberName.ToLowerInvariant();
                return lower.Contains("folder")
                       || lower.Contains("asset")
                       || lower.Contains("treeview")
                       || lower.Contains("state")
                       || lower.Contains("data")
                       || lower == "m_rootitem";
            }

            private static bool CanInspect(object source, int depth, HashSet<object> visited)
            {
                if (source == null || depth < 0)
                    return false;

                Type type = source.GetType();
                if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                    return false;

                return visited.Add(source);
            }

            private static object SafeGet(Func<object> getter)
            {
                try
                {
                    return getter();
                }
                catch
                {
                    return null;
                }
            }

            private static void SafeCall(Action action)
            {
                try
                {
                    action();
                }
                catch
                {
                }
            }

            private sealed class ReferenceComparer : IEqualityComparer<object>
            {
                public static readonly ReferenceComparer Instance = new ReferenceComparer();

                public new bool Equals(object x, object y)
                {
                    return ReferenceEquals(x, y);
                }

                public int GetHashCode(object obj)
                {
                    return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
                }
            }
        }

        private static class SceneHierarchyReflection
        {
            private const int ReflectionSearchDepth = 6;

            private static readonly Type HierarchyWindowType =
                typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");

            private static readonly BindingFlags InstanceFlags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            public static bool CanSetExpandedState
            {
                get
                {
                    object hierarchyObject = GetSceneHierarchyObject();
                    return FindMethodOwner(hierarchyObject, "SetExpanded", ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out _, out _)
                           || TryFindExpandedIds(hierarchyObject, ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out _);
                }
            }

            public static HashSet<int> GetExpandedInstanceIds()
            {
                var result = new HashSet<int>();
                object hierarchyObject = GetSceneHierarchyObject();
                if (hierarchyObject == null)
                    return result;

                if (!TryFindExpandedIds(hierarchyObject, ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out IList expandedIds))
                    return result;

                foreach (object item in expandedIds)
                {
                    if (item is int id)
                        result.Add(id);
                }

                return result;
            }

            public static bool SetExpanded(int instanceId, bool expanded)
            {
                object hierarchyObject = GetSceneHierarchyObject();
                if (hierarchyObject == null)
                    return false;

                if (FindMethodOwner(hierarchyObject, "SetExpanded", ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out object owner, out MethodInfo method))
                {
                    method.Invoke(owner, new object[] { instanceId, expanded });
                    return true;
                }

                return TrySetExpandedId(hierarchyObject, instanceId, expanded);
            }

            private static object GetSceneHierarchyObject()
            {
                if (HierarchyWindowType == null)
                    return null;

                var windows = Resources.FindObjectsOfTypeAll(HierarchyWindowType);
                if (windows == null || windows.Length == 0)
                    return null;

                // Unity 2022 的 SceneHierarchyWindow 内部通常持有 m_SceneHierarchy。
                // 如果字段名变化，则回退到 window 自身继续搜索，降低版本差异导致的失败概率。
                object window = windows[0];
                var field = HierarchyWindowType.GetField("m_SceneHierarchy", InstanceFlags);
                return field != null ? field.GetValue(window) : window;
            }

            private static bool TryFindExpandedIds(object source, int depth, HashSet<object> visited, out IList expandedIds)
            {
                expandedIds = null;
                if (!CanInspect(source, depth, visited))
                    return false;

                Type type = source.GetType();
                foreach (var field in type.GetFields(InstanceFlags))
                {
                    object value = SafeGet(() => field.GetValue(source));
                    if (IsExpandedIdsMember(field.Name, value, out expandedIds))
                        return true;

                    if (ShouldTraverseMember(field.Name) && TryFindExpandedIds(value, depth - 1, visited, out expandedIds))
                        return true;
                }

                foreach (var property in type.GetProperties(InstanceFlags))
                {
                    if (property.GetIndexParameters().Length > 0)
                        continue;

                    object value = SafeGet(() => property.GetValue(source, null));
                    if (IsExpandedIdsMember(property.Name, value, out expandedIds))
                        return true;

                    if (ShouldTraverseMember(property.Name) && TryFindExpandedIds(value, depth - 1, visited, out expandedIds))
                        return true;
                }

                return false;
            }

            private static bool TrySetExpandedId(object hierarchyObject, int instanceId, bool expanded)
            {
                if (!TryFindExpandedIds(hierarchyObject, ReflectionSearchDepth, new HashSet<object>(ReferenceComparer.Instance), out IList expandedIds))
                    return false;

                bool contains = false;
                foreach (object item in expandedIds)
                {
                    if (item is int id && id == instanceId)
                    {
                        contains = true;
                        break;
                    }
                }

                if (expanded)
                {
                    if (!contains)
                        expandedIds.Add(instanceId);

                    return true;
                }

                if (contains)
                    expandedIds.Remove(instanceId);

                return true;
            }

            private static bool FindMethodOwner(object source, string methodName, int depth, HashSet<object> visited, out object owner, out MethodInfo method)
            {
                owner = null;
                method = null;
                if (!CanInspect(source, depth, visited))
                    return false;

                Type type = source.GetType();
                method = type.GetMethod(methodName, InstanceFlags, null, new[] { typeof(int), typeof(bool) }, null);
                if (method != null)
                {
                    owner = source;
                    return true;
                }

                foreach (var field in type.GetFields(InstanceFlags))
                {
                    if (!ShouldTraverseMember(field.Name))
                        continue;

                    object value = SafeGet(() => field.GetValue(source));
                    if (FindMethodOwner(value, methodName, depth - 1, visited, out owner, out method))
                        return true;
                }

                foreach (var property in type.GetProperties(InstanceFlags))
                {
                    if (!ShouldTraverseMember(property.Name) || property.GetIndexParameters().Length > 0)
                        continue;

                    object value = SafeGet(() => property.GetValue(source, null));
                    if (FindMethodOwner(value, methodName, depth - 1, visited, out owner, out method))
                        return true;
                }

                return false;
            }

            private static bool IsExpandedIdsMember(string memberName, object value, out IList expandedIds)
            {
                expandedIds = null;
                if (!string.Equals(memberName, "expandedIDs", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (value is IList list)
                {
                    expandedIds = list;
                    return true;
                }

                return false;
            }

            private static bool ShouldTraverseMember(string memberName)
            {
                if (string.IsNullOrEmpty(memberName))
                    return false;

                // 限制反射搜索范围，只进入可能承载 TreeView 状态的成员，避免扫描整个编辑器对象图。
                string lower = memberName.ToLowerInvariant();
                return lower.Contains("scenehierarchy")
                       || lower.Contains("treeview")
                       || lower.Contains("state")
                       || lower.Contains("data")
                       || lower == "m_rootitem";
            }

            private static bool CanInspect(object source, int depth, HashSet<object> visited)
            {
                if (source == null || depth < 0)
                    return false;

                Type type = source.GetType();
                if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                    return false;

                return visited.Add(source);
            }

            private static object SafeGet(Func<object> getter)
            {
                try
                {
                    return getter();
                }
                catch
                {
                    return null;
                }
            }

            private sealed class ReferenceComparer : IEqualityComparer<object>
            {
                public static readonly ReferenceComparer Instance = new ReferenceComparer();

                public new bool Equals(object x, object y)
                {
                    return ReferenceEquals(x, y);
                }

                public int GetHashCode(object obj)
                {
                    return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
                }
            }
        }
    }
}
#endif
