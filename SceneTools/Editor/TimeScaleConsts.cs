#if UNITY_EDITOR && UNITY_2021_2_OR_NEWER
namespace CameraFollowTool
{
    internal static class TimeScaleConsts
    {
        public const float MinScale = 0f;
        public const float MaxScale = 10f;
        public const float NormalScale = 1f;
        public const long SyncIntervalMilliseconds = 100;
        public const string ModifiedKey = "SceneTools.TimeScale.Modified";
        public const string OriginalScaleKey = "SceneTools.TimeScale.Original";
        public static readonly float[] Presets = { 0f, 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };
    }
}
#endif
