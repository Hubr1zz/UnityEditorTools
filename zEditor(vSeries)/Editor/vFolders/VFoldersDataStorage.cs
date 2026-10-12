#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using static VFolders.Libs.VUtils;


namespace VFolders
{
    internal static class VFoldersDataStorage
    {
        internal static VFoldersData LoadData() => Load<VFoldersData>(VFoldersConsts.LastKnownDataPathKey, VFoldersConsts.DataFileName);

        internal static VFoldersPalette LoadPalette()
        {
            return GetOrCreatePalette();
        }

        internal static VFoldersData GetOrCreateData() => GetOrCreate<VFoldersData>(VFoldersConsts.LastKnownDataPathKey, VFoldersConsts.DataFileName);

        internal static VFoldersPalette GetOrCreatePalette() => GetOrCreate<VFoldersPalette>(VFoldersConsts.LastKnownPalettePathKey, VFoldersConsts.PaletteFileName);

        static T Load<T>(string lastKnownPathKey, string fileName) where T : ScriptableObject
        {
            var asset = LoadFromAssets<T>(ProjectPrefs.GetString(lastKnownPathKey));
            if (asset)
                return Remember(asset, lastKnownPathKey);

            asset = LoadFromAssets<T>(VFoldersConsts.DataFolderPath + "/" + fileName);
            if (asset)
                return Remember(asset, lastKnownPathKey);

            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets" }))
            {
                if (string.IsNullOrEmpty(guid))
                    continue;

                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsAssetsPath(path))
                    continue;

                asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (!asset)
                    continue;

                return Remember(asset, lastKnownPathKey);
            }

            return null;
        }

        static T GetOrCreate<T>(string lastKnownPathKey, string fileName) where T : ScriptableObject
        {
            var asset = Load<T>(lastKnownPathKey, fileName);
            if (asset)
                return asset;

            var cachedPath = ProjectPrefs.GetString(lastKnownPathKey);
            asset = MigratePackageData<T>(cachedPath, fileName, lastKnownPathKey);
            if (asset)
                return asset;

            var packagePath = GetScriptAdjacentPath(fileName);
            if (packagePath != cachedPath)
                asset = MigratePackageData<T>(packagePath, fileName, lastKnownPathKey);
            if (asset)
                return asset;

            return CreateAsset(ScriptableObject.CreateInstance<T>(), fileName, lastKnownPathKey, null);
        }

        static T LoadFromAssets<T>(string path) where T : ScriptableObject
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (!IsAssetsPath(path))
                return null;

            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        static T MigratePackageData<T>(string oldDataPath, string fileName, string lastKnownPathKey) where T : ScriptableObject
        {
            if (!IsPackagesPath(oldDataPath))
                return null;

            var oldData = AssetDatabase.LoadAssetAtPath<T>(oldDataPath);
            if (oldData)
                return CreateAsset(Object.Instantiate(oldData), fileName, lastKnownPathKey, oldDataPath);

            var packageDataPath = ResolvePackageDataPath(oldDataPath);
            if (string.IsNullOrEmpty(packageDataPath) || !File.Exists(packageDataPath))
                return null;

            var targetPath = PrepareTargetPath(fileName);
            var targetFilePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, targetPath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                File.Copy(packageDataPath, targetFilePath, false);
                AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Failed to migrate vFolders asset from '{oldDataPath}' to '{targetPath}'. The package source was not modified and the copied target, if any, was left for inspection.", exception);
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(targetPath);
            if (!asset)
                throw new InvalidOperationException($"Migrated vFolders asset from '{oldDataPath}' could not be loaded from '{targetPath}'. The copied file was kept for inspection.");

            Remember(asset, lastKnownPathKey);
            Debug.Log($"Migrated vFolders asset from '{oldDataPath}' to '{targetPath}'.");
            return asset;
        }

        static string ResolvePackageDataPath(string assetPath)
        {
            var packageInfo = PackageInfo.FindForAssetPath(assetPath);
            if (packageInfo == null || string.IsNullOrEmpty(packageInfo.resolvedPath) || string.IsNullOrEmpty(packageInfo.assetPath))
                return "";
            if (assetPath != packageInfo.assetPath && !assetPath.StartsWith(packageInfo.assetPath + "/", StringComparison.Ordinal))
                return "";

            var relativePath = assetPath.Substring(packageInfo.assetPath.Length).TrimStart('/', '\\');
            return Path.Combine(packageInfo.resolvedPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        static T CreateAsset<T>(T asset, string fileName, string lastKnownPathKey, string sourcePath) where T : ScriptableObject
        {
            try
            {
                var targetPath = PrepareTargetPath(fileName);
                AssetDatabase.CreateAsset(asset, targetPath);
                if (!AssetDatabase.Contains(asset))
                    throw new InvalidOperationException($"Failed to persist vFolders asset at '{targetPath}'.");

                Remember(asset, lastKnownPathKey);
                if (!string.IsNullOrEmpty(sourcePath))
                    Debug.Log($"Migrated vFolders asset from '{sourcePath}' to '{targetPath}'.");
                return asset;
            }
            catch
            {
                if (asset && !AssetDatabase.Contains(asset))
                    Object.DestroyImmediate(asset);

                throw;
            }
        }

        static string PrepareTargetPath(string fileName)
        {
            EnsureFolderPathExists(VFoldersConsts.DataFolderPath);

            var targetPath = VFoldersConsts.DataFolderPath + "/" + fileName;
            var existingGuid = AssetDatabase.AssetPathToGUID(targetPath);
            var projectDirectory = Directory.GetParent(Application.dataPath);
            if (projectDirectory == null)
                throw new InvalidOperationException("Could not resolve the Unity project directory for vFolders data.");

            var existingFilePath = Path.Combine(projectDirectory.FullName, targetPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(existingFilePath) || File.Exists(existingFilePath + ".meta") || !string.IsNullOrEmpty(existingGuid))
                throw new InvalidOperationException($"The vFolders asset target '{targetPath}' already exists. It was not overwritten.");

            var uniqueTargetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
            if (uniqueTargetPath != targetPath)
                throw new InvalidOperationException($"The vFolders asset target '{targetPath}' already exists. It was not overwritten.");

            return targetPath;
        }

        static void EnsureFolderPathExists(string folderPath)
        {
            var segments = folderPath.Split('/');
            if (segments.Length == 0 || segments[0] != "Assets")
                throw new InvalidOperationException($"The vFolders data folder '{folderPath}' must be inside Assets.");

            var parentPath = "Assets";
            for (var index = 1; index < segments.Length; index++)
            {
                var path = parentPath + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(path))
                    AssetDatabase.CreateFolder(parentPath, segments[index]);
                if (!AssetDatabase.IsValidFolder(path))
                    throw new InvalidOperationException($"Could not create '{path}' for vFolders data.");

                parentPath = path;
            }
        }

        static string GetScriptAdjacentPath(string fileName)
        {
            var scriptPath = GetScriptPath("VFolders");
            if (string.IsNullOrEmpty(scriptPath) || !scriptPath.Contains("/"))
                return "";

            return scriptPath.GetParentPath() + "/" + fileName;
        }

        static bool IsAssetsPath(string path) => !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal);

        static bool IsPackagesPath(string path) => !string.IsNullOrEmpty(path) && path.StartsWith("Packages/", StringComparison.Ordinal);

        static T Remember<T>(T asset, string lastKnownPathKey) where T : ScriptableObject
        {
            var path = AssetDatabase.GetAssetPath(asset);
            if (!IsAssetsPath(path))
                return asset;

            var targetPath = VFoldersConsts.DataFolderPath + "/" + Path.GetFileName(path);
            if (path != targetPath)
            {
                EnsureFolderPathExists(VFoldersConsts.DataFolderPath);
                var error = AssetDatabase.MoveAsset(path, targetPath);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException($"Could not move vFolders data from '{path}' to '{targetPath}': {error}");

                asset = AssetDatabase.LoadAssetAtPath<T>(targetPath);
                if (!asset)
                    throw new InvalidOperationException($"Moved vFolders data could not be loaded from '{targetPath}'.");
            }

            ProjectPrefs.SetString(lastKnownPathKey, targetPath);
            return asset;
        }
    }
}
#endif
