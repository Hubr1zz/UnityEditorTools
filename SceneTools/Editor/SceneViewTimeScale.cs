#if UNITY_EDITOR && UNITY_2021_2_OR_NEWER
using System.Globalization;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CameraFollowTool
{
    [Overlay(typeof(SceneView), OverlayId, "Time Scale")]
    [Icon("d_UnityEditor.AnimationWindow")]
    public class TimeScaleOverlay : ToolbarOverlay
    {
        public const string OverlayId = "time-scale-overlay";

        TimeScaleOverlay() : base(TimeScaleField.Id, TimeScalePresets.Id, TimeScaleReset.Id) { }

        [MenuItem("Window/Time Scale Overlay")]
        private static void ShowOverlay()
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                sceneView = EditorWindow.GetWindow<SceneView>();

            if (sceneView.TryGetOverlay(OverlayId, out var overlay))
                overlay.displayed = true;
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class TimeScaleField : FloatField
    {
        public const string Id = "TimeScale/Value";

        public TimeScaleField() : base("×")
        {
            tooltip = "Runtime time scale (0–10). 0 pauses scaled game time; 1 is normal speed.";
            isDelayed = true;
            style.width = 90;
            style.minWidth = 90;
            style.marginLeft = 0;
            style.marginRight = 0;
            labelElement.style.minWidth = 12;
            labelElement.style.width = 12;
            AddToClassList("unity-editor-toolbar-element");
            this.RegisterValueChangedCallback(evt =>
            {
                TimeScaleState.SetScale(evt.newValue);
                SetValueWithoutNotify(Time.timeScale);
            });
            Sync();
            schedule.Execute(Sync).Every(TimeScaleConsts.SyncIntervalMilliseconds);
        }

        private void Sync()
        {
            SetEnabled(TimeScaleState.CanAdjust);
            if (panel?.focusController.focusedElement is VisualElement focusedElement && Contains(focusedElement))
                return;

            SetValueWithoutNotify(Time.timeScale);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class TimeScalePresets : EditorToolbarDropdown
    {
        public const string Id = "TimeScale/Presets";

        public TimeScalePresets()
        {
            text = "Speed";
            icon = EditorGUIUtility.FindTexture("d_UnityEditor.AnimationWindow");
            tooltip = "Choose a runtime time scale";
            clicked += ShowPresets;
            SetEnabled(TimeScaleState.CanAdjust);
            schedule.Execute(() => SetEnabled(TimeScaleState.CanAdjust)).Every(TimeScaleConsts.SyncIntervalMilliseconds);
        }

        private void ShowPresets()
        {
            var menu = new GenericMenu();
            foreach (float scale in TimeScaleConsts.Presets)
                menu.AddItem(new GUIContent(scale.ToString("0.##", CultureInfo.InvariantCulture) + "×"), Mathf.Approximately(Time.timeScale, scale), () => TimeScaleState.SetScale(scale));

            menu.DropDown(worldBound);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class TimeScaleReset : EditorToolbarButton
    {
        public const string Id = "TimeScale/Reset";

        public TimeScaleReset()
        {
            text = "1×";
            icon = EditorGUIUtility.FindTexture("d_Refresh");
            tooltip = "Restore normal game speed (1×)";
            clicked += () => TimeScaleState.SetScale(TimeScaleConsts.NormalScale);
            SetEnabled(TimeScaleState.CanAdjust);
            schedule.Execute(() => SetEnabled(TimeScaleState.CanAdjust)).Every(TimeScaleConsts.SyncIntervalMilliseconds);
        }
    }

    [InitializeOnLoad]
    internal static class TimeScaleState
    {
        public static bool CanAdjust => EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode;

        static TimeScaleState()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void SetScale(float scale)
        {
            if (!CanAdjust || float.IsNaN(scale) || float.IsInfinity(scale))
                return;

            if (!SessionState.GetBool(TimeScaleConsts.ModifiedKey, false))
            {
                SessionState.SetFloat(TimeScaleConsts.OriginalScaleKey, Time.timeScale);
                SessionState.SetBool(TimeScaleConsts.ModifiedKey, true);
            }

            Time.timeScale = Mathf.Clamp(scale, TimeScaleConsts.MinScale, TimeScaleConsts.MaxScale);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(TimeScaleConsts.ModifiedKey, false))
                return;

            Time.timeScale = SessionState.GetFloat(TimeScaleConsts.OriginalScaleKey, TimeScaleConsts.NormalScale);
            SessionState.EraseBool(TimeScaleConsts.ModifiedKey);
            SessionState.EraseFloat(TimeScaleConsts.OriginalScaleKey);
        }
    }
}
#endif
