#if UNITY_EDITOR
namespace VFavorites
{
    internal static class VFavoritesConsts
    {
        internal const string DataFileName = "vFavorites Data.asset";
        internal const string DataFolderPath = "Assets/Editor";
        internal const string DataAssetPath = DataFolderPath + "/" + DataFileName;
        internal const string LastKnownDataPathKeyPrefix = "vFavorites-lastKnownDataPath-";
        internal const int WindowsSpaceKey = 0x20;
        internal const int WindowsTabKey = 0x09;
        internal const int WindowsAltKey = 0x12;
    }
}
#endif
