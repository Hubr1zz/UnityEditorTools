#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static VInspector.Libs.VUtils;

namespace VInspector
{
    public class VInspectorComponentTabs
    {
        const float RowHeight = 23f;
        const float HorizontalPadding = 2f;
        const float VerticalPadding = 2f;
        const string HiddenEditorClass = "vInspector-component-tab-hidden";

        static readonly Dictionary<GameObject, HashSet<Component>> activeComponentsByGameObject = new();

        readonly HashSet<VisualElement> hiddenEditorElements = new();
        readonly EditorWindow window;

        GameObject gameObject;

        public float RequiredHeight { get; private set; } = RowHeight + VerticalPadding * 2f;

        public VInspectorComponentTabs(EditorWindow window)
        {
            this.window = window;
        }

        public bool Update()
        {
            if (!VInspectorMenu.componentTabsEnabled)
            {
                gameObject = null;
                RestoreHiddenEditors();
                return false;
            }

            var inspectedGameObject = GetInspectedGameObject();
            if (!inspectedGameObject)
            {
                gameObject = null;
                RestoreHiddenEditors();
                return false;
            }

            if (gameObject != inspectedGameObject)
            {
                RestoreHiddenEditors();
                gameObject = inspectedGameObject;
            }

            GetActiveComponents(gameObject);
            ApplyComponentVisibility();
            return true;
        }

        public void OnGUI(Rect rect)
        {
            if (!gameObject) return;

            var availableWidth = rect.width > 0f ? rect.width : GetAvailableWidth();
            var requiredHeight = CalculateRequiredHeight(gameObject, availableWidth);
            if (!Mathf.Approximately(RequiredHeight, requiredHeight))
            {
                RequiredHeight = requiredHeight;
                window.Repaint();
            }

            var components = gameObject.GetComponents<Component>().Where(component => component).ToArray();
            var activeComponents = GetActiveComponents(gameObject);

            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(.19f, .19f, .19f) : new Color(.76f, .76f, .76f));

            var x = HorizontalPadding;
            var y = VerticalPadding;
            foreach (var component in components)
            {
                var width = Mathf.Min(GetTabWidth(component), Mathf.Max(1f, rect.width - HorizontalPadding * 2f));
                if (x > HorizontalPadding && x + width > rect.width - HorizontalPadding)
                {
                    x = HorizontalPadding;
                    y += RowHeight;
                }

                DrawTab(new Rect(x, y, width, 22f), component, activeComponents);
                x += width;
            }
        }

        public void Detach()
        {
            gameObject = null;
            RestoreHiddenEditors();
        }

        public void DeselectAll()
        {
            if (!gameObject) return;

            GetActiveComponents(gameObject).Clear();
            ApplyComponentVisibility();
            window.Repaint();
        }

        void DrawTab(Rect rect, Component component, HashSet<Component> activeComponents)
        {
            var enabledState = EditorUtility.GetObjectEnabled(component);
            var hasEnabledToggle = enabledState >= 0;
            var toggleWidth = hasEnabledToggle ? 21f : 0f;
            var selectionRect = rect;
            selectionRect.width -= toggleWidth;

            var icon = AssetPreview.GetMiniThumbnail(component);
            var label = ObjectNames.NicifyVariableName(component.GetType().Name);
            var content = new GUIContent(label, icon, $"显示或隐藏 {label}");
            var isActive = activeComponents.Contains(component);

            if (Event.current.type == EventType.Repaint)
                EditorStyles.toolbarButton.Draw(rect, GUIContent.none, rect.Contains(Event.current.mousePosition), false, isActive, false);

            tabContentStyle ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(5, 2, 0, 0) };
            GUI.Label(selectionRect, content, tabContentStyle);

            if (GUI.Button(selectionRect, GUIContent.none, GUIStyle.none))
            {
                if (isActive)
                    activeComponents.Remove(component);
                else
                    activeComponents.Add(component);

                ApplyComponentVisibility();
                window.Repaint();
            }

            if (!hasEnabledToggle) return;

            var toggleRect = rect;
            toggleRect.xMin = selectionRect.xMax;
            toggleRect.width = 16f;
            toggleRect.height = 16f;
            toggleRect.center = new Vector2(rect.xMax - toggleWidth / 2f, rect.center.y);
            var enabled = enabledState == 1;
            var newEnabled = GUI.Toggle(toggleRect, enabled, GUIContent.none);
            if (newEnabled == enabled) return;

            Undo.RecordObject(component, "Toggle Component");
            EditorUtility.SetObjectEnabled(component, newEnabled);
            window.Repaint();
        }

        static GUIStyle tabContentStyle;

        void ApplyComponentVisibility()
        {
            if (!gameObject) return;

            var activeComponents = GetActiveComponents(gameObject);
            UpdateEditorElementVisibility(window.rootVisualElement, activeComponents);
        }

        void UpdateEditorElementVisibility(VisualElement element, HashSet<Component> activeComponents)
        {
            if (TryGetEditorElementComponent(element, out var component))
            {
                if (component.gameObject == gameObject && !activeComponents.Contains(component))
                {
                    element.style.display = DisplayStyle.None;
                    element.AddToClassList(HiddenEditorClass);
                    hiddenEditorElements.Add(element);
                }
                else if (element.ClassListContains(HiddenEditorClass))
                {
                    element.style.display = StyleKeyword.Null;
                    element.RemoveFromClassList(HiddenEditorClass);
                    hiddenEditorElements.Remove(element);
                }
            }

            foreach (var child in element.Children())
                UpdateEditorElementVisibility(child, activeComponents);
        }

        bool TryGetEditorElementComponent(VisualElement element, out Component component)
        {
            component = null;
            if (element.GetType().Name != "EditorElement") return false;

            var editorTargetField = element.GetType().GetFieldInfo("m_EditorTarget");
            component = editorTargetField?.GetValue(element) as Component;
            if (component) return true;

            var editorField = element.GetType().GetFieldInfo("m_Editor");
            component = (editorField?.GetValue(element) as Editor)?.target as Component;
            if (component) return true;

            component = FindNestedEditorComponent(element);
            return component;
        }

        Component FindNestedEditorComponent(VisualElement element)
        {
            foreach (var child in element.Children())
            {
                var editorField = child.GetType().GetFieldInfo("m_Editor");
                var component = (editorField?.GetValue(child) as Editor)?.target as Component;
                if (component) return component;

                component = FindNestedEditorComponent(child);
                if (component) return component;
            }

            return null;
        }

        GameObject GetInspectedGameObject()
        {
            var inspectedObjects = window.InvokeMethod<Object[]>("GetInspectedObjects");
            if (inspectedObjects == null || inspectedObjects.Length != 1) return null;
            return inspectedObjects[0] as GameObject;
        }

        static HashSet<Component> GetActiveComponents(GameObject target)
        {
            CleanupDestroyedObjects();

            if (!activeComponentsByGameObject.TryGetValue(target, out var activeComponents))
            {
                activeComponents = new HashSet<Component>();
                var firstComponent = target.GetComponents<Component>().FirstOrDefault(component => component);
                if (firstComponent)
                    activeComponents.Add(firstComponent);
                activeComponentsByGameObject[target] = activeComponents;
            }

            activeComponents.RemoveWhere(component => !component || component.gameObject != target);
            return activeComponents;
        }

        static void CleanupDestroyedObjects()
        {
            foreach (var target in activeComponentsByGameObject.Keys.Where(target => !target).ToArray())
                activeComponentsByGameObject.Remove(target);
        }

        static float GetTabWidth(Component component)
        {
            var label = ObjectNames.NicifyVariableName(component.GetType().Name);
            var labelWidth = EditorStyles.label.CalcSize(new GUIContent(label)).x;
            var toggleWidth = EditorUtility.GetObjectEnabled(component) >= 0 ? 21f : 0f;
            return Mathf.Clamp(labelWidth + 29f + toggleWidth, 72f, 220f);
        }

        float GetAvailableWidth()
        {
            var width = window.rootVisualElement.resolvedStyle.width;
            width = float.IsNaN(width) || width <= 0f ? window.position.width : width;
            return Mathf.Max(1f, width);
        }

        static float CalculateRequiredHeight(GameObject target, float availableWidth)
        {
            var rowCount = 1;
            var x = HorizontalPadding;
            var maxTabWidth = Mathf.Max(1f, availableWidth - HorizontalPadding * 2f);

            foreach (var component in target.GetComponents<Component>().Where(component => component))
            {
                var width = Mathf.Min(GetTabWidth(component), maxTabWidth);
                if (x > HorizontalPadding && x + width > availableWidth - HorizontalPadding)
                {
                    rowCount++;
                    x = HorizontalPadding;
                }

                x += width;
            }

            return rowCount * RowHeight + VerticalPadding * 2f;
        }

        void RestoreHiddenEditors()
        {
            RestoreMarkedEditorElements(window.rootVisualElement);

            foreach (var element in hiddenEditorElements)
                if (element?.panel != null)
                {
                    element.style.display = StyleKeyword.Null;
                    element.RemoveFromClassList(HiddenEditorClass);
                }

            hiddenEditorElements.Clear();
        }

        void RestoreMarkedEditorElements(VisualElement element)
        {
            if (element.ClassListContains(HiddenEditorClass))
            {
                element.style.display = StyleKeyword.Null;
                element.RemoveFromClassList(HiddenEditorClass);
            }

            foreach (var child in element.Children())
                RestoreMarkedEditorElements(child);
        }
    }
}
#endif
