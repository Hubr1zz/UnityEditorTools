#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using Object = UnityEngine.Object;
using static VFavorites.Libs.VUtils;


namespace VFavorites
{
    internal static class VFavoritesDataStorage
    {
        internal static VFavoritesData Load()
        {
            var cachedPath = EditorPrefsCached.GetString(VFavoritesConsts.LastKnownDataPathKeyPrefix + GetProjectId());
            var data = LoadFromAssets(cachedPath);
            if (data)
                return Remember(data);

            data = LoadFromAssets(VFavoritesConsts.DataAssetPath);
            if (data)
                return Remember(data);

            foreach (var guid in AssetDatabase.FindAssets("t:VFavoritesData", new[] { "Assets" }))
            {
                var path = guid.ToPath();
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;

                data = AssetDatabase.LoadAssetAtPath<VFavoritesData>(path);
                if (data)
                    return Remember(data);
            }

            return null;
        }

        internal static VFavoritesData GetOrCreate()
        {
            var data = Load();
            if (data)
                return data;

            var cachedPath = EditorPrefsCached.GetString(VFavoritesConsts.LastKnownDataPathKeyPrefix + GetProjectId());
            data = MigratePackageData(cachedPath);
            if (data)
                return data;

            var scriptPath = GetScriptPath("VFavorites");
            var oldDataPath = "";
            if (scriptPath.Contains("/"))
                oldDataPath = scriptPath.GetParentPath().CombinePath(VFavoritesConsts.DataFileName);

            if (oldDataPath != cachedPath)
                data = MigratePackageData(oldDataPath);
            if (data)
                return data;

            data = ScriptableObject.CreateInstance<VFavoritesData>();
            return CreateAsset(data, null);
        }

        static VFavoritesData LoadFromAssets(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                return null;

            return AssetDatabase.LoadAssetAtPath<VFavoritesData>(path);
        }

        static VFavoritesData MigratePackageData(string oldDataPath)
        {
            if (!oldDataPath.StartsWith("Packages/", StringComparison.Ordinal))
                return null;

            var oldData = AssetDatabase.LoadAssetAtPath<VFavoritesData>(oldDataPath);
            if (oldData)
                return CreateAsset(Object.Instantiate(oldData), oldDataPath);

            var packageDataPath = ResolvePackageDataPath(oldDataPath);
            if (!File.Exists(packageDataPath))
                return null;

            var targetPath = PrepareTargetPath();
            var targetFilePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, targetPath.Replace('/', Path.DirectorySeparatorChar));
            File.Copy(packageDataPath, targetFilePath, false);
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceSynchronousImport);

            var data = AssetDatabase.LoadAssetAtPath<VFavoritesData>(targetPath);
            if (!data)
                throw new InvalidOperationException($"Migrated vFavorites data at '{oldDataPath}' could not be loaded from '{targetPath}'. The copied file was kept for inspection.");

            Remember(data);
            Debug.Log($"Migrated vFavorites data from '{oldDataPath}' to '{targetPath}'.");
            return data;
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

        static VFavoritesData CreateAsset(VFavoritesData data, string sourcePath)
        {
            try
            {
                var targetPath = PrepareTargetPath();
                AssetDatabase.CreateAsset(data, targetPath);
                if (!AssetDatabase.Contains(data))
                    throw new InvalidOperationException($"Failed to persist vFavorites data at '{targetPath}'.");

                Remember(data);
                if (!string.IsNullOrEmpty(sourcePath))
                    Debug.Log($"Migrated vFavorites data from '{sourcePath}' to '{targetPath}'.");
                return data;
            }
            catch
            {
                if (data && !AssetDatabase.Contains(data))
                    Object.DestroyImmediate(data);

                throw;
            }
        }

        static string PrepareTargetPath()
        {
            if (!AssetDatabase.IsValidFolder(VFavoritesConsts.DataFolderPath))
                AssetDatabase.CreateFolder("Assets", "Editor");
            if (!AssetDatabase.IsValidFolder(VFavoritesConsts.DataFolderPath))
                throw new InvalidOperationException($"Could not create '{VFavoritesConsts.DataFolderPath}' for vFavorites data.");

            var existingGuid = AssetDatabase.AssetPathToGUID(VFavoritesConsts.DataAssetPath);
            var existingFilePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, VFavoritesConsts.DataAssetPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(existingFilePath) || !string.IsNullOrEmpty(existingGuid))
                throw new InvalidOperationException($"The vFavorites data target '{VFavoritesConsts.DataAssetPath}' already exists. It was not overwritten.");

            var targetPath = AssetDatabase.GenerateUniqueAssetPath(VFavoritesConsts.DataAssetPath);
            if (targetPath != VFavoritesConsts.DataAssetPath)
                throw new InvalidOperationException($"The vFavorites data target '{VFavoritesConsts.DataAssetPath}' already exists. It was not overwritten.");

            return targetPath;
        }

        static VFavoritesData Remember(VFavoritesData data)
        {
            EditorPrefsCached.SetString(VFavoritesConsts.LastKnownDataPathKeyPrefix + GetProjectId(), AssetDatabase.GetAssetPath(data));
            return data;
        }
    }
}
#endif
