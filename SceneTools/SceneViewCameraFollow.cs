#if UNITY_EDITOR && UNITY_2021_2_OR_NEWER
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace CameraFollowTool
{
    [Overlay(typeof(SceneView), OverlayId, "Cam Follow")]
    [Icon("d_Camera Icon")]
    public class CameraFollowOverlay : ToolbarOverlay
    {
        public const string OverlayId = "camera-follow-overlay";

        CameraFollowOverlay() : base(
            LookAtFollowPair.Id,
            RuntimeConfigPair.Id,
            ClearRestorePair.Id
        )
        { }

        [MenuItem("Window/Cam Follow Overlay")]
        private static void ShowOverlay()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null && SceneView.sceneViews.Count > 0)
                sv = (SceneView)SceneView.sceneViews[0];
            if (sv == null) return;
            if (sv.TryGetOverlay(OverlayId, out var overlay))
                overlay.displayed = true;
        }
    }

    // ===================================================================
    //  Split-button helper
    // ===================================================================
    static class SplitButtonHelper
    {
        // USS class names matching Unity's native toolbar button styling
        const string ToolbarBtnClass = "unity-editor-toolbar-element";
        const string ToolbarToggleClass = "unity-toolbar-toggle";

        static readonly Color ActiveBg = new Color(0.25f, 0.45f, 0.75f, 0.7f);
        static readonly Color HoverBg = new Color(0.5f, 0.5f, 0.5f, 0.01f);

        public static VisualElement CreateContainer()
        {
            var c = new VisualElement();
            c.style.flexDirection = FlexDirection.Row;
            c.style.alignItems = Align.Center;
            // Match native toolbar element height
            c.style.height = 22;
            c.style.marginLeft = 0;
            c.style.marginRight = 0;
            return c;
        }

        public static Button CreateHalf(string tip, string iconName, bool isLeft)
        {
            var btn = new Button();
            btn.tooltip = tip;

            // Add native toolbar classes for consistent look
            btn.AddToClassList("unity-toolbar-button");

            // Size: each half ~18px, pair totals ~36px matching native toolbar buttons
            btn.style.width = 20;
            btn.style.minWidth = 18;
            btn.style.height = 22;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.paddingTop = 2;
            btn.style.paddingBottom = 2;
            btn.style.marginLeft = isLeft ? -1f : 0;
            btn.style.marginRight = isLeft ? 0 : -1f;

            // Joined border: only outer edges get radius
            btn.style.borderTopLeftRadius = isLeft ? 0.5f : 0;
            btn.style.borderBottomLeftRadius = isLeft ? 0.5f : 0;
            btn.style.borderTopRightRadius = isLeft ? 0 : 0.5f;
            btn.style.borderBottomRightRadius = isLeft ? 0 : 0.5f;

            // Thin shared border between left and right
            btn.style.borderLeftWidth = isLeft ? 1 : 0;
            btn.style.borderRightWidth = 1;
            btn.style.borderTopWidth = 1;
            btn.style.borderBottomWidth = 1;

            var borderColor = EditorGUIUtility.isProSkin
                ? new Color(0.11f, 0.11f, 0.11f, 0.8f)
                : new Color(0.6f, 0.6f, 0.6f, 0.8f);
            btn.style.borderLeftColor = borderColor;
            btn.style.borderRightColor = borderColor;
            btn.style.borderTopColor = borderColor;
            btn.style.borderBottomColor = borderColor;

            // Background matching native toolbar
            var normalBg = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 0.6f)
                : new Color(0.76f, 0.76f, 0.76f, 0.6f);
            btn.style.backgroundColor = normalBg;

            // Hover effect via callback — use class to track state
            btn.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (!btn.enabledInHierarchy) return;
                btn.AddToClassList("cam-follow--hover");
                if (!btn.ClassListContains("cam-follow--active"))
                    btn.style.backgroundColor = HoverBg + normalBg * 0.5f;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                btn.RemoveFromClassList("cam-follow--hover");
                if (!btn.ClassListContains("cam-follow--active"))
                    btn.style.backgroundColor = normalBg;
            });

            // Icon
            var icon = EditorGUIUtility.FindTexture(iconName);
            if (icon != null)
            {
                var img = new Image { image = icon };
                img.style.width = 14;
                img.style.height = 14;
                img.style.alignSelf = Align.Center;
                // Ensure icon isn't cut off
                img.style.flexShrink = 0;
                btn.Add(img);
            }

            return btn;
        }

        public static void SetActive(Button btn, bool active)
        {
            var normalBg = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 0.6f)
                : new Color(0.76f, 0.76f, 0.76f, 0.6f);

            if (active)
            {
                btn.AddToClassList("cam-follow--active");
                btn.style.backgroundColor = ActiveBg;
                btn.style.borderBottomColor = new Color(0.35f, 0.65f, 1f, 1f);
                btn.style.borderBottomWidth = 2;
            }
            else
            {
                btn.RemoveFromClassList("cam-follow--active");
                // Respect hover state: don't overwrite hover color
                if (btn.ClassListContains("cam-follow--hover"))
                    btn.style.backgroundColor = HoverBg + normalBg;
                else
                    btn.style.backgroundColor = normalBg;
                var borderColor = EditorGUIUtility.isProSkin
                    ? new Color(0.11f, 0.11f, 0.11f, 0.8f)
                    : new Color(0.6f, 0.6f, 0.6f, 0.8f);
                btn.style.borderBottomColor = borderColor;
                btn.style.borderBottomWidth = 1;
            }
        }

        public static void SetInteractive(Button btn, bool interactive)
        {
            if (btn.enabledSelf == interactive) return;

            btn.SetEnabled(interactive);
            btn.style.opacity = interactive ? 1f : 0.4f;
            if (!interactive)
            {
                btn.RemoveFromClassList("cam-follow--hover");
                SetActive(btn, false);
            }
        }
    }

    // ===================================================================
    //  Slot 1: LookAt | Follow
    // ===================================================================
    [EditorToolbarElement(Id, typeof(SceneView))]
    public class LookAtFollowPair : VisualElement
    {
        public const string Id = "CameraFollow/LookAtFollowPair";
        Button _lookAt, _follow;

        public LookAtFollowPair()
        {
            var c = SplitButtonHelper.CreateContainer();
            Add(c);
            _lookAt = SplitButtonHelper.CreateHalf("Look At", "d_ViewToolOrbit", true);
            _follow = SplitButtonHelper.CreateHalf("Follow", "d_MoveTool", false);
            _lookAt.clicked += OnLookAt;
            _follow.clicked += OnFollow;
            c.Add(_lookAt);
            c.Add(_follow);
            schedule.Execute(Sync).Every(100);
        }

        void OnLookAt()
        {
            if (!CameraFollowState.LookAtEnabled)
            {
                if (!CameraFollowState.TryEnableWithAutoCapture()) return;
                CameraFollowState.SnapshotCameraIfNeeded();
                CameraFollowState.LookAtEnabled = true;
            }
            else
                CameraFollowState.LookAtEnabled = false;
        }

        void OnFollow()
        {
            if (!CameraFollowState.FollowEnabled)
            {
                if (!CameraFollowState.TryEnableWithAutoCapture()) return;
                CameraFollowState.SnapshotCameraIfNeeded();
                CameraFollowState.FollowEnabled = true;
                CameraFollowState.HasCapturedOffset = false;
            }
            else
            {
                CameraFollowState.FollowEnabled = false;
                CameraFollowState.HasCapturedOffset = false;
            }
        }

        void Sync()
        {
            SplitButtonHelper.SetActive(_lookAt, CameraFollowState.LookAtEnabled);
            SplitButtonHelper.SetActive(_follow, CameraFollowState.FollowEnabled);
        }
    }

    // ===================================================================
    //  Slot 2: RuntimeOnly | Config
    // ===================================================================
    [EditorToolbarElement(Id, typeof(SceneView))]
    public class RuntimeConfigPair : VisualElement
    {
        public const string Id = "CameraFollow/RuntimeConfigPair";
        Button _runtime, _config;

        public RuntimeConfigPair()
        {
            var c = SplitButtonHelper.CreateContainer();
            Add(c);
            _runtime = SplitButtonHelper.CreateHalf("Runtime Only", "d_PlayButton", true);
            _config = SplitButtonHelper.CreateHalf("Settings", "d_Settings", false);
            _runtime.clicked += () =>
            {
                CameraFollowState.RuntimeOnly = !CameraFollowState.RuntimeOnly;
                CameraFollowState.ResetCapture();
            };
            _config.clicked += () =>
                UnityEditor.PopupWindow.Show(_config.worldBound, new ConfigPopup());
            c.Add(_runtime);
            c.Add(_config);
            schedule.Execute(Sync).Every(100);
        }

        void Sync()
        {
            SplitButtonHelper.SetActive(_runtime, CameraFollowState.RuntimeOnly);
        }
    }

    // ===================================================================
    //  Slot 3: Clear | Restore
    // ===================================================================
    [EditorToolbarElement(Id, typeof(SceneView))]
    public class ClearRestorePair : VisualElement
    {
        public const string Id = "CameraFollow/ClearRestorePair";
        Button _clear, _restore;

        public ClearRestorePair()
        {
            var c = SplitButtonHelper.CreateContainer();
            Add(c);
            _clear = SplitButtonHelper.CreateHalf("Clear target", "TreeEditor.Trash", true);
            _restore = SplitButtonHelper.CreateHalf("Restore camera", "d_Refresh", false);
            _clear.clicked += () =>
            {
                CameraFollowState.ClearTargets();
                foreach (SceneView sv in SceneView.sceneViews) sv.Repaint();
            };
            _restore.clicked += () =>
            {
                CameraFollowState.RestoreCamera();
                foreach (SceneView sv in SceneView.sceneViews) sv.Repaint();
            };
            c.Add(_clear);
            c.Add(_restore);
            schedule.Execute(Sync).Every(100);
        }

        void Sync()
        {
            bool hasTarget = CameraFollowState.HasTargets;
            SplitButtonHelper.SetInteractive(_clear, hasTarget);
            SplitButtonHelper.SetInteractive(
                _restore, hasTarget && CameraFollowState.HasSavedCamera);
        }
    }

    // ===================================================================
    //  Shared state
    // ===================================================================
    [InitializeOnLoad]
    public static class CameraFollowState
    {
        public static bool LookAtEnabled;
        public static bool FollowEnabled;
        public static bool RuntimeOnly = true;
        public static Transform[] TargetObjects;

        public static Vector3 Offset;
        public static bool HasCapturedOffset;

        private static float _orbitYaw, _orbitPitch, _orbitDist;
        private static bool _orbitCaptured;

        private static Vector3 _lockedOffset;
        private static Quaternion _lockedRotation;
        private static bool _lockedCaptured;

        private static bool _isRightDown;
        private static Vector2 _lastMousePos;

        private const float OrbitSensitivity = 0.3f;
        private const float MinPitch = -89f;
        private const float MaxPitch = 89f;

        public static float WasdOrbitSpeed = 120f;
        public static float WasdDistanceSpeed = 2f;

        private static bool _keyW, _keyA, _keyS, _keyD, _keyQ, _keyE;
        private static double _lastTime;

        private static Vector3 _prevCenter;
        private static bool _hasPrevCenter;

        // Camera snapshot
        private static Vector3 _savedPivot;
        private static Quaternion _savedRotation;
        private static float _savedSize;
        public static bool HasSavedCamera { get; private set; }

        static CameraFollowState()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            _lastTime = EditorApplication.timeSinceStartup;
        }

        public static bool HasTargets =>
            TargetObjects != null && TargetObjects.Length > 0 &&
            TargetObjects.Any(t => t != null);

        public static void SnapshotCameraIfNeeded()
        {
            if (HasSavedCamera) return;
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            _savedPivot = sv.pivot;
            _savedRotation = sv.rotation;
            _savedSize = sv.size;
            HasSavedCamera = true;
        }

        public static void RestoreCamera()
        {
            var sv = SceneView.lastActiveSceneView;
            if (HasSavedCamera && sv != null)
            {
                sv.pivot = _savedPivot;
                sv.rotation = _savedRotation;
                sv.size = _savedSize;
            }

            ClearTargetState();
            HasSavedCamera = false;
        }

        public static bool TryEnableWithAutoCapture()
        {
            if (HasTargets) { ResetCapture(); return true; }
            var sel = Selection.transforms;
            if (sel == null || sel.Length == 0) return false;
            var valid = sel.Where(t => t != null).ToArray();
            if (valid.Length == 0) return false;
            TargetObjects = valid;
            ResetCapture();
            return true;
        }

        public static void ClearTargets()
        {
            // Clearing a target is also the end of the camera-follow session.
            // Restore before discarding the snapshot so both toolbar actions
            // return the Scene view to the camera state captured on activation.
            RestoreCamera();
        }

        private static void ClearTargetState()
        {
            LookAtEnabled = false;
            FollowEnabled = false;
            TargetObjects = null;
            HasCapturedOffset = false;
            _orbitCaptured = false;
            _lockedCaptured = false;
            _hasPrevCenter = false;
        }

        public static void ResetCapture()
        {
            HasCapturedOffset = false;
            _orbitCaptured = false;
            _lockedCaptured = false;
            _hasPrevCenter = false;
        }

        private static void OnSceneGUI(SceneView sv)
        {
            if (!LookAtEnabled && !FollowEnabled)
            {
                HasCapturedOffset = false;
                _orbitCaptured = false;
                _lockedCaptured = false;
                _hasPrevCenter = false;
                return;
            }

            if (RuntimeOnly && !EditorApplication.isPlaying) return;

            Vector3 center;
            if (!TryGetCenter(out center)) return;

            bool both = LookAtEnabled && FollowEnabled;
            if (both) HandleBothMode(sv, center);
            else if (LookAtEnabled) HandleLookAtOnly(sv, center);
            else if (FollowEnabled) HandleFollowOnly(sv, center);

            DrawTargetGizmo(sv, center);
            sv.Repaint();
        }

        private const float GizmoScreenSize = 8f;
        private static void DrawTargetGizmo(SceneView sv, Vector3 center)
        {
            float dist = Vector3.Distance(sv.camera.transform.position, center);
            float r = dist * GizmoScreenSize / sv.camera.pixelHeight
                      * 2f * Mathf.Tan(sv.camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Color old = Handles.color;
            Handles.color = new Color(0.3f, 0.7f, 1f, 0.6f);
            Handles.SphereHandleCap(0, center, Quaternion.identity, r * 2f, EventType.Repaint);
            Handles.color = new Color(0.3f, 0.7f, 1f, 0.9f);
            Handles.DrawWireDisc(center, sv.camera.transform.forward, r);
            Handles.color = old;
        }

        private static void HandleBothMode(SceneView sv, Vector3 center)
        {
            if (!_lockedCaptured)
            {
                Vector3 camPos = GetCameraWorldPos(sv);
                _lockedOffset = camPos - center;
                Vector3 dir = center - camPos;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Vector3 fwd = dir.normalized;
                    Vector3 up = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.99f
                        ? Vector3.forward : Vector3.up;
                    _lockedRotation = Quaternion.LookRotation(fwd, up);
                }
                else _lockedRotation = sv.rotation;
                _lockedCaptured = true;
            }
            Vector3 lcp = center + _lockedOffset;
            sv.rotation = _lockedRotation;
            sv.pivot = lcp + _lockedRotation * Vector3.forward * sv.cameraDistance;
        }

        private static void HandleLookAtOnly(SceneView sv, Vector3 center)
        {
            var e = Event.current;
            if (!_orbitCaptured)
            {
                Vector3 cp = GetCameraWorldPos(sv);
                Vector3 off = cp - center;
                _orbitDist = off.magnitude;
                if (_orbitDist < 0.01f) _orbitDist = 5f;
                _orbitYaw = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg;
                _orbitPitch = Mathf.Asin(Mathf.Clamp(off.y / _orbitDist, -1f, 1f)) * Mathf.Rad2Deg;
                _orbitCaptured = true;
            }

            if (e != null)
            {
                if (e.type == EventType.MouseDown && e.button == 1)
                { _isRightDown = true; _lastMousePos = e.mousePosition; }
                else if (e.type == EventType.MouseUp && e.button == 1)
                { _isRightDown = false; _keyW = _keyA = _keyS = _keyD = _keyQ = _keyE = false; }
            }

            if (_isRightDown)
            {
                if (e != null && e.type == EventType.MouseDrag && e.button == 1)
                {
                    Vector2 d = e.mousePosition - _lastMousePos;
                    _lastMousePos = e.mousePosition;
                    _orbitYaw += d.x * OrbitSensitivity;
                    _orbitPitch += d.y * OrbitSensitivity;
                    _orbitPitch = Mathf.Clamp(_orbitPitch, MinPitch, MaxPitch);
                    e.Use();
                }
                if (e != null)
                {
                    if (e.type == EventType.KeyDown)
                    {
                        switch (e.keyCode)
                        {
                            case KeyCode.W: _keyW = true; e.Use(); break;
                            case KeyCode.A: _keyA = true; e.Use(); break;
                            case KeyCode.S: _keyS = true; e.Use(); break;
                            case KeyCode.D: _keyD = true; e.Use(); break;
                            case KeyCode.Q: _keyQ = true; e.Use(); break;
                            case KeyCode.E: _keyE = true; e.Use(); break;
                        }
                    }
                    else if (e.type == EventType.KeyUp)
                    {
                        switch (e.keyCode)
                        {
                            case KeyCode.W: _keyW = false; break;
                            case KeyCode.A: _keyA = false; break;
                            case KeyCode.S: _keyS = false; break;
                            case KeyCode.D: _keyD = false; break;
                            case KeyCode.Q: _keyQ = false; break;
                            case KeyCode.E: _keyE = false; break;
                        }
                    }
                }

                double now = EditorApplication.timeSinceStartup;
                float dt = Mathf.Clamp((float)(now - _lastTime), 0.001f, 0.1f);
                _lastTime = now;

                if (_keyA) _orbitYaw += WasdOrbitSpeed * dt;
                if (_keyD) _orbitYaw -= WasdOrbitSpeed * dt;
                if (_keyW) _orbitPitch += WasdOrbitSpeed * dt;
                if (_keyS) _orbitPitch -= WasdOrbitSpeed * dt;
                if (_keyQ) _orbitDist += _orbitDist * WasdDistanceSpeed * dt;
                if (_keyE) _orbitDist -= _orbitDist * WasdDistanceSpeed * dt;
                _orbitPitch = Mathf.Clamp(_orbitPitch, MinPitch, MaxPitch);
                _orbitDist = Mathf.Max(0.1f, _orbitDist);

                Vector3 camPos = SphericalToCartesian(_orbitYaw, _orbitPitch, _orbitDist) + center;
                Vector3 fwd = (center - camPos).normalized;
                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
                sv.rotation = rot;
                sv.pivot = camPos + rot * Vector3.forward * sv.cameraDistance;

                if (_keyW || _keyA || _keyS || _keyD || _keyQ || _keyE) sv.Repaint();
            }
            else
            {
                Vector3 camPos = GetCameraWorldPos(sv);
                Vector3 dir = center - camPos;
                if (dir.sqrMagnitude > 0.01f)
                {
                    Vector3 fwd = dir.normalized;
                    Quaternion nr = Quaternion.LookRotation(fwd, Vector3.up);
                    sv.rotation = nr;
                    sv.pivot = camPos + nr * Vector3.forward * sv.cameraDistance;
                }
                Vector3 no = camPos - center;
                _orbitDist = no.magnitude;
                if (_orbitDist > 0.01f)
                {
                    _orbitYaw = Mathf.Atan2(no.x, no.z) * Mathf.Rad2Deg;
                    _orbitPitch = Mathf.Asin(Mathf.Clamp(no.y / _orbitDist, -1f, 1f)) * Mathf.Rad2Deg;
                }
            }

            if (e != null && e.type == EventType.ScrollWheel)
            {
                _orbitDist += e.delta.y * _orbitDist * 0.05f;
                _orbitDist = Mathf.Max(0.1f, _orbitDist);
            }
        }

        private static void HandleFollowOnly(SceneView sv, Vector3 center)
        {
            if (!_hasPrevCenter)
            {
                _prevCenter = center;
                _hasPrevCenter = true;
                return;
            }
            Vector3 delta = center - _prevCenter;
            if (delta.sqrMagnitude > 0.00001f) sv.pivot += delta;
            _prevCenter = center;
        }

        private static Vector3 GetCameraWorldPos(SceneView sv)
        {
            return sv.pivot - sv.rotation * Vector3.forward * sv.cameraDistance;
        }

        private static Vector3 SphericalToCartesian(float yawDeg, float pitchDeg, float radius)
        {
            float y = yawDeg * Mathf.Deg2Rad;
            float p = pitchDeg * Mathf.Deg2Rad;
            return new Vector3(
                Mathf.Sin(y) * Mathf.Cos(p),
                Mathf.Sin(p),
                Mathf.Cos(y) * Mathf.Cos(p)
            ) * radius;
        }

        public static bool TryGetCenter(out Vector3 center)
        {
            center = Vector3.zero;
            if (TargetObjects == null || TargetObjects.Length == 0) return false;
            var alive = TargetObjects.Where(t => t != null).ToArray();
            if (alive.Length == 0) { ClearTargets(); return false; }
            if (alive.Length != TargetObjects.Length) TargetObjects = alive;
            if (alive.Length == 1) { center = alive[0].position; return true; }
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < alive.Length; i++) sum += alive[i].position;
            center = sum / alive.Length;
            return true;
        }

        public static string GetTargetLabel()
        {
            if (TargetObjects == null || TargetObjects.Length == 0) return "None";
            var alive = TargetObjects.Where(t => t != null).ToArray();
            if (alive.Length == 0) return "None";
            if (alive.Length == 1) return alive[0].name;
            return alive[0].name + " + " + (alive.Length - 1) + " more";
        }
    }

    // ===================================================================
    //  Config Popup
    // ===================================================================
    public class ConfigPopup : PopupWindowContent
    {
        private Transform _singleTarget;

        public ConfigPopup()
        {
            var t = CameraFollowState.TargetObjects;
            if (t != null && t.Length == 1 && t[0] != null) _singleTarget = t[0];
        }

        public override Vector2 GetWindowSize() => new Vector2(300, 155);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Follow Target", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            var targets = CameraFollowState.TargetObjects;
            bool multi = targets != null && targets.Count(t => t != null) > 1;

            if (multi)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("Targets", CameraFollowState.GetTargetLabel());
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                _singleTarget = (Transform)EditorGUILayout.ObjectField(
                    "Target", _singleTarget, typeof(Transform), true);
                if (EditorGUI.EndChangeCheck())
                {
                    CameraFollowState.TargetObjects = _singleTarget != null
                        ? new[] { _singleTarget } : null;
                    CameraFollowState.ResetCapture();
                }
            }

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Use Current Selection"))
            {
                var sel = Selection.transforms;
                if (sel != null && sel.Length > 0)
                {
                    CameraFollowState.TargetObjects = sel.Where(t => t != null).ToArray();
                    CameraFollowState.ResetCapture();
                    _singleTarget = CameraFollowState.TargetObjects.Length == 1
                        ? CameraFollowState.TargetObjects[0] : null;
                }
            }

            EditorGUILayout.Space(4);
            CameraFollowState.WasdOrbitSpeed = EditorGUILayout.Slider(
                "Orbit Speed (°/s)", CameraFollowState.WasdOrbitSpeed, 30f, 360f);
            CameraFollowState.WasdDistanceSpeed = EditorGUILayout.Slider(
                "Zoom Speed", CameraFollowState.WasdDistanceSpeed, 0.5f, 10f);

            EditorGUILayout.Space(2);
            string mode = CameraFollowState.LookAtEnabled && CameraFollowState.FollowEnabled
                ? "Look At + Follow"
                : CameraFollowState.LookAtEnabled ? "Look At"
                : CameraFollowState.FollowEnabled ? "Follow" : "Inactive";
            string rt = CameraFollowState.RuntimeOnly ? "Runtime only" : "Always active";
            EditorGUILayout.HelpBox(mode + "  |  " + rt, MessageType.Info);
        }
    }
}
#endif
