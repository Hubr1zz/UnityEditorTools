#if UNITY_EDITOR
using System;
namespace VTabs
{
    internal static class VTabsConsts
    {
        internal const string DefaultDataFolderPath = "Assets/Editor";
        internal const string DataFolderPathPreferenceKey = "zEditorTools.DataFolderPath.";

        internal static string ProjectDataFolderPath => GetConfiguredDataFolderPath();

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
