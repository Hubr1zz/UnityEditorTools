#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using static VHierarchy.Libs.VUtils;

namespace VHierarchy
{
    internal static class VHierarchyDataStorage
    {
        internal static VHierarchyData LoadData() => Load<VHierarchyData>(VHierarchyConsts.LastKnownDataPathKey, VHierarchyConsts.DataFileName);

        internal static VHierarchyPalette LoadPalette() => Load<VHierarchyPalette>(VHierarchyConsts.LastKnownPalettePathKey, VHierarchyConsts.PaletteFileName);

        internal static VHierarchyData GetOrCreateData() => GetOrCreate<VHierarchyData>(VHierarchyConsts.LastKnownDataPathKey, VHierarchyConsts.DataFileName);

        internal static VHierarchyPalette GetOrCreatePalette() => GetOrCreate<VHierarchyPalette>(VHierarchyConsts.LastKnownPalettePathKey, VHierarchyConsts.PaletteFileName);

        static T Load<T>(string pathKey, string fileName) where T : ScriptableObject
        {
            var cachedPath = ProjectPrefs.GetString(pathKey);
            var asset = LoadFromAssets<T>(cachedPath);
            if (asset)
                return Remember(asset, pathKey);

            asset = LoadFromAssets(VHierarchyConsts.DataFolderPath + "/" + fileName);
            if (asset)
                return Remember(asset, pathKey);

            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                asset = LoadFromAssets<T>(path);
                if (asset)
                    return Remember(asset, pathKey);
            }

            asset = MigratePackageAsset<T>(cachedPath, fileName, pathKey);
            if (asset)
                return asset;

            return MigratePackageAsset<T>(GetScriptAdjacentPath(fileName), fileName, pathKey);
        }

        static T GetOrCreate<T>(string pathKey, string fileName) where T : ScriptableObject
        {
            var asset = Load<T>(pathKey, fileName);
            if (asset)
                return asset;

            return CreateAsset(ScriptableObject.CreateInstance<T>(), fileName, pathKey);
        }

        static T LoadFromAssets<T>(string path) where T : ScriptableObject
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return null;

            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        static T MigratePackageAsset<T>(string sourcePath, string fileName, string pathKey) where T : ScriptableObject
        {
            if (string.IsNullOrEmpty(sourcePath) || !sourcePath.StartsWith("Packages/", StringComparison.Ordinal))
                return null;

            var source = AssetDatabase.LoadAssetAtPath<T>(sourcePath);
            if (!source)
                return null;

            var asset = CreateAsset(Object.Instantiate(source), fileName, pathKey);
            Debug.Log($"Migrated zEditor asset from '{sourcePath}' to '{AssetDatabase.GetAssetPath(asset)}'.");
            return asset;
        }

        static T CreateAsset<T>(T asset, string fileName, string pathKey) where T : ScriptableObject
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(VHierarchyConsts.DataFolderPath))
                    AssetDatabase.CreateFolder("Assets", "Editor");
                if (!AssetDatabase.IsValidFolder(VHierarchyConsts.DataFolderPath))
                    throw new InvalidOperationException($"Could not create '{VHierarchyConsts.DataFolderPath}' for zEditor data.");

                var path = VHierarchyConsts.DataFolderPath + "/" + fileName;
                var projectDirectory = Directory.GetParent(Application.dataPath);
                if (projectDirectory == null)
                    throw new InvalidOperationException("Could not resolve the Unity project directory for zEditor data.");

                var filePath = Path.Combine(projectDirectory.FullName, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(filePath) || File.Exists(filePath + ".meta") || AssetDatabase.AssetPathToGUID(path) != "")
                    throw new InvalidOperationException($"The zEditor data target '{path}' already exists but could not be loaded. It was not overwritten.");

                AssetDatabase.CreateAsset(asset, path);
                if (!AssetDatabase.Contains(asset))
                    throw new InvalidOperationException($"Failed to persist zEditor data at '{path}'.");

                return Remember(asset, pathKey);
            }
            catch
            {
                if (asset && !AssetDatabase.Contains(asset))
                    Object.DestroyImmediate(asset);

                throw;
            }
        }

        static string GetScriptAdjacentPath(string fileName)
        {
            var scriptPath = GetScriptPath("VHierarchy");
            if (string.IsNullOrEmpty(scriptPath) || !scriptPath.Contains("/"))
                return "";

            return scriptPath.GetParentPath() + "/" + fileName;
        }

        static T Remember<T>(T asset, string pathKey) where T : ScriptableObject
        {
            var path = AssetDatabase.GetAssetPath(asset);
            if (path.StartsWith("Assets/", StringComparison.Ordinal))
                ProjectPrefs.SetString(pathKey, path);

            return asset;
        }
    }
}
#endif
