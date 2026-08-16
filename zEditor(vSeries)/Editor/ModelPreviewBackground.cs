using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ZEditorTools
{
    /// <summary>
    /// Adds the zEditorTools mascot to Unity's isolated mesh and prefab preview scenes.
    /// </summary>
    [InitializeOnLoad]
    internal static class ModelPreviewBackground
    {
        private const string ModelPath = "Assets/Plugins/zEditorTools/Model/菲比啾比.obj";
        private const string TexturePath = "Assets/Plugins/zEditorTools/Model/FeiBi_SubTool2Tex2.png";
        private const string EnabledPref = "zEditorTools.ModelPreviewBackground.Enabled";
        private const string ModelGuidPref = "zEditorTools.ModelPreviewBackground.ModelGuid";
        private const double ScanInterval = 0.5d;
        private const float BackgroundSizeRelativeToTarget = 1.08f;
        private const float BackgroundSeparation = 1.18f;
        private const float PreviewHeaderHeight = 22f;

        private static readonly BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo PreviewOpenedField =
            typeof(PreviewRenderUtility).GetField("m_previewOpened", InstanceFields);
        private static readonly Vector3[] AmbientLightDirections =
        {
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back
        };

        private static readonly Dictionary<PreviewRenderUtility, BackgroundInstance> Instances = new();
        private static readonly Dictionary<PreviewRenderUtility, PreviewContext> FoundUtilities = new();
        private static readonly HashSet<object> VisitedObjects = new(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<EditorWindow, UnityEngine.UIElements.Button> SettingsButtons = new();
        private static readonly Dictionary<EditorWindow, VisualElement> WheelContainers = new();
        private static readonly Dictionary<EditorWindow, EventCallback<WheelEvent>> WheelCallbacks = new();
        private static readonly Dictionary<EditorWindow, EventCallback<PointerDownEvent>> FocusCallbacks = new();

        private static GameObject sourceModel;
        private static Texture2D sourceTexture;
        private static double nextScanTime;
        private static double nextButtonScanTime;
        private static int pendingPreviewInitializationFrames;
        private static float pendingWheelDelta;
        private static double pendingWheelExpiry;
        private static EditorWindow focusedPreviewHost;

        static ModelPreviewBackground()
        {
            EditorApplication.update += Update;
            Selection.selectionChanged += OnSelectionChanged;
            Camera.onPreCull += PositionForCamera;
            Camera.onPostRender += RestoreAfterCamera;
            RenderPipelineManager.beginCameraRendering += PositionForRenderPipelineCamera;
            RenderPipelineManager.endCameraRendering += RestoreAfterRenderPipelineCamera;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }

        private static void OnSelectionChanged()
        {
            nextScanTime = 0d;
            nextButtonScanTime = 0d;
            pendingPreviewInitializationFrames = 12;
            focusedPreviewHost = null;

            // The tracker must build the new primary Editor before it can create its
            // PreviewRenderUtility. The following repaints then initialize the Preview.
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            RepaintAllViews();
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set => EditorPrefs.SetBool(EnabledPref, value);
        }

        internal static bool FeatureEnabled
        {
            get => Enabled;
            set
            {
                if (Enabled == value)
                    return;

                Enabled = value;
                if (!value)
                    RemoveAllInstances();
                nextScanTime = 0d;
                RepaintAllViews();
            }
        }

        internal static GameObject DecorationModel
        {
            get
            {
                if (sourceModel == null)
                    sourceModel = LoadConfiguredSourceModel();
                return sourceModel;
            }
            set
            {
                var selectedModel = value != null
                    ? value
                    : AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (sourceModel == selectedModel)
                    return;

                var path = AssetDatabase.GetAssetPath(selectedModel);
                var guid = string.IsNullOrEmpty(path)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid) ||
                    string.Equals(path, ModelPath, StringComparison.OrdinalIgnoreCase))
                    EditorPrefs.DeleteKey(ModelGuidPref);
                else
                    EditorPrefs.SetString(ModelGuidPref, guid);

                RemoveAllInstances();
                sourceModel = selectedModel;
                nextScanTime = 0d;
                pendingPreviewInitializationFrames = 12;
                ClearGameObjectPreviewCaches();
                RepaintAllViews();
            }
        }

        internal static string DecorationModelPath => AssetDatabase.GetAssetPath(DecorationModel);

        internal static bool TrySetDecorationModelPath(string path, out string error)
        {
            var normalizedPath = path?.Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(normalizedPath))
            {
                error = "模型路径不能为空。";
                return false;
            }

            if (!normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && !normalizedPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                error = "模型路径必须位于 Assets 或 Packages 下。";
                return false;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(normalizedPath);
            if (model == null)
            {
                error = "该路径没有可用的 GameObject、Prefab 或模型资源。";
                return false;
            }

            DecorationModel = model;
            error = string.Empty;
            return true;
        }

        internal static void ResetDecorationModel()
        {
            EditorPrefs.DeleteKey(ModelGuidPref);
            DecorationModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        }

        private static GameObject LoadConfiguredSourceModel()
        {
            var guid = EditorPrefs.GetString(ModelGuidPref, string.Empty);
            if (!string.IsNullOrEmpty(guid))
            {
                var configuredPath = AssetDatabase.GUIDToAssetPath(guid);
                var configuredModel =
                    AssetDatabase.LoadAssetAtPath<GameObject>(configuredPath);
                if (configuredModel != null)
                    return configuredModel;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        }

        internal static void SettingsChanged()
        {
            ClearGameObjectPreviewCaches();
            foreach (var pair in Instances)
                ApplyLighting(pair.Key);
            RepaintAllViews();
            EditorApplication.delayCall += RepaintAllViews;
        }

        private static void Update()
        {
            InitializeNewSelectionPreview();
            UpdateSettingsButtons();

            if (!Enabled || EditorApplication.timeSinceStartup < nextScanTime)
                return;

            nextScanTime = EditorApplication.timeSinceStartup + ScanInterval;
            sourceModel = sourceModel != null
                ? sourceModel
                : LoadConfiguredSourceModel();
            sourceTexture = sourceTexture != null
                ? sourceTexture
                : AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);

            if (sourceModel == null)
                return;

            FindActivePreviewUtilities();
            AddOrUpdateInstances();
            RemoveUnusedInstances();
        }

        private static void InitializeNewSelectionPreview()
        {
            if (pendingPreviewInitializationFrames <= 0)
                return;

            pendingPreviewInitializationFrames--;
            nextScanTime = 0d;
            ActiveEditorTracker.sharedTracker.RebuildIfNecessary();
            RepaintAllViews();
        }

        private static void FindActivePreviewUtilities()
        {
            FoundUtilities.Clear();
            VisitedObjects.Clear();

            foreach (var editor in ActiveEditorTracker.sharedTracker.activeEditors)
            {
                if (editor != null)
                    FindPreviewUtilities(editor, 0, default);
            }

            // A non-focused or floating Inspector does not necessarily use sharedTracker.
            // Scan only the relevant live Editor instances so their previews initialize too.
            foreach (var editor in Resources.FindObjectsOfTypeAll<Editor>())
            {
                if (editor != null && IsSupportedPreviewEditor(editor.GetType()))
                    FindPreviewUtilities(editor, 0, default);
            }
        }

        private static bool IsSupportedPreviewEditor(Type type)
        {
            if (type == typeof(MaterialEditor) ||
                type.FullName == "UnityEditor.GameObjectInspector")
                return true;

            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.FullName == "UnityEditor.AssetImporters.AssetImporterEditor")
                    return true;
            }

            return false;
        }

        private static void UpdateSettingsButtons()
        {
            if (EditorApplication.timeSinceStartup < nextButtonScanTime)
                return;
            nextButtonScanTime = EditorApplication.timeSinceStartup + ScanInterval;

            var livePreviewHosts = new HashSet<EditorWindow>();
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                var windowTypeName = window != null ? window.GetType().Name : string.Empty;
                if (window == null ||
                    (windowTypeName != "PreviewWindow" &&
                     windowTypeName != "InspectorWindow" &&
                     windowTypeName != "PropertyEditor"))
                    continue;

                livePreviewHosts.Add(window);
                if (!SettingsButtons.TryGetValue(window, out var button) || button == null)
                {
                    var owner = window;
                    button = new UnityEngine.UIElements.Button(() =>
                        {
                            if (owner != null && SettingsButtons.TryGetValue(owner, out var ownerButton))
                                ModelPreviewLightingWindow.Open(owner, ownerButton.worldBound);
                        })
                    {
                        text = "⚙",
                        tooltip = "zEditorTools 模型预览设置"
                    };
                    button.name = "zEditorTools-model-preview-settings";
                    button.style.position = Position.Absolute;
                    button.style.left = 0f;
                    button.style.top = PreviewHeaderHeight;
                    button.style.width = 25f;
                    button.style.height = 22f;
                    button.style.paddingLeft = 2f;
                    button.style.paddingRight = 2f;
                    button.style.unityTextAlign = TextAnchor.MiddleCenter;
                    SettingsButtons.Add(window, button);
                }

                // Inspector rebuilds this element whenever its inspected object changes.
                // Reattach an existing button when its previous container was destroyed.
                var previewElement = windowTypeName == "PreviewWindow"
                    ? GetFieldValueRecursive<VisualElement>(window, "m_previewElement")
                    : GetFieldValueRecursive<VisualElement>(window, "m_PreviewAndLabelElement");
                var targetContainer = previewElement ?? window.rootVisualElement;
                if (button.parent != targetContainer)
                {
                    button.RemoveFromHierarchy();
                    targetContainer.Add(button);
                }

                // Never attach wheel handling to the whole Inspector. MaterialEditor
                // also renders a small static icon in its header, which must retain
                // Unity's native behavior.
                if (previewElement != null)
                    RegisterWheelHandler(window, previewElement);
                else
                    UnregisterWheelHandler(window);
                button.style.display = DisplayStyle.Flex;
                button.BringToFront();
            }

            var staleWindows = new List<EditorWindow>();
            foreach (var pair in SettingsButtons)
            {
                if (pair.Key == null || !livePreviewHosts.Contains(pair.Key))
                    staleWindows.Add(pair.Key);
            }

            foreach (var window in staleWindows)
            {
                UnregisterWheelHandler(window);
                if (window != null && SettingsButtons[window] != null)
                    SettingsButtons[window].RemoveFromHierarchy();
                SettingsButtons.Remove(window);
            }
        }

        private static void RegisterWheelHandler(EditorWindow window, VisualElement container)
        {
            if (WheelContainers.TryGetValue(window, out var oldContainer) &&
                oldContainer == container)
                return;

            UnregisterWheelHandler(window);
            EventCallback<WheelEvent> callback = wheelEvent => OnPreviewWheel(window, wheelEvent);
            EventCallback<PointerDownEvent> focusCallback =
                pointerEvent => OnPreviewPointerDown(window, pointerEvent);
            container.RegisterCallback(callback, TrickleDown.TrickleDown);
            container.RegisterCallback(focusCallback, TrickleDown.TrickleDown);
            WheelContainers[window] = container;
            WheelCallbacks[window] = callback;
            FocusCallbacks[window] = focusCallback;
        }

        private static void UnregisterWheelHandler(EditorWindow window)
        {
            if (window != null &&
                WheelContainers.TryGetValue(window, out var container) &&
                container != null &&
                WheelCallbacks.TryGetValue(window, out var callback))
            {
                container.UnregisterCallback(callback, TrickleDown.TrickleDown);
                if (FocusCallbacks.TryGetValue(window, out var focusCallback))
                    container.UnregisterCallback(focusCallback, TrickleDown.TrickleDown);
            }

            WheelContainers.Remove(window);
            WheelCallbacks.Remove(window);
            FocusCallbacks.Remove(window);
            if (focusedPreviewHost == window)
                focusedPreviewHost = null;
        }

        private static void OnPreviewWheel(EditorWindow owner, WheelEvent wheelEvent)
        {
            // IMGUI-based GameObject/Prefab previews do not consistently forward a
            // PointerDownEvent to their parent UI Toolkit container. In that case the
            // preview host is still Unity's focused window, so accept either signal.
            if (focusedPreviewHost != owner && EditorWindow.focusedWindow != owner)
                return;

            focusedPreviewHost = owner;
            pendingWheelDelta += wheelEvent.delta.y;
            pendingWheelExpiry = EditorApplication.timeSinceStartup + 1d;
            ClearGameObjectPreviewCaches(owner);
            owner.Repaint();
            owner.rootVisualElement.MarkDirtyRepaint();
            EditorApplication.QueuePlayerLoopUpdate();
            wheelEvent.StopPropagation();
        }

        private static void OnPreviewPointerDown(
            EditorWindow owner, PointerDownEvent pointerEvent)
        {
            if (pointerEvent.button == 0 || pointerEvent.button == 1 || pointerEvent.button == 2)
                focusedPreviewHost = owner;
        }

        private static T GetFieldValueRecursive<T>(object value, string fieldName) where T : class
        {
            for (var type = value.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, InstanceFields | BindingFlags.DeclaredOnly);
                if (field?.GetValue(value) is T result)
                    return result;
            }

            return null;
        }

        // Unity uses different private object graphs for imported models and Prefabs.
        // MaterialEditor is special: all Material inspectors share one static preview utility.
        private static void FindPreviewUtilities(object value, int depth, PreviewContext context)
        {
            if (value == null || depth > 8)
                return;

            if (value is PreviewRenderUtility utility)
            {
                try
                {
                    if (utility.camera != null &&
                        (!FoundUtilities.TryGetValue(utility, out var oldContext) ||
                         context.IsMoreSpecificThan(oldContext)))
                        FoundUtilities[utility] = context;
                }
                catch
                {
                    // The inspector can dispose a native preview between two editor callbacks.
                }

                return;
            }

            if (!VisitedObjects.Add(value))
                return;

            var type = value.GetType();
            if (value is MaterialEditor materialEditor)
            {
                AddMaterialPreviewUtility(materialEditor);
                return;
            }

            if (type.FullName == "UnityEditor.GameObjectInspector")
            {
                context = context.WithGameObjectPreview();
                InitializeGameObjectPreview(value, context, depth);
            }

            if (type.FullName == "UnityEditor.MeshPreview")
                context = context.WithMesh(GetFieldValue<Mesh>(value, "m_Target"));
            else if (type.FullName == "UnityEditor.GameObjectInspector+PreviewData")
                context = context.WithGameObject(GetFieldValue<GameObject>(value, "<gameObject>k__BackingField"));

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry item in dictionary)
                    FindPreviewUtilities(item.Value, depth + 1, context);
                return;
            }

            if (value is IEnumerable enumerable && value is not string && value is not Object)
            {
                foreach (var item in enumerable)
                    FindPreviewUtilities(item, depth + 1, context);
                return;
            }

            if (value is Object && value is not Editor)
                return;

            if (value is not Editor &&
                (type.Namespace == null || !type.Namespace.StartsWith("UnityEditor", StringComparison.Ordinal)))
                return;

            for (var currentType = type; currentType != null; currentType = currentType.BaseType)
            {
                foreach (var field in currentType.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.FieldType.IsPrimitive || field.FieldType.IsEnum ||
                        field.FieldType == typeof(string) || typeof(Delegate).IsAssignableFrom(field.FieldType))
                        continue;

                    try
                    {
                        FindPreviewUtilities(field.GetValue(value), depth + 1, context);
                    }
                    catch
                    {
                        // Some native-backed fields become invalid while an Inspector rebuilds.
                    }
                }
            }
        }

        private static void InitializeGameObjectPreview(
            object gameObjectInspector, PreviewContext context, int depth)
        {
            try
            {
                if (gameObjectInspector is not Editor editor ||
                    editor.target == null ||
                    !AssetDatabase.Contains(editor.target) ||
                    !editor.HasPreviewGUI())
                    return;

                var previewData = gameObjectInspector.GetType()
                    .GetMethod("GetPreviewData", InstanceFields)
                    ?.Invoke(gameObjectInspector, new object[] { false });
                if (previewData != null)
                    FindPreviewUtilities(previewData, depth + 1, context);
            }
            catch
            {
                // Selection/import can invalidate the native target during the same frame.
            }
        }

        private static void AddMaterialPreviewUtility(MaterialEditor editor)
        {
            var type = typeof(MaterialEditor);
            var utility = type.GetField("s_PreviewRenderUtility", InstanceFields | BindingFlags.Static)
                ?.GetValue(null) as PreviewRenderUtility;
            if (utility == null || utility.camera == null)
                return;

            Mesh previewMesh = null;
            var meshes = type.GetField("s_Meshes", InstanceFields | BindingFlags.Static)
                ?.GetValue(null) as Mesh[];
            var selectedMesh = GetIntFieldValue(editor, "m_SelectedMesh");
            if (meshes != null && selectedMesh >= 0 && selectedMesh < meshes.Length)
                previewMesh = meshes[selectedMesh];

            FoundUtilities[utility] = PreviewContext.ForMaterial(previewMesh);
        }

        private static int GetIntFieldValue(object value, string fieldName)
        {
            var field = value.GetType().GetField(fieldName, InstanceFields);
            return field?.GetValue(value) is int result ? result : -1;
        }

        private static T GetFieldValue<T>(object value, string fieldName) where T : Object
        {
            var field = value.GetType().GetField(fieldName, InstanceFields);
            return field?.GetValue(value) as T;
        }

        private static void AddOrUpdateInstances()
        {
            var addedAny = false;
            foreach (var pair in FoundUtilities)
            {
                var utility = pair.Key;
                if (!pair.Value.IsSupported)
                    continue;

                if (Instances.TryGetValue(utility, out var oldInstance))
                {
                    if (!pair.Value.RefersToSameTarget(oldInstance.Context))
                    {
                        oldInstance.PlacementRotation = GetHorizontalCameraRotation(utility.camera);
                        oldInstance.ZoomMultiplier = 1f;
                        oldInstance.LastAppliedCameraDistance = 0f;
                        oldInstance.LastAppliedOrthographicSize = 0f;
                    }
                    oldInstance.Context = pair.Value;
                    continue;
                }

                var instance = Object.Instantiate(sourceModel);
                instance.name = "zEditorTools Model Preview Background";
                SetHideFlagsRecursively(instance, HideFlags.HideAndDontSave);

                foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    Object.DestroyImmediate(behaviour);

                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;

                var ownedMaterials = new List<Material>();
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    renderer.sharedMaterials = CreatePipelineCompatibleMaterials(renderer.sharedMaterials, ownedMaterials);
                }

                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                var localBounds = CalculateBoundsInRootSpace(instance);

                utility.AddSingleGO(instance);
                var ambientLights = CreateAmbientLights(utility);
                Instances.Add(utility,
                    new BackgroundInstance(
                        utility.camera, instance, localBounds, renderers,
                        ambientLights, ownedMaterials, pair.Value,
                        GetHorizontalCameraRotation(utility.camera)));
                ApplyLighting(utility);
                addedAny = true;
            }

            if (addedAny)
            {
                ClearGameObjectPreviewCaches();
                RepaintAllViews();
            }
        }

        private static void ClearGameObjectPreviewCaches()
        {
            foreach (var editor in Resources.FindObjectsOfTypeAll<Editor>())
            {
                if (editor == null || editor.GetType().FullName != "UnityEditor.GameObjectInspector")
                    continue;

                try
                {
                    editor.GetType()
                        .GetMethod("ClearPreviewCache", InstanceFields)
                        ?.Invoke(editor, null);
                }
                catch
                {
                    // The Inspector may be rebuilding or disposing its native target.
                }
            }
        }

        private static void ClearGameObjectPreviewCaches(EditorWindow owner)
        {
            var tracker = owner != null
                ? GetFieldValueRecursive<ActiveEditorTracker>(owner, "m_Tracker")
                : null;
            if (tracker == null)
                return;

            foreach (var editor in tracker.activeEditors)
            {
                if (editor == null ||
                    editor.GetType().FullName != "UnityEditor.GameObjectInspector")
                    continue;

                try
                {
                    editor.GetType()
                        .GetMethod("ClearPreviewCache", InstanceFields)
                        ?.Invoke(editor, null);
                    editor.Repaint();
                }
                catch
                {
                    // The preview can be rebuilt while a selection changes.
                }
            }
        }

        private static Quaternion GetHorizontalCameraRotation(Camera camera)
        {
            if (camera == null)
                return Quaternion.identity;

            var forward = camera.transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
        }

        private static Material[] CreatePipelineCompatibleMaterials(
            Material[] sourceMaterials, List<Material> ownedMaterials)
        {
            var urp = GraphicsSettings.currentRenderPipeline != null;
            var shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");

            var count = Mathf.Max(sourceMaterials?.Length ?? 0, 1);
            var result = new Material[count];
            for (var i = 0; i < count; i++)
            {
                var source = sourceMaterials != null && i < sourceMaterials.Length
                    ? sourceMaterials[i]
                    : null;
                var material = new Material(shader)
                {
                    name = "zEditorTools Preview Material",
                    hideFlags = HideFlags.HideAndDontSave
                };

                var useDefaultTextureFallback = string.Equals(
                    AssetDatabase.GetAssetPath(sourceModel), ModelPath,
                    StringComparison.OrdinalIgnoreCase);
                var texture = GetMainTexture(source) ??
                              (useDefaultTextureFallback ? sourceTexture : null);
                var color = GetMainColor(source);
                SetTextureIfPresent(material, "_BaseMap", texture);
                SetTextureIfPresent(material, "_MainTex", texture);
                SetColorIfPresent(material, "_BaseColor", color);
                SetColorIfPresent(material, "_Color", color);

                if (source != null)
                {
                    CopyTextureProperty(source, material, "_BumpMap");
                    CopyFloatProperty(source, material, "_Smoothness");
                    CopyFloatProperty(source, material, "_Glossiness");
                    CopyFloatProperty(source, material, "_Metallic");
                }

                ownedMaterials.Add(material);
                result[i] = material;
            }

            return result;
        }

        private static Light[] CreateAmbientLights(PreviewRenderUtility utility)
        {
            var result = new Light[AmbientLightDirections.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var lightObject = new GameObject("zEditorTools Preview Ambient Light")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                utility.AddSingleGO(lightObject);
                result[i] = light;
            }

            return result;
        }

        private static Texture GetMainTexture(Material material)
        {
            if (material == null)
                return null;
            if (material.HasProperty("_BaseMap"))
                return material.GetTexture("_BaseMap");
            return material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        }

        private static Color GetMainColor(Material material)
        {
            if (material == null)
                return Color.white;
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            return material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        }

        private static void SetTextureIfPresent(Material material, string property, Texture value)
        {
            if (value != null && material.HasProperty(property))
                material.SetTexture(property, value);
        }

        private static void SetColorIfPresent(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
                material.SetColor(property, value);
        }

        private static void CopyTextureProperty(Material source, Material target, string property)
        {
            if (source.HasProperty(property) && target.HasProperty(property))
                target.SetTexture(property, source.GetTexture(property));
        }

        private static void CopyFloatProperty(Material source, Material target, string property)
        {
            if (source.HasProperty(property) && target.HasProperty(property))
                target.SetFloat(property, source.GetFloat(property));
        }

        private static Bounds CalculateBoundsInRootSpace(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            var rootWorldToLocal = root.transform.worldToLocalMatrix;
            var bounds = new Bounds();
            var initialized = false;

            foreach (var renderer in renderers)
            {
                var worldBounds = renderer.bounds;
                var min = worldBounds.min;
                var max = worldBounds.max;
                for (var x = 0; x < 2; x++)
                for (var y = 0; y < 2; y++)
                for (var z = 0; z < 2; z++)
                {
                    var corner = new Vector3(x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    var localCorner = rootWorldToLocal.MultiplyPoint3x4(corner);
                    if (!initialized)
                    {
                        bounds = new Bounds(localCorner, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localCorner);
                    }
                }
            }

            return bounds;
        }

        private static void PositionForCamera(Camera camera)
        {
            PreparePreview(camera);
        }

        private static void PositionForRenderPipelineCamera(ScriptableRenderContext _, Camera camera)
        {
            PreparePreview(camera);
        }

        private static void RestoreAfterCamera(Camera camera)
        {
            RestoreCameraState(camera);
        }

        private static void RestoreAfterRenderPipelineCamera(
            ScriptableRenderContext _, Camera camera)
        {
            RestoreCameraState(camera);
        }

        private static void PreparePreview(Camera camera)
        {
            if (camera == null)
                return;

            foreach (var pair in Instances)
            {
                var entry = pair.Value;
                if (entry.Camera != camera || entry.Instance == null)
                    continue;

                if (!IsInteractivePreview(pair.Key))
                {
                    // MaterialEditor reuses one utility for the interactive preview and
                    // the small static header icon. Always restore the native camera
                    // before a static render so interactive zoom cannot leak into it.
                    RestoreCameraState(camera);
                    SetPreviewAdditionsEnabled(entry, false);
                    continue;
                }

                SetPreviewAdditionsEnabled(entry, true);
                ApplyLighting(pair.Key);
                var targetBounds = GetTargetBounds(entry, camera);
                RememberCameraState(entry, camera);
                ApplyScrollZoom(entry, camera, targetBounds.center);
                PositionInPreviewWorld(entry, targetBounds, camera);
            }
        }

        private static void RememberCameraState(BackgroundInstance entry, Camera camera)
        {
            if (entry.HasCameraRestoreState)
                return;

            entry.CameraPositionBeforePreview = camera.transform.position;
            entry.OrthographicSizeBeforePreview = camera.orthographicSize;
            entry.HasCameraRestoreState = true;
        }

        private static void RestoreCameraState(Camera camera)
        {
            if (camera == null)
                return;

            foreach (var entry in Instances.Values)
            {
                if (entry.Camera != camera || !entry.HasCameraRestoreState)
                    continue;

                camera.transform.position = entry.CameraPositionBeforePreview;
                camera.orthographicSize = entry.OrthographicSizeBeforePreview;
                entry.HasCameraRestoreState = false;
            }
        }

        private static bool IsInteractivePreview(PreviewRenderUtility utility)
        {
            try
            {
                return PreviewOpenedField?.GetValue(utility) is true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetPreviewAdditionsEnabled(
            BackgroundInstance entry, bool enabled)
        {
            SetRenderersEnabled(entry, enabled);
            foreach (var ambientLight in entry.AmbientLights)
            {
                if (ambientLight != null)
                    ambientLight.enabled = enabled;
            }
        }

        private static void ApplyScrollZoom(
            BackgroundInstance entry, Camera camera, Vector3 focusPoint)
        {
            if (pendingWheelDelta != 0f &&
                EditorApplication.timeSinceStartup > pendingWheelExpiry)
                pendingWheelDelta = 0f;

            var oldZoom = entry.ZoomMultiplier;
            var consumeWheel = pendingWheelDelta != 0f;
            if (consumeWheel)
            {
                entry.ZoomMultiplier = Mathf.Clamp(
                    oldZoom * Mathf.Exp(pendingWheelDelta * 0.035f), 0.2f, 8f);
                pendingWheelDelta = 0f;
            }

            if (camera.orthographic)
            {
                var currentSize = camera.orthographicSize;
                var alreadyApplied = entry.LastAppliedOrthographicSize > 0f &&
                                     Mathf.Abs(currentSize - entry.LastAppliedOrthographicSize) <
                                     Mathf.Max(currentSize, 1f) * 0.0001f;
                var baseSize = alreadyApplied
                    ? currentSize / Mathf.Max(oldZoom, 0.0001f)
                    : currentSize;
                var newSize = baseSize * entry.ZoomMultiplier;
                camera.orthographicSize = newSize;
                entry.LastAppliedOrthographicSize = newSize;
                return;
            }

            var offset = camera.transform.position - focusPoint;
            var currentDistance = offset.magnitude;
            if (currentDistance <= 0.0001f)
                return;

            var distanceAlreadyApplied = entry.LastAppliedCameraDistance > 0f &&
                                         Mathf.Abs(currentDistance - entry.LastAppliedCameraDistance) <
                                         Mathf.Max(currentDistance, 1f) * 0.0001f;
            var baseDistance = distanceAlreadyApplied
                ? currentDistance / Mathf.Max(oldZoom, 0.0001f)
                : currentDistance;
            var newDistance = baseDistance * entry.ZoomMultiplier;
            camera.transform.position = focusPoint + offset / currentDistance * newDistance;
            entry.LastAppliedCameraDistance = newDistance;
        }

        private static Bounds GetTargetBounds(BackgroundInstance entry, Camera camera)
        {
            if (entry.Context.Kind == PreviewKind.Material)
            {
                var materialMeshBounds = entry.Context.Mesh != null
                    ? entry.Context.Mesh.bounds
                    : new Bounds(Vector3.zero, Vector3.one * 2f);
                return new Bounds(Vector3.zero, materialMeshBounds.size);
            }

            if (entry.Context.GameObject != null &&
                TryGetRendererBounds(entry.Context.GameObject, entry.Instance, out var prefabBounds))
                return prefabBounds;

            // Explicitly covers PrefabImporter/GameObjectInspector previews even when Unity
            // changes the private PreviewData layout.
            var previewScene = camera.gameObject.scene;
            if (previewScene.IsValid())
            {
                var initialized = false;
                var sceneBounds = new Bounds();
                foreach (var root in previewScene.GetRootGameObjects())
                {
                    if (root == entry.Instance || root == camera.gameObject)
                        continue;
                    if (!TryGetRendererBounds(root, entry.Instance, out var rootBounds))
                        continue;

                    if (!initialized)
                    {
                        sceneBounds = rootBounds;
                        initialized = true;
                    }
                    else
                    {
                        sceneBounds.Encapsulate(rootBounds);
                    }
                }

                if (initialized)
                    return sceneBounds;
            }

            var cameraTransform = camera.transform;
            var depth = Mathf.Max(Vector3.Dot(-cameraTransform.position, cameraTransform.forward), 1f);
            var visibleHeight = camera.orthographic
                ? camera.orthographicSize * 2f
                : 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Bounds(Vector3.zero, Vector3.one * Mathf.Max(visibleHeight * 0.45f, 0.1f));
        }

        private static bool TryGetRendererBounds(
            GameObject root, GameObject excludedRoot, out Bounds bounds)
        {
            bounds = default;
            var initialized = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer.transform.IsChildOf(excludedRoot.transform))
                    continue;

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return initialized;
        }

        private static void PositionInPreviewWorld(
            BackgroundInstance entry, Bounds targetBounds, Camera camera)
        {
            var targetSize = Mathf.Max(
                targetBounds.size.x, targetBounds.size.y, targetBounds.size.z, 0.1f);
            var localModelSize = Mathf.Max(
                entry.LocalBounds.size.x, entry.LocalBounds.size.y,
                entry.LocalBounds.size.z, 0.0001f);
            var scale = targetSize * BackgroundSizeRelativeToTarget / localModelSize;
            var modelRadius = entry.LocalBounds.extents.magnitude * scale;
            var targetRadius = Mathf.Max(targetBounds.extents.magnitude, targetSize * 0.5f);

            // Model size is determined only by the two bounding boxes. It never changes
            // because of camera zoom or orbit.
            SetRenderersEnabled(entry, true);
            var diagonal = new Vector3(1f, 0f, 1f).normalized;
            var canonicalOffset =
                diagonal * (targetRadius * 0.72f +
                            modelRadius * (BackgroundSeparation - 0.38f)) +
                ModelPreviewLightingSettings.ModelOffset * targetSize;
            var desiredCenter =
                targetBounds.center + entry.PlacementRotation * canonicalOffset;

            // Keep it fixed in preview-world space and looking horizontally at world origin.
            // Orbiting the preview camera therefore rotates the whole composition naturally.
            var lookAt = new Vector3(0f, desiredCenter.y, 0f);
            var lookDirection = lookAt - desiredCenter;
            var rotation = lookDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                : Quaternion.identity;

            var transform = entry.Instance.transform;
            transform.localScale = Vector3.one * scale;
            transform.rotation = rotation;
            transform.position = desiredCenter - rotation * (entry.LocalBounds.center * scale);

            // Keep Unity's native orbit/scroll zoom untouched. Only widen the clipping
            // range to include the complete preview-world composition.
            var compositionRadius = Mathf.Max(
                targetRadius,
                Vector3.Distance(desiredCenter, targetBounds.center) + modelRadius);
            var cameraTransform = camera.transform;
            var orbitOffset = cameraTransform.position - targetBounds.center;
            var cameraDistance = orbitOffset.magnitude;

            var nearestSurface = Mathf.Max(cameraDistance - compositionRadius, 0.001f);
            camera.nearClipPlane = Mathf.Max(
                0.001f, Mathf.Min(camera.nearClipPlane, nearestSurface * 0.2f));
            camera.farClipPlane = Mathf.Max(
                camera.farClipPlane, cameraDistance + compositionRadius * 1.5f);
        }

        private static void SetRenderersEnabled(BackgroundInstance entry, bool enabled)
        {
            if (entry.RenderersEnabled == enabled)
                return;

            foreach (var renderer in entry.Renderers)
            {
                if (renderer != null)
                    renderer.enabled = enabled;
            }

            entry.RenderersEnabled = enabled;
        }

        private static void ApplyLighting(PreviewRenderUtility utility)
        {
            try
            {
                var lights = utility.lights;
                if (lights != null && lights.Length > 0 && lights[0] != null)
                {
                    lights[0].type = LightType.Directional;
                    lights[0].color = ModelPreviewLightingSettings.DirectionalColor;
                    lights[0].intensity = ModelPreviewLightingSettings.DirectionalIntensity;
                    lights[0].transform.rotation =
                        Quaternion.Euler(ModelPreviewLightingSettings.DirectionalEuler);
                    lights[0].shadows = LightShadows.None;
                }

                if (lights != null && lights.Length > 1 && lights[1] != null)
                    lights[1].intensity = 0f;

                utility.ambientColor =
                    ModelPreviewLightingSettings.AmbientColor *
                    ModelPreviewLightingSettings.AmbientIntensity;

                if (Instances.TryGetValue(utility, out var entry))
                {
                    var ambientIntensity =
                        ModelPreviewLightingSettings.AmbientIntensity * 0.5f;
                    for (var i = 0; i < entry.AmbientLights.Length; i++)
                    {
                        var ambientLight = entry.AmbientLights[i];
                        if (ambientLight == null)
                            continue;

                        ambientLight.color = ModelPreviewLightingSettings.AmbientColor;
                        ambientLight.intensity = ambientIntensity;
                        var up = Mathf.Abs(Vector3.Dot(
                            AmbientLightDirections[i], Vector3.up)) > 0.99f
                            ? Vector3.forward
                            : Vector3.up;
                        ambientLight.transform.rotation =
                            Quaternion.LookRotation(AmbientLightDirections[i], up);
                    }
                }
            }
            catch
            {
                // Preview may have been disposed during an Inspector rebuild.
            }
        }

        private static void RemoveUnusedInstances()
        {
            var stale = new List<PreviewRenderUtility>();
            foreach (var pair in Instances)
            {
                if (!FoundUtilities.TryGetValue(pair.Key, out var context) ||
                    !context.IsSupported || pair.Value.Camera == null)
                    stale.Add(pair.Key);
            }

            foreach (var utility in stale)
            {
                DestroyInstance(Instances[utility]);
                Instances.Remove(utility);
            }
        }

        private static void RemoveAllInstances()
        {
            foreach (var entry in Instances.Values)
                DestroyInstance(entry);
            Instances.Clear();
        }

        private static void DestroyInstance(BackgroundInstance entry)
        {
            if (entry.HasCameraRestoreState && entry.Camera != null)
            {
                entry.Camera.transform.position = entry.CameraPositionBeforePreview;
                entry.Camera.orthographicSize = entry.OrthographicSizeBeforePreview;
                entry.HasCameraRestoreState = false;
            }
            if (entry.Instance != null)
                Object.DestroyImmediate(entry.Instance);
            foreach (var ambientLight in entry.AmbientLights)
            {
                if (ambientLight != null)
                    Object.DestroyImmediate(ambientLight.gameObject);
            }
            foreach (var material in entry.OwnedMaterials)
            {
                if (material != null)
                    Object.DestroyImmediate(material);
            }
        }

        private static void Cleanup()
        {
            EditorApplication.update -= Update;
            Selection.selectionChanged -= OnSelectionChanged;
            Camera.onPreCull -= PositionForCamera;
            Camera.onPostRender -= RestoreAfterCamera;
            RenderPipelineManager.beginCameraRendering -= PositionForRenderPipelineCamera;
            RenderPipelineManager.endCameraRendering -= RestoreAfterRenderPipelineCamera;
            EditorApplication.delayCall -= RepaintAllViews;
            RemoveAllInstances();
            foreach (var window in new List<EditorWindow>(WheelContainers.Keys))
                UnregisterWheelHandler(window);
            foreach (var button in SettingsButtons.Values)
            {
                if (button != null)
                    button.RemoveFromHierarchy();
            }
            SettingsButtons.Clear();
            sourceModel = null;
            sourceTexture = null;
        }

        private static void SetHideFlagsRecursively(GameObject root, HideFlags flags)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                transform.gameObject.hideFlags = flags;
        }

        private static void RepaintAllViews()
        {
            foreach (var editor in ActiveEditorTracker.sharedTracker.activeEditors)
            {
                if (editor != null)
                    editor.Repaint();
            }

            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                var windowTypeName = window.GetType().Name;
                if (windowTypeName == "InspectorWindow" ||
                    windowTypeName == "PropertyEditor" ||
                    windowTypeName == "PreviewWindow" ||
                    window is ModelPreviewLightingWindow)
                {
                    window.Repaint();
                    window.rootVisualElement.MarkDirtyRepaint();
                }
            }

            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private enum PreviewKind
        {
            None,
            GameObject,
            Material,
            Mesh
        }

        private readonly struct PreviewContext
        {
            private PreviewContext(PreviewKind kind, Mesh mesh, GameObject gameObject)
            {
                Kind = kind;
                Mesh = mesh;
                GameObject = gameObject;
            }

            public PreviewKind Kind { get; }
            public Mesh Mesh { get; }
            public GameObject GameObject { get; }
            public bool IsSupported =>
                Kind == PreviewKind.GameObject || Kind == PreviewKind.Material;
            public PreviewContext WithGameObjectPreview() =>
                new(PreviewKind.GameObject, Mesh, GameObject);
            public PreviewContext WithMesh(Mesh mesh) =>
                new(PreviewKind.Mesh, mesh, GameObject);
            public PreviewContext WithGameObject(GameObject gameObject) =>
                gameObject != null
                    ? new PreviewContext(PreviewKind.GameObject, Mesh, gameObject)
                    : WithGameObjectPreview();
            public static PreviewContext ForMaterial(Mesh mesh) =>
                new(PreviewKind.Material, mesh, null);
            public bool IsMoreSpecificThan(PreviewContext other) =>
                Kind != PreviewKind.None && other.Kind == PreviewKind.None;
            public bool RefersToSameTarget(PreviewContext other) =>
                Kind == other.Kind && Mesh == other.Mesh && GameObject == other.GameObject;
        }

        private sealed class BackgroundInstance
        {
            public BackgroundInstance(Camera camera, GameObject instance, Bounds localBounds,
                Renderer[] renderers, Light[] ambientLights,
                List<Material> ownedMaterials, PreviewContext context,
                Quaternion placementRotation)
            {
                Camera = camera;
                Instance = instance;
                LocalBounds = localBounds;
                Renderers = renderers;
                AmbientLights = ambientLights;
                OwnedMaterials = ownedMaterials;
                Context = context;
                PlacementRotation = placementRotation;
                RenderersEnabled = true;
                ZoomMultiplier = 1f;
            }

            public Camera Camera { get; }
            public GameObject Instance { get; }
            public Bounds LocalBounds { get; }
            public Renderer[] Renderers { get; }
            public Light[] AmbientLights { get; }
            public List<Material> OwnedMaterials { get; }
            public PreviewContext Context { get; set; }
            public Quaternion PlacementRotation { get; set; }
            public bool RenderersEnabled { get; set; }
            public float ZoomMultiplier { get; set; }
            public float LastAppliedCameraDistance { get; set; }
            public float LastAppliedOrthographicSize { get; set; }
            public bool HasCameraRestoreState { get; set; }
            public Vector3 CameraPositionBeforePreview { get; set; }
            public float OrthographicSizeBeforePreview { get; set; }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    internal static class ModelPreviewLightingSettings
    {
        private const string Prefix = "zEditorTools.ModelPreviewLighting.";

        public static float DirectionalIntensity
        {
            get => EditorPrefs.GetFloat(Prefix + "DirectionalIntensity", 1.2f);
            set => EditorPrefs.SetFloat(Prefix + "DirectionalIntensity", value);
        }

        public static Color DirectionalColor
        {
            get => GetColor("DirectionalColor", Color.white);
            set => SetColor("DirectionalColor", value);
        }

        public static Vector3 DirectionalEuler
        {
            get => new(EditorPrefs.GetFloat(Prefix + "DirectionX", 35f),
                EditorPrefs.GetFloat(Prefix + "DirectionY", -35f),
                EditorPrefs.GetFloat(Prefix + "DirectionZ", 0f));
            set
            {
                EditorPrefs.SetFloat(Prefix + "DirectionX", value.x);
                EditorPrefs.SetFloat(Prefix + "DirectionY", value.y);
                EditorPrefs.SetFloat(Prefix + "DirectionZ", value.z);
            }
        }

        public static float AmbientIntensity
        {
            get => EditorPrefs.GetFloat(Prefix + "AmbientIntensity", 0.55f);
            set => EditorPrefs.SetFloat(Prefix + "AmbientIntensity", value);
        }

        public static Color AmbientColor
        {
            get => GetColor("AmbientColor", new Color(0.55f, 0.62f, 0.72f, 1f));
            set => SetColor("AmbientColor", value);
        }

        public static Vector3 ModelOffset
        {
            get => new(EditorPrefs.GetFloat(Prefix + "ModelOffsetX", 0f),
                EditorPrefs.GetFloat(Prefix + "ModelOffsetY", 0f),
                EditorPrefs.GetFloat(Prefix + "ModelOffsetZ", 0f));
            set
            {
                EditorPrefs.SetFloat(Prefix + "ModelOffsetX", value.x);
                EditorPrefs.SetFloat(Prefix + "ModelOffsetY", value.y);
                EditorPrefs.SetFloat(Prefix + "ModelOffsetZ", value.z);
            }
        }

        public static void ResetLighting()
        {
            DirectionalIntensity = 1.2f;
            DirectionalColor = Color.white;
            DirectionalEuler = new Vector3(35f, -35f, 0f);
            AmbientIntensity = 0.55f;
            AmbientColor = new Color(0.55f, 0.62f, 0.72f, 1f);
        }

        private static Color GetColor(string key, Color fallback)
        {
            return new Color(
                EditorPrefs.GetFloat(Prefix + key + "R", fallback.r),
                EditorPrefs.GetFloat(Prefix + key + "G", fallback.g),
                EditorPrefs.GetFloat(Prefix + key + "B", fallback.b),
                EditorPrefs.GetFloat(Prefix + key + "A", fallback.a));
        }

        private static void SetColor(string key, Color value)
        {
            EditorPrefs.SetFloat(Prefix + key + "R", value.r);
            EditorPrefs.SetFloat(Prefix + key + "G", value.g);
            EditorPrefs.SetFloat(Prefix + key + "B", value.b);
            EditorPrefs.SetFloat(Prefix + key + "A", value.a);
        }
    }

    internal sealed class ModelPreviewLightingWindow : EditorWindow
    {
        private static ModelPreviewLightingWindow instance;
        private EditorWindow ownerWindow;
        private Vector2 scrollPosition;
        private string decorationModelPath;
        private string decorationModelPathError;

        internal static void Open(EditorWindow owner, Rect localActivatorRect)
        {
            var wasOpenForSameOwner = false;
            foreach (var oldWindow in Resources.FindObjectsOfTypeAll<ModelPreviewLightingWindow>())
            {
                if (oldWindow != null)
                {
                    wasOpenForSameOwner |= oldWindow.ownerWindow == owner;
                    oldWindow.Close();
                }
            }

            if (wasOpenForSameOwner)
                return;

            instance = CreateInstance<ModelPreviewLightingWindow>();
            instance.ownerWindow = owner;
            var screenRect = localActivatorRect;
            screenRect.position += owner.position.position;
            var popupSize = new Vector2(380f, 340f);
            var popupPosition = new Vector2(
                screenRect.xMin,
                Mathf.Max(0f, screenRect.yMin - popupSize.y - 4f));
            instance.position = new Rect(popupPosition, popupSize);
            instance.ShowPopup();
            instance.position = new Rect(popupPosition, popupSize);
        }

        private void OnEnable()
        {
            instance = this;
            decorationModelPath = ModelPreviewBackground.DecorationModelPath;
            decorationModelPathError = string.Empty;
            EditorApplication.update += CloseWhenOwnerIsGone;
        }

        private void OnDisable()
        {
            EditorApplication.update -= CloseWhenOwnerIsGone;
            if (instance == this)
                instance = null;
        }

        private void CloseWhenOwnerIsGone()
        {
            if (ownerWindow == null)
                Close();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("模型预览设置", EditorStyles.boldLabel);
                if (GUILayout.Button("×", GUILayout.Width(24f), GUILayout.Height(18f)))
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.Space(3f);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUI.BeginChangeCheck();
            var decorationModel = EditorGUILayout.ObjectField(
                "模型资源", ModelPreviewBackground.DecorationModel,
                typeof(GameObject), false) as GameObject;
            if (EditorGUI.EndChangeCheck())
            {
                ModelPreviewBackground.DecorationModel = decorationModel;
                decorationModelPath = ModelPreviewBackground.DecorationModelPath;
                decorationModelPathError = string.Empty;
            }

            decorationModelPath = EditorGUILayout.TextField("模型路径", decorationModelPath);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("应用路径"))
                {
                    if (ModelPreviewBackground.TrySetDecorationModelPath(decorationModelPath, out decorationModelPathError))
                        decorationModelPath = ModelPreviewBackground.DecorationModelPath;
                }

                if (GUILayout.Button("恢复默认模型"))
                {
                    ModelPreviewBackground.ResetDecorationModel();
                    decorationModelPath = ModelPreviewBackground.DecorationModelPath;
                    decorationModelPathError = string.Empty;
                }
            }

            if (!string.IsNullOrEmpty(decorationModelPathError))
                EditorGUILayout.HelpBox(decorationModelPathError, MessageType.Error);

            EditorGUILayout.Space(6f);
            EditorGUI.BeginChangeCheck();
            var featureEnabled = EditorGUILayout.ToggleLeft(
                "启用模型预览背景", ModelPreviewBackground.FeatureEnabled);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("模型位置", EditorStyles.boldLabel);
            var modelOffset = EditorGUILayout.Vector3Field(
                "Offset（BBox 倍数）", ModelPreviewLightingSettings.ModelOffset);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("方向光", EditorStyles.boldLabel);
            var intensity = EditorGUILayout.Slider(
                "强度", ModelPreviewLightingSettings.DirectionalIntensity, 0f, 8f);
            var color = EditorGUILayout.ColorField(
                "颜色", ModelPreviewLightingSettings.DirectionalColor);
            var euler = EditorGUILayout.Vector3Field(
                "方向（欧拉角）", ModelPreviewLightingSettings.DirectionalEuler);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("环境填充光（AO）", EditorStyles.boldLabel);
            var ambientColor = EditorGUILayout.ColorField(
                "颜色", ModelPreviewLightingSettings.AmbientColor);
            var ambientIntensity = EditorGUILayout.Slider(
                "强度", ModelPreviewLightingSettings.AmbientIntensity, 0f, 2f);

            if (EditorGUI.EndChangeCheck())
            {
                ModelPreviewBackground.FeatureEnabled = featureEnabled;
                ModelPreviewLightingSettings.ModelOffset = modelOffset;
                ModelPreviewLightingSettings.DirectionalIntensity = intensity;
                ModelPreviewLightingSettings.DirectionalColor = color;
                ModelPreviewLightingSettings.DirectionalEuler = euler;
                ModelPreviewLightingSettings.AmbientColor = ambientColor;
                ModelPreviewLightingSettings.AmbientIntensity = ambientIntensity;
                ModelPreviewBackground.SettingsChanged();
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("恢复默认值"))
            {
                ModelPreviewLightingSettings.ResetLighting();
                ModelPreviewBackground.SettingsChanged();
                Repaint();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.EndScrollView();
        }
    }
}
