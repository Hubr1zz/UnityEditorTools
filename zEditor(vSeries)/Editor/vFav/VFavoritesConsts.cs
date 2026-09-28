#if UNITY_EDITOR
namespace VFavorites
{
    internal static class VFavoritesConsts
    {
        internal const string DataFileName = "vFavorites Data.asset";
        internal const string DataFolderPath = "Assets/Editor";
        internal const string DataAssetPath = DataFolderPath + "/" + DataFileName;
        internal const string LastKnownDataPathKeyPrefix = "vFavorites-lastKnownDataPath-";
    }
}
#endif
