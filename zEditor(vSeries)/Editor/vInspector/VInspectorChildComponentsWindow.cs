#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VInspector
{
    public class VInspectorChildComponentsWindow : EditorWindow
    {
        private const float typePanelWidth = 180f;

        private sealed class ComponentGroup
        {
            public Type type;
            public string displayName;
            public Texture icon;
            public readonly List<Component> components = new();
            public readonly List<GameObject> gameObjects = new();
        }

        [MenuItem("Window/zEditor Tools/Child Components")]
        public static void Open()
        {
            OpenFromSelection(Selection.activeGameObject);
        }

        public static void OpenFromSelection(GameObject gameObject)
        {
            var window = GetWindow<VInspectorChildComponentsWindow>("Child Components");
            window.SetRoot(gameObject);
            window.Show();
            window.Focus();
        }

        private readonly List<ComponentGroup> componentGroups = new();
        private readonly Dictionary<Type, HashSet<GameObject>> excludedGameObjectsByType = new();
        private static readonly Dictionary<Type, PropertyInfo> enabledPropertiesByType = new();

        private GameObject root;
        private Type selectedType;
        private UnityEditor.Editor componentEditor;
        private string typeSearch = string.Empty;
        private Vector2 typeScrollPosition;
        private Vector2 objectScrollPosition;
        private Vector2 inspectorScrollPosition;
        private bool includeRoot = true;
        private bool includeInactive = true;
        private bool ignoreSelectionChange;
        private bool ignoreHierarchyChange;

        private ComponentGroup SelectedGroup => componentGroups.FirstOrDefault(group => group.type == selectedType);

        private void OnEnable()
        {
            minSize = new Vector2(700, 360);
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            SetRoot(Selection.activeGameObject);
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            DestroyComponentEditor();
        }

        private void OnSelectionChanged()
        {
            if (ignoreSelectionChange) return;

            SetRoot(Selection.activeGameObject);
            Repaint();
        }

        private void OnHierarchyChanged()
        {
            if (ignoreHierarchyChange) return;

            RefreshGroups();
            Repaint();
        }

        private void OnUndoRedoPerformed()
        {
            RefreshGroups();
            Repaint();
        }

        private void SetRoot(GameObject gameObject)
        {
            if (root == gameObject)
            {
                RefreshGroups();
                return;
            }

            root = gameObject;
            selectedType = null;
            excludedGameObjectsByType.Clear();
            RefreshGroups();
        }

        private void RefreshGroups()
        {
            DestroyComponentEditor();
            componentGroups.Clear();

            if (!root) return;

            var groupsByType = new Dictionary<Type, ComponentGroup>();
            var gameObjectsByType = new Dictionary<Type, HashSet<GameObject>>();

            foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive))
            {
                if (!includeRoot && transform == root.transform) continue;

                foreach (var component in transform.GetComponents<Component>())
                {
                    if (!component) continue;

                    var type = component.GetType();
                    if (!groupsByType.TryGetValue(type, out var group))
                    {
                        group = CreateGroup(component);
                        groupsByType.Add(type, group);
                        gameObjectsByType.Add(type, new HashSet<GameObject>());
                    }

                    group.components.Add(component);
                    if (gameObjectsByType[type].Add(transform.gameObject))
                        group.gameObjects.Add(transform.gameObject);
                }
            }

            componentGroups.AddRange(groupsByType.Values.OrderBy(group => group.displayName).ThenBy(group => group.type.FullName));

            foreach (var group in componentGroups)
                if (excludedGameObjectsByType.TryGetValue(group.type, out var excludedGameObjects))
                    excludedGameObjects.RemoveWhere(gameObject => !gameObject || !group.gameObjects.Contains(gameObject));

            foreach (var type in excludedGameObjectsByType.Keys.Where(type => componentGroups.All(group => group.type != type)).ToList())
                excludedGameObjectsByType.Remove(type);

            if (selectedType == null || componentGroups.All(group => group.type != selectedType))
                selectedType = componentGroups.FirstOrDefault()?.type;

            CreateComponentEditor();
        }

        private static ComponentGroup CreateGroup(Component component)
        {
            var type = component.GetType();
            return new ComponentGroup
            {
                type = type,
                displayName = ObjectNames.NicifyVariableName(type.Name),
                icon = EditorGUIUtility.ObjectContent(component, type).image
            };
        }

        private void SelectGroup(Type type)
        {
            if (selectedType == type) return;

            selectedType = type;
            inspectorScrollPosition = Vector2.zero;
            objectScrollPosition = Vector2.zero;
            DestroyComponentEditor();
            CreateComponentEditor();
            Repaint();
        }

        private void CreateComponentEditor()
        {
            var group = SelectedGroup;
            if (group == null || group.components.Count == 0) return;

            // A single Editor with same-type targets provides Unity's native mixed-value and multi-edit behavior.
            var targets = GetIncludedComponents(group).Cast<Object>().ToArray();
            if (targets.Length == 0) return;

            componentEditor = UnityEditor.Editor.CreateEditor(targets);
        }

        private void DestroyComponentEditor()
        {
            if (!componentEditor) return;

            DestroyImmediate(componentEditor);
            componentEditor = null;
        }

        private void OnGUI()
        {
            DrawRootOptions();

            if (!root)
            {
                EditorGUILayout.HelpBox("Select a scene GameObject to inspect all component types on it and its children.", MessageType.Info);
                return;
            }

            if (!root.scene.IsValid())
            {
                EditorGUILayout.HelpBox("The selected object is not a scene object.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawTypePanel();
            DrawSeparator();
            DrawSelectedGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRootOptions()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var nextRoot = (GameObject)EditorGUILayout.ObjectField("Root", root, typeof(GameObject), true, GUILayout.MinWidth(180));
            if (nextRoot != root)
                SetRoot(nextRoot);

            var nextIncludeRoot = GUILayout.Toggle(includeRoot, "Root", EditorStyles.toolbarButton, GUILayout.Width(48));
            if (nextIncludeRoot != includeRoot)
            {
                includeRoot = nextIncludeRoot;
                RefreshGroups();
            }

            var nextIncludeInactive = GUILayout.Toggle(includeInactive, "Inactive", EditorStyles.toolbarButton, GUILayout.Width(62));
            if (nextIncludeInactive != includeInactive)
            {
                includeInactive = nextIncludeInactive;
                RefreshGroups();
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(58)))
                RefreshGroups();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTypePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(typePanelWidth), GUILayout.MaxWidth(typePanelWidth), GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            typeSearch = EditorGUILayout.TextField(typeSearch, EditorStyles.toolbarSearchField, GUILayout.Width(typePanelWidth - 8));
            EditorGUILayout.EndHorizontal();

            var filteredGroups = componentGroups.Where(MatchesTypeSearch).ToList();
            typeScrollPosition = EditorGUILayout.BeginScrollView(typeScrollPosition, false, true, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (filteredGroups.Count == 0)
                EditorGUILayout.HelpBox("No component types found.", MessageType.Info);

            foreach (var group in filteredGroups)
                DrawType(group);

            EditorGUILayout.EndScrollView();
            GUILayout.Label(string.Format("{0} component types", filteredGroups.Count), EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private bool MatchesTypeSearch(ComponentGroup group)
        {
            if (string.IsNullOrWhiteSpace(typeSearch)) return true;

            return group.displayName.IndexOf(typeSearch, StringComparison.OrdinalIgnoreCase) >= 0 || group.type.FullName != null && group.type.FullName.IndexOf(typeSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawType(ComponentGroup group)
        {
            var selected = group.type == selectedType;
            var rowRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            if (GUI.Toggle(rowRect, selected, GUIContent.none, EditorStyles.toolbarButton) && !selected)
                SelectGroup(group.type);

            var iconRect = new Rect(rowRect.x + 5, rowRect.y + 3, 18, 18);
            if (group.icon)
                GUI.DrawTexture(iconRect, group.icon, ScaleMode.ScaleToFit);

            var countRect = new Rect(rowRect.xMax - 34, rowRect.y, 28, rowRect.height);
            var nameRect = new Rect(iconRect.xMax + 3, rowRect.y, Mathf.Max(0, countRect.x - iconRect.xMax - 5), rowRect.height);
            GUI.Label(nameRect, new GUIContent(group.displayName, group.type.FullName), EditorStyles.label);
            GUI.Label(countRect, group.components.Count.ToString(), EditorStyles.miniLabel);
        }

        private static void DrawSeparator()
        {
            var previousColor = GUI.color;
            GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, .25f);
            GUILayout.Box(GUIContent.none, GUILayout.Width(1), GUILayout.ExpandHeight(true));
            GUI.color = previousColor;
        }

        private void DrawSelectedGroup()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            var group = SelectedGroup;
            if (group == null)
            {
                EditorGUILayout.HelpBox("Select a component type.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawGroupHeader(group);
            if (DrawObjectList(group))
            {
                DestroyComponentEditor();
                CreateComponentEditor();
            }
            DrawMultiObjectInspector(group);
            EditorGUILayout.EndVertical();
        }

        private void DrawGroupHeader(ComponentGroup group)
        {
            var includedComponents = GetIncludedComponents(group);
            var includedGameObjects = GetIncludedGameObjects(group);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (group.icon)
                GUILayout.Label(group.icon, GUILayout.Width(20), GUILayout.Height(18));

            GUILayout.Label(group.displayName, EditorStyles.boldLabel);
            GUILayout.Label(string.Format("{0}/{1} objects · {2} components", includedGameObjects.Count, group.gameObjects.Count, includedComponents.Count), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            var canToggleEnabled = GetEnabledProperty(group.type) != null;
            EditorGUI.BeginDisabledGroup(!canToggleEnabled || includedComponents.Count == 0);

            if (GUILayout.Button("Enable", EditorStyles.toolbarButton, GUILayout.Width(55)))
                SetGroupEnabled(group, includedComponents, true);

            if (GUILayout.Button("Disable", EditorStyles.toolbarButton, GUILayout.Width(60)))
                SetGroupEnabled(group, includedComponents, false);

            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(includedGameObjects.Count == 0);

            if (GUILayout.Button("Select objects", EditorStyles.toolbarButton, GUILayout.Width(86)))
                SelectObjects(includedGameObjects);

            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(typeof(Transform).IsAssignableFrom(group.type) || includedComponents.Count == 0);

            if (GUILayout.Button("Remove", EditorStyles.toolbarButton, GUILayout.Width(58)))
                RemoveComponents(group, includedComponents, includedGameObjects.Count);

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private bool DrawObjectList(ComponentGroup group)
        {
            var targetsChanged = false;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Objects participating in this edit", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(38)))
            {
                SetAllGameObjectsIncluded(group, true);
                targetsChanged = true;
            }

            if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(44)))
            {
                SetAllGameObjectsIncluded(group, false);
                targetsChanged = true;
            }

            EditorGUILayout.EndHorizontal();

            var listHeight = Mathf.Clamp(group.gameObjects.Count * 20f + 4f, 42f, 124f);
            objectScrollPosition = EditorGUILayout.BeginScrollView(objectScrollPosition, EditorStyles.helpBox, GUILayout.Height(listHeight));

            foreach (var gameObject in group.gameObjects)
            {
                EditorGUILayout.BeginHorizontal();
                var included = IsGameObjectIncluded(group, gameObject);
                var nextIncluded = EditorGUILayout.Toggle(included, GUILayout.Width(18));
                if (nextIncluded != included)
                {
                    SetGameObjectIncluded(group, gameObject, nextIncluded);
                    targetsChanged = true;
                }

                GUILayout.Label(GetPath(gameObject.transform), EditorStyles.label);

                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(38)))
                    EditorGUIUtility.PingObject(gameObject);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            return targetsChanged;
        }

        private void DrawMultiObjectInspector(ComponentGroup group)
        {
            GUILayout.Space(4);
            var includedComponentCount = GetIncludedComponents(group).Count;
            GUILayout.Label(string.Format("Multi-object Inspector ({0})", includedComponentCount), EditorStyles.boldLabel);

            if (!componentEditor)
            {
                EditorGUILayout.HelpBox(includedComponentCount == 0 ? "Select at least one object above to edit this component." : "Unity could not create an Inspector for this component type.", includedComponentCount == 0 ? MessageType.Info : MessageType.Warning);
                return;
            }

            inspectorScrollPosition = EditorGUILayout.BeginScrollView(inspectorScrollPosition, EditorStyles.helpBox, GUILayout.ExpandHeight(true));

            var previousHierarchyMode = EditorGUIUtility.hierarchyMode;
            var previousWideMode = EditorGUIUtility.wideMode;
            var previousLabelWidth = EditorGUIUtility.labelWidth;

            // Built-in inspectors such as Transform use the full window width unless the sub-panel width is supplied here.
            EditorGUIUtility.hierarchyMode = true;
            EditorGUIUtility.wideMode = true;
            EditorGUIUtility.labelWidth = Mathf.Clamp((position.width - typePanelWidth - 28) * .36f, 120, 180);

            try
            {
                componentEditor.OnInspectorGUI();
            }
            finally
            {
                EditorGUIUtility.hierarchyMode = previousHierarchyMode;
                EditorGUIUtility.wideMode = previousWideMode;
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }

            EditorGUILayout.EndScrollView();
        }

        private string GetPath(Transform transform)
        {
            var names = new Stack<string>();
            var current = transform;

            while (current && current != root.transform)
            {
                names.Push(current.name);
                current = current.parent;
            }

            if (names.Count == 0) return root.name;
            return string.Concat(root.name, "/", string.Join("/", names));
        }

        private List<Component> GetIncludedComponents(ComponentGroup group)
        {
            if (!excludedGameObjectsByType.TryGetValue(group.type, out var excludedGameObjects) || excludedGameObjects.Count == 0)
                return group.components.Where(component => component).ToList();

            return group.components.Where(component => component && !excludedGameObjects.Contains(component.gameObject)).ToList();
        }

        private List<GameObject> GetIncludedGameObjects(ComponentGroup group)
        {
            if (!excludedGameObjectsByType.TryGetValue(group.type, out var excludedGameObjects) || excludedGameObjects.Count == 0)
                return group.gameObjects.Where(gameObject => gameObject).ToList();

            return group.gameObjects.Where(gameObject => gameObject && !excludedGameObjects.Contains(gameObject)).ToList();
        }

        private bool IsGameObjectIncluded(ComponentGroup group, GameObject gameObject)
        {
            return !excludedGameObjectsByType.TryGetValue(group.type, out var excludedGameObjects) || !excludedGameObjects.Contains(gameObject);
        }

        private void SetGameObjectIncluded(ComponentGroup group, GameObject gameObject, bool included)
        {
            if (!excludedGameObjectsByType.TryGetValue(group.type, out var excludedGameObjects))
            {
                excludedGameObjects = new HashSet<GameObject>();
                excludedGameObjectsByType.Add(group.type, excludedGameObjects);
            }

            if (included)
                excludedGameObjects.Remove(gameObject);
            else
                excludedGameObjects.Add(gameObject);
        }

        private void SetAllGameObjectsIncluded(ComponentGroup group, bool included)
        {
            if (included)
            {
                excludedGameObjectsByType.Remove(group.type);
                return;
            }

            excludedGameObjectsByType[group.type] = new HashSet<GameObject>(group.gameObjects.Where(gameObject => gameObject));
        }

        private static PropertyInfo GetEnabledProperty(Type type)
        {
            if (enabledPropertiesByType.TryGetValue(type, out var property)) return property;

            property = type.GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(bool) || !property.CanRead || !property.CanWrite)
                property = null;

            enabledPropertiesByType[type] = property;
            return property;
        }

        private static void SetGroupEnabled(ComponentGroup group, List<Component> components, bool enabled)
        {
            var property = GetEnabledProperty(group.type);
            if (property == null || components.Count == 0) return;

            Undo.RecordObjects(components.Cast<Object>().ToArray(), enabled ? "Enable child components" : "Disable child components");

            foreach (var component in components)
            {
                property.SetValue(component, enabled);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }

        private void RemoveComponents(ComponentGroup group, List<Component> components, int gameObjectCount)
        {
            if (typeof(Transform).IsAssignableFrom(group.type)) return;
            if (components.Count == 0) return;
            if (!EditorUtility.DisplayDialog("Remove components", string.Format("Remove {0} selected {1} components from {2} objects?", components.Count, group.displayName, gameObjectCount), "Remove", "Cancel")) return;

            DestroyComponentEditor();
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(string.Concat("Remove ", group.displayName, " components"));

            ignoreHierarchyChange = true;
            try
            {
                foreach (var component in components)
                    if (component)
                        Undo.DestroyObjectImmediate(component);
            }
            finally
            {
                ignoreHierarchyChange = false;
            }

            Undo.CollapseUndoOperations(undoGroup);
            RefreshGroups();
            Repaint();
        }

        private void SelectObjects(List<GameObject> gameObjects)
        {
            var objects = gameObjects.Cast<Object>().ToArray();
            if (objects.Length == 0) return;

            ignoreSelectionChange = true;
            try
            {
                Selection.objects = objects;
            }
            finally
            {
                ignoreSelectionChange = false;
            }

            Repaint();
        }
    }
}
#endif
