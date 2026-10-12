#if UNITY_EDITOR
using System;
namespace VFolders
{
    internal static class VFoldersConsts
    {
        internal const string DefaultDataFolderPath = "Assets/Editor";
        internal const string DataFolderPathPreferenceKey = "zEditorTools.DataFolderPath.";
        internal const string DataFileName = "vFolders Data.asset";
        internal const string PaletteFileName = "vFolders Palette.asset";
        internal const string LastKnownDataPathKey = "vFolders-lastKnownDataPath";
        internal const string LastKnownPalettePathKey = "vFolders-lastKnownPalettePath";

        internal static string DataFolderPath => GetConfiguredDataFolderPath();

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
