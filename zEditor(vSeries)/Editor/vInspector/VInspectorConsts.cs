#if UNITY_EDITOR
using System;
namespace VInspector
{
    internal static class VInspectorConsts
    {
        internal const string DefaultDataFolderPath = "Assets/Editor";
        internal const string DataFolderPathPreferenceKey = "zEditorTools.DataFolderPath.";
        internal const string DataFileName = "vInspector Data.asset";
        internal const string LastKnownDataPathKey = "vInspector-lastKnownDataPath";

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
