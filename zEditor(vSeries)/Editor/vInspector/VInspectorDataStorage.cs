#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using static VInspector.Libs.VUtils;

namespace VInspector
{
    internal static class VInspectorDataStorage
    {
        internal static VInspectorData Load()
        {
            var cachedPath = ProjectPrefs.GetString(VInspectorConsts.LastKnownDataPathKey);
            var asset = LoadFromAssets(cachedPath);
            if (asset)
                return Remember(asset);

            asset = LoadFromAssets(VInspectorConsts.DataFolderPath + "/" + VInspectorConsts.DataFileName);
            if (asset)
                return Remember(asset);

            foreach (var guid in AssetDatabase.FindAssets("t:VInspectorData", new[] { "Assets" }))
            {
                asset = LoadFromAssets(AssetDatabase.GUIDToAssetPath(guid));
                if (asset)
                    return Remember(asset);
            }

            asset = MigratePackageData(cachedPath);
            if (asset)
                return asset;

            return MigratePackageData(GetScriptAdjacentPath(VInspectorConsts.DataFileName));
        }

        internal static VInspectorData GetOrCreate()
        {
            var data = Load();
            if (data)
                return data;

            return CreateAsset(ScriptableObject.CreateInstance<VInspectorData>());
        }

        static VInspectorData LoadFromAssets(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return null;

            return AssetDatabase.LoadAssetAtPath<VInspectorData>(path);
        }

        static VInspectorData MigratePackageData(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !sourcePath.StartsWith("Packages/", StringComparison.Ordinal))
                return null;

            var source = AssetDatabase.LoadAssetAtPath<VInspectorData>(sourcePath);
            if (!source)
                return null;

            var data = CreateAsset(Object.Instantiate(source));
            Debug.Log($"Migrated vInspector data from '{sourcePath}' to '{AssetDatabase.GetAssetPath(data)}'.");
            return data;
        }

        static string GetScriptAdjacentPath(string fileName)
        {
            var scriptPath = GetScriptPath("VInspector");
            if (string.IsNullOrEmpty(scriptPath) || !scriptPath.Contains("/"))
                return "";

            return scriptPath.GetParentPath() + "/" + fileName;
        }

        static VInspectorData CreateAsset(VInspectorData data)
        {
            try
            {
                EnsureFolderPathExists(VInspectorConsts.DataFolderPath);

                var path = VInspectorConsts.DataFolderPath + "/" + VInspectorConsts.DataFileName;
                var projectDirectory = Directory.GetParent(Application.dataPath);
                if (projectDirectory == null)
                    throw new InvalidOperationException("Could not resolve the Unity project directory for vInspector data.");

                var filePath = Path.Combine(projectDirectory.FullName, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(filePath) || File.Exists(filePath + ".meta") || AssetDatabase.AssetPathToGUID(path) != "")
                    throw new InvalidOperationException($"The vInspector data target '{path}' already exists but could not be loaded. It was not overwritten.");

                AssetDatabase.CreateAsset(data, path);
                if (!AssetDatabase.Contains(data))
                    throw new InvalidOperationException($"Failed to persist vInspector data at '{path}'.");

                return Remember(data);
            }
            catch
            {
                if (data && !AssetDatabase.Contains(data))
                    Object.DestroyImmediate(data);

                throw;
            }
        }

        static VInspectorData Remember(VInspectorData data)
        {
            var path = AssetDatabase.GetAssetPath(data);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                return data;

            var targetPath = VInspectorConsts.DataFolderPath + "/" + VInspectorConsts.DataFileName;
            if (path != targetPath)
            {
                EnsureFolderPathExists(VInspectorConsts.DataFolderPath);
                var error = AssetDatabase.MoveAsset(path, targetPath);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException($"Could not move vInspector data from '{path}' to '{targetPath}': {error}");

                data = AssetDatabase.LoadAssetAtPath<VInspectorData>(targetPath);
                if (!data)
                    throw new InvalidOperationException($"Moved vInspector data could not be loaded from '{targetPath}'.");
            }

            ProjectPrefs.SetString(VInspectorConsts.LastKnownDataPathKey, targetPath);

            return data;
        }

        static void EnsureFolderPathExists(string folderPath)
        {
            var segments = folderPath.Split('/');
            if (segments.Length == 0 || segments[0] != "Assets")
                throw new InvalidOperationException($"The vInspector data folder '{folderPath}' must be inside Assets.");

            var parentPath = "Assets";
            for (var index = 1; index < segments.Length; index++)
            {
                var path = parentPath + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(path))
                    AssetDatabase.CreateFolder(parentPath, segments[index]);
                if (!AssetDatabase.IsValidFolder(path))
                    throw new InvalidOperationException($"Could not create '{path}' for vInspector data.");

                parentPath = path;
            }
        }
    }
}
#endif
