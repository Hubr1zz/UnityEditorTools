#if UNITY_EDITOR
using System;
namespace VFavorites
{
    internal static class VFavoritesConsts
    {
        internal const string DefaultDataFolderPath = "Assets/Editor";
        internal const string DataFolderPathPreferenceKey = "zEditorTools.DataFolderPath.";
        internal const string DataFileName = "vFavorites Data.asset";
        internal const string LastKnownDataPathKeyPrefix = "vFavorites-lastKnownDataPath-";
        internal const int WindowsSpaceKey = 0x20;
        internal const int WindowsTabKey = 0x09;
        internal const int WindowsAltKey = 0x12;

        internal static string DataFolderPath => GetConfiguredDataFolderPath();
        internal static string DataAssetPath => DataFolderPath + "/" + DataFileName;

        static string GetConfiguredDataFolderPath()
        {
            var path = UnityEditor.EditorPrefs.GetString(DataFolderPathPreferenceKey + UnityEditor.PlayerSettings.productGUID, DefaultDataFolderPath).Replace('\\', '/').TrimEnd('/');
            if (path != "Assets" && !path.StartsWith("Assets/", StringComparison.Ordinal))
                return DefaultDataFolderPath;

            foreach (var segment in path.Split('/'))
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                    return DefaultDataFolderPath;

            return path;
        }
    }
}
#endif
