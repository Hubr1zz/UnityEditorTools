#if UNITY_EDITOR && UNITY_2021_2_OR_NEWER
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace CameraFollowTool
{
    [Overlay(typeof(SceneView), OverlayId, "Root Rotate")]
    [Icon("d_RotateTool")]
    public class RootRotationOverlay : ToolbarOverlay
    {
        public const string OverlayId = "root-rotation-overlay";

        RootRotationOverlay() : base(
            RootRotationToolbarButton.Id,
            RootRotationLookAtToolbarButton.Id)
        { }

        [MenuItem("Window/Root Rotate Overlay")]
        private static void ShowOverlay()
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null && SceneView.sceneViews.Count > 0)
                sceneView = (SceneView)SceneView.sceneViews[0];
            if (sceneView == null) return;

            if (sceneView.TryGetOverlay(OverlayId, out var overlay))
                overlay.displayed = true;
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class RootRotationToolbarButton : VisualElement
    {
        public const string Id = "RootRotation/Toggle";

        private readonly Button _button;

        public RootRotationToolbarButton()
        {
            var container = SplitButtonHelper.CreateContainer();
            Add(container);

            _button = SplitButtonHelper.CreateHalf(
                "Use the active selection as rotation root", "d_RotateTool", true);
            _button.style.width = 28;
            _button.style.borderTopRightRadius = 0.5f;
            _button.style.borderBottomRightRadius = 0.5f;
            _button.clicked += ToggleRoot;
            container.Add(_button);

            schedule.Execute(Sync).Every(100);
        }

        private void ToggleRoot()
        {
            if (RootRotationState.HasRoot)
            {
                RootRotationState.ClearRoot();
                return;
            }

            var selected = Selection.activeTransform;
            if (selected == null) return;

            RootRotationState.SetRoot(selected);
            ShowToast("Root Set!");
        }

        private void ShowToast(string message)
        {
            if (_button.panel == null) return;

            Rect buttonBounds = _button.worldBound;
            var anchor = new Rect(
                buttonBounds.center.x - 44f,
                buttonBounds.yMin - 32f,
                88f,
                1f);
            UnityEditor.PopupWindow.Show(
                anchor, new RootSelectionToastPopup(message));
        }

        private void Sync()
        {
            bool active = RootRotationState.HasRoot;
            SplitButtonHelper.SetActive(_button, active);
            SplitButtonHelper.SetInteractive(
                _button, active || Selection.activeTransform != null);

            _button.tooltip = active
                ? "Clear rotation root: " + RootRotationState.Root.name
                : "Use the active selection as rotation root";
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class RootRotationLookAtToolbarButton : VisualElement
    {
        public const string Id = "RootRotation/LookAt";

        private readonly Button _button;

        public RootRotationLookAtToolbarButton()
        {
            _button = SplitButtonHelper.CreateHalf(
                "Look selected objects at the rotation root", "d_ViewToolOrbit", false);
            _button.style.width = 28;
            _button.clicked += LookAtRoot;
            Add(_button);

            schedule.Execute(Sync).Every(100);
        }

        private void LookAtRoot()
        {
            int count = RootRotationState.LookSelectedObjectsAtRoot();
            if (count > 0)
                ShowToast("Look At " + count + " object" + (count == 1 ? "" : "s"));
        }

        private void ShowToast(string message)
        {
            if (_button.panel == null) return;

            Rect buttonBounds = _button.worldBound;
            var anchor = new Rect(
                buttonBounds.center.x - 44f,
                buttonBounds.yMin - 32f,
                88f,
                1f);
            UnityEditor.PopupWindow.Show(
                anchor, new RootSelectionToastPopup(message));
        }

        private void Sync()
        {
            bool canLookAt = RootRotationState.CanLookAtRoot;
            SplitButtonHelper.SetInteractive(_button, canLookAt);
            _button.tooltip = canLookAt
                ? "Look selected objects at the rotation root"
                : "Set a rotation root and select other objects";
        }
    }

    public class RootSelectionToastPopup : PopupWindowContent
    {
        private readonly string _message;
        private double _closeAt;

        public RootSelectionToastPopup(string message)
        {
            _message = message;
        }

        public override Vector2 GetWindowSize() => new Vector2(88f, 26f);

        public override void OnOpen()
        {
            _closeAt = EditorApplication.timeSinceStartup + 1.6d;
            EditorApplication.update += CloseWhenDue;
        }

        public override void OnClose()
        {
            EditorApplication.update -= CloseWhenDue;
        }

        public override void OnGUI(Rect rect)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };
            GUI.Label(rect, _message, style);
        }

        private void CloseWhenDue()
        {
            if (EditorApplication.timeSinceStartup < _closeAt) return;
            EditorApplication.update -= CloseWhenDue;
            if (editorWindow != null) editorWindow.Close();
        }
    }

    [InitializeOnLoad]
    public static class RootRotationState
    {
        public static Transform Root { get; private set; }
        public static bool HasRoot => Root != null;
        public static bool CanLookAtRoot => Root != null && GetLookAtTargets().Length > 0;

        private static Quaternion _handleRotation;
        private static bool _previousToolsHidden;
        private static bool _ownsToolsHidden;
        private static GUIStyle _rootLabelStyle;

        static RootRotationState()
        {
            EditorApplication.hierarchyChanged += ValidateRoot;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }

        public static void SetRoot(Transform root)
        {
            Root = root;
            _handleRotation = root.rotation;

            if (!_ownsToolsHidden)
            {
                _previousToolsHidden = Tools.hidden;
                _ownsToolsHidden = true;
            }
            Tools.hidden = true;
            RepaintSceneViews();
        }

        public static void ClearRoot()
        {
            // Unity's overloaded null check is true for destroyed objects; use
            // a CLR null check so a deleted root can still cleanly exit the tool.
            if (ReferenceEquals(Root, null)) return;

            Root = null;
            RestoreNativeTools();
            RepaintSceneViews();
        }

        public static int LookSelectedObjectsAtRoot()
        {
            Transform root = Root;
            if (root == null) return 0;

            Transform[] targets = GetLookAtTargets();
            if (targets.Length == 0) return 0;

            var validTargets = targets
                .Where(target => (target.position - root.position).sqrMagnitude > 0.000001f)
                .ToArray();
            if (validTargets.Length == 0) return 0;

            Undo.RecordObjects(validTargets, "Look At Rotation Root");
            for (int i = 0; i < validTargets.Length; i++)
                validTargets[i].LookAt(root.position);

            RepaintSceneViews();
            return validTargets.Length;
        }

        private static void ValidateRoot()
        {
            if (!ReferenceEquals(Root, null) && Root == null)
                ClearRoot();
        }

        private static void BeforeAssemblyReload()
        {
            RestoreNativeTools();
        }

        private static void RestoreNativeTools()
        {
            if (!_ownsToolsHidden) return;
            Tools.hidden = _previousToolsHidden;
            _ownsToolsHidden = false;
        }

        public static void RepaintSceneViews()
        {
            foreach (SceneView sceneView in SceneView.sceneViews)
                sceneView.Repaint();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            var root = Root;
            if (root == null) return;

            // Root Rotation owns the scene handles while active, but remains
            // independent from Unity's global EditorTool/dropdown system.
            Tools.hidden = true;

            DrawSelectionHighlight();
            DrawRootMarker(root);

            Transform[] targets = GetTopLevelSelection();
            if (targets.Length == 0) return;

            EditorGUI.BeginChangeCheck();
            Quaternion newRotation = Handles.RotationHandle(
                _handleRotation, root.position);
            if (!EditorGUI.EndChangeCheck()) return;

            Quaternion delta = newRotation * Quaternion.Inverse(_handleRotation);
            if (Quaternion.Angle(Quaternion.identity, delta) < 0.0001f) return;

            Undo.RecordObjects(targets, "Rotate Around Root");
            Vector3 pivot = root.position;
            for (int i = 0; i < targets.Length; i++)
            {
                Transform target = targets[i];
                target.position = pivot + delta * (target.position - pivot);
                target.rotation = delta * target.rotation;
            }

            _handleRotation = newRotation;
            RepaintSceneViews();
        }

        private static Transform[] GetTopLevelSelection()
        {
            var selected = new HashSet<Transform>(
                Selection.transforms.Where(t => t != null));

            return selected.Where(transform =>
            {
                for (Transform parent = transform.parent;
                     parent != null; parent = parent.parent)
                {
                    if (selected.Contains(parent)) return false;
                }
                return true;
            }).ToArray();
        }

        private static Transform[] GetLookAtTargets()
        {
            Transform root = Root;
            if (root == null) return new Transform[0];

            return Selection.transforms
                .Where(transform => transform != null && transform != root)
                .Distinct()
                .ToArray();
        }

        private static void DrawRootMarker(Transform root)
        {
            Color previous = Handles.color;
            Handles.color = new Color(1f, 0.65f, 0.15f, 0.9f);
            float size = HandleUtility.GetHandleSize(root.position) * 0.08f;
            Handles.SphereHandleCap(
                0, root.position, Quaternion.identity, size, EventType.Repaint);
            Handles.Label(root.position + Vector3.up * size * 1.5f,
                "Root", RootLabelStyle);
            Handles.color = previous;
        }

        private static GUIStyle RootLabelStyle
        {
            get
            {
                if (_rootLabelStyle != null) return _rootLabelStyle;
                _rootLabelStyle = new GUIStyle(EditorStyles.boldLabel);
                _rootLabelStyle.fontSize = 12;
                _rootLabelStyle.normal.textColor =
                    new Color(1f, 0.72f, 0.25f, 1f);
                return _rootLabelStyle;
            }
        }

        private static void DrawSelectionHighlight()
        {
            if (Event.current.type != EventType.Repaint) return;

            GameObject[] selected = Selection.gameObjects
                .Where(gameObject => gameObject != null)
                .ToArray();
            if (selected.Length == 0) return;

            Handles.DrawOutline(
                selected,
                new Color(1f, 0.65f, 0.15f, 0.4f),
                new Color(1f, 0.65f, 0.15f, 0.07f),
                1f);
        }
    }
}
#endif
