#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using VFavorites;
using VFolders;
using VHierarchy;
using VInspector;
using VTabs;

namespace ZEditorTools
{
    public class ZEditorToolsSettingsWindow : EditorWindow
    {
        static readonly string[] TabNames = { "vFavorites", "vFolders", "vHierarchy", "vInspector", "vTabs" };

        Vector2 scrollPosition;
        int selectedTab;

        [MenuItem("Tools/EditorTools/Settings", false, 1)]
        public static void ShowWindow()
        {
            var window = GetWindow<ZEditorToolsSettingsWindow>();
            window.titleContent = new GUIContent("EditorTools Settings");
            window.minSize = new Vector2(520f, 420f);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8f);
            selectedTab = GUILayout.Toolbar(selectedTab, TabNames);
            EditorGUILayout.Space(8f);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            switch (selectedTab)
            {
                case 0:
                    DrawFavoritesSettings();
                    break;
                case 1:
                    DrawFoldersSettings();
                    break;
                case 2:
                    DrawHierarchySettings();
                    break;
                case 3:
                    DrawInspectorSettings();
                    break;
                case 4:
                    DrawTabsSettings();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        static void DrawFavoritesSettings()
        {
            DrawHeader("vFavorites", "在 Project 窗口中快速显示和管理收藏内容。");
            DrawModuleEnabled(!VFavoritesMenu.pluginDisabled, value => VFavoritesMenu.pluginDisabled = !value);

            using (new EditorGUI.DisabledScope(VFavoritesMenu.pluginDisabled))
            {
                DrawSection("显示方式");
                DrawActivationKey();
                DrawToggle("使用面板界面", "在 Project 窗口内嵌显示完整的收藏夹面板界面。", () => VFavoritesMenu.embeddedPanelUsesWindowUI, value => VFavoritesMenu.embeddedPanelUsesWindowUI = value, RepaintProject);

                DrawSection("快捷交互");
                DrawToggle("滚轮浏览收藏", "鼠标位于收藏区域时，使用滚轮滚动收藏条目。", () => VFavoritesMenu.pageScrollEnabled, value => VFavoritesMenu.pageScrollEnabled = value);
                DrawToggle("数字键切换页面", "使用 1–9 数字键快速切换收藏页面。", () => VFavoritesMenu.numberKeysEnabled, value => VFavoritesMenu.numberKeysEnabled = value);
                DrawToggle("方向键导航", "使用方向键切换收藏页面或当前选择。", () => VFavoritesMenu.arrowKeysEnabled, value => VFavoritesMenu.arrowKeysEnabled = value);

                DrawSection("动画与诊断");
                DrawToggle("淡入淡出动画", "显示或隐藏收藏面板时使用透明度过渡。", () => VFavoritesMenu.fadeAnimationsEnabled, value => VFavoritesMenu.fadeAnimationsEnabled = value, RepaintProject);
                DrawToggle("页面滚动动画", "切换收藏页面时使用平滑滚动过渡。", () => VFavoritesMenu.pageScrollAnimationEnabled, value => VFavoritesMenu.pageScrollAnimationEnabled = value, RepaintProject);
                DrawToggle("调试日志", "在 Console 输出收藏夹窗口创建和清理等诊断信息。", () => VFavoritesMenu.debugLoggingEnabled, value => VFavoritesMenu.debugLoggingEnabled = value);
            }
        }

        static void DrawFoldersSettings()
        {
            DrawHeader("vFolders", "增强 Project 窗口的文件夹导航、外观与快捷操作。");
            DrawModuleEnabled(!VFoldersMenu.pluginDisabled, value => VFoldersMenu.pluginDisabled = !value);

            using (new EditorGUI.DisabledScope(VFoldersMenu.pluginDisabled))
            {
                DrawSection("外观与导航");
                DrawToggle("导航栏", "在 Project 窗口顶部显示路径和导航控件。", () => VFoldersMenu.navigationBarEnabled, value => VFoldersMenu.navigationBarEnabled = value, RepaintProject);
                DrawToggle("两行名称", "为较长的文件夹或资源名称提供第二行显示空间。", () => VFoldersMenu.twoLineNamesEnabled, value => VFoldersMenu.twoLineNamesEnabled = value, RepaintProject);
                DrawToggle("自动图标", "根据文件夹内容自动选择具有辨识度的图标。", () => VFoldersMenu.autoIconsEnabled, value => VFoldersMenu.autoIconsEnabled = value, RepaintProject);
                DrawToggle("层级连线", "在文件夹树中绘制父子层级引导线。", () => VFoldersMenu.hierarchyLinesEnabled, value => VFoldersMenu.hierarchyLinesEnabled = value, RepaintProject);
                DrawToggle("斑马条纹", "用交替背景色提升列表行的可读性。", () => VFoldersMenu.zebraStripingEnabled, value => VFoldersMenu.zebraStripingEnabled = value, RepaintProject);
                DrawToggle("内容缩略提示", "在文件夹行旁显示其内容的简要视觉提示。", () => VFoldersMenu.contentMinimapEnabled, value => VFoldersMenu.contentMinimapEnabled = value, RepaintProject);
                DrawToggle("背景颜色", "使用调色板颜色绘制文件夹背景。", () => VFoldersMenu.backgroundColorsEnabled, value => VFoldersMenu.backgroundColorsEnabled = value, RepaintProject);
                DrawToggle("极简模式", "隐藏次要装饰，使 Project 列表更紧凑。", () => VFoldersMenu.minimalModeEnabled, value => VFoldersMenu.minimalModeEnabled = value, RepaintProject);
#if UNITY_EDITOR_OSX
                DrawToggle("文件夹优先", "排序时把文件夹放在其他资源之前。", () => VFoldersMenu.foldersFirstEnabled, value => VFoldersMenu.foldersFirstEnabled = value, RepaintProject);
#endif

                DrawSection("快捷键");
                DrawToggle("E 展开/收起", "鼠标悬停文件夹时按 E 切换其展开状态。", () => VFoldersMenu.toggleExpandedEnabled, value => VFoldersMenu.toggleExpandedEnabled = value);
                DrawToggle("Shift+E 隔离", "仅展开当前文件夹，并收起同级的其他文件夹。", () => VFoldersMenu.collapseEverythingElseEnabled, value => VFoldersMenu.collapseEverythingElseEnabled = value);
                DrawToggle("Ctrl+Shift+E 全部收起", "收起 Project 窗口中的全部文件夹层级。", () => VFoldersMenu.collapseEverythingEnabled, value => VFoldersMenu.collapseEverythingEnabled = value);
            }
        }

        static void DrawHierarchySettings()
        {
            DrawHeader("vHierarchy", "增强 Hierarchy 窗口的场景导航、状态显示与快捷操作。");
            DrawModuleEnabled(!VHierarchyMenu.pluginDisabled, value => VHierarchyMenu.pluginDisabled = !value);

            using (new EditorGUI.DisabledScope(VHierarchyMenu.pluginDisabled))
            {
                DrawSection("外观与导航");
                DrawToggle("导航栏", "在 Hierarchy 顶部显示收藏、搜索和导航控件。", () => VHierarchyMenu.navigationBarEnabled, value => VHierarchyMenu.navigationBarEnabled = value, RepaintHierarchy);
                DrawToggle("场景选择器", "在导航栏中提供已加载场景的快速切换入口。", () => VHierarchyMenu.sceneSelectorEnabled, value => VHierarchyMenu.sceneSelectorEnabled = value, RepaintHierarchy);
                DrawToggle("组件缩略栏", "在对象行右侧显示主要组件图标。", () => VHierarchyMenu.componentMinimapEnabled, value => VHierarchyMenu.componentMinimapEnabled = value, RepaintHierarchy);
                DrawToggle("激活开关", "在对象行中显示启用或停用 GameObject 的开关。", () => VHierarchyMenu.activationToggleEnabled, value => VHierarchyMenu.activationToggleEnabled = value, RepaintHierarchy);
                DrawToggle("层级连线", "绘制父子对象之间的层级引导线。", () => VHierarchyMenu.hierarchyLinesEnabled, value => VHierarchyMenu.hierarchyLinesEnabled = value, RepaintHierarchy);
                DrawToggle("斑马条纹", "用交替背景色提升 Hierarchy 行的可读性。", () => VHierarchyMenu.zebraStripingEnabled, value => VHierarchyMenu.zebraStripingEnabled = value, RepaintHierarchy);
                DrawToggle("极简模式", "隐藏次要装饰，使对象列表更紧凑。", () => VHierarchyMenu.minimalModeEnabled, value => VHierarchyMenu.minimalModeEnabled = value, RepaintHierarchy);

                DrawSection("快捷键");
                DrawToggle("D 设置默认父对象", "将悬停对象设为新建 GameObject 的默认父对象。", () => VHierarchyMenu.setDefaultParentEnabled, value => VHierarchyMenu.setDefaultParentEnabled = value);
                DrawToggle("A 切换激活", "切换悬停 GameObject 的激活状态。", () => VHierarchyMenu.toggleActiveEnabled, value => VHierarchyMenu.toggleActiveEnabled = value);
                DrawToggle("F 聚焦", "在 Scene 视图中聚焦悬停的 GameObject。", () => VHierarchyMenu.focusEnabled, value => VHierarchyMenu.focusEnabled = value);
                DrawToggle("X 删除", "删除鼠标悬停的 GameObject；启用后请谨慎使用。", () => VHierarchyMenu.deleteEnabled, value => VHierarchyMenu.deleteEnabled = value);
                DrawToggle("E 展开/收起", "切换悬停对象的子层级展开状态。", () => VHierarchyMenu.toggleExpandedEnabled, value => VHierarchyMenu.toggleExpandedEnabled = value);
                DrawToggle("Shift+E 隔离", "展开当前对象并收起同级的其他对象。", () => VHierarchyMenu.isolateEnabled, value => VHierarchyMenu.isolateEnabled = value);
                DrawToggle("Ctrl+Shift+E 全部收起", "收起 Hierarchy 中的全部对象层级。", () => VHierarchyMenu.collapseEverythingEnabled, value => VHierarchyMenu.collapseEverythingEnabled = value);
            }
        }

        static void DrawInspectorSettings()
        {
            DrawHeader("vInspector", "增强 Inspector 的组件操作、显示方式与键盘交互。");
            DrawModuleEnabled(!VInspectorMenu.pluginDisabled, value => VInspectorMenu.pluginDisabled = !value);

            using (new EditorGUI.DisabledScope(VInspectorMenu.pluginDisabled))
            {
                DrawSection("组件界面");
                DrawToggle("导航栏", "在 Inspector 顶部显示选择历史和导航控件。", () => VInspectorMenu.navigationBarEnabled, value => VInspectorMenu.navigationBarEnabled = value, RefreshInspector);
                DrawToggle("组件标签模式", "在导航栏下用标签选择要同时显示的组件；每个 GameObject 独立保留自己的激活状态。启用时会同时显示导航栏。", () => VInspectorMenu.componentTabsEnabled, value => VInspectorMenu.componentTabsEnabled = value, RefreshInspector);
                DrawToggle("复制/粘贴按钮", "在组件标题栏提供复制和粘贴组件值的按钮。", () => VInspectorMenu.copyPasteButtonsEnabled, value => VInspectorMenu.copyPasteButtonsEnabled = value, RefreshInspector);
                DrawToggle("运行时保存按钮", "在 Play Mode 中提供保存组件状态的按钮。", () => VInspectorMenu.playmodeSaveButtonEnabled, value => VInspectorMenu.playmodeSaveButtonEnabled = value, RefreshInspector);
                DrawToggle("组件浮动窗口", "允许把单个组件打开为独立浮动窗口。", () => VInspectorMenu.componentWindowsEnabled, value => VInspectorMenu.componentWindowsEnabled = value, RefreshInspector);
                DrawToggle("组件动画", "展开、收起组件时使用平滑过渡。", () => VInspectorMenu.componentAnimationsEnabled, value => VInspectorMenu.componentAnimationsEnabled = value, RefreshInspector);
                DrawToggle("极简模式", "减少组件标题栏和 Inspector 中的次要元素。", () => VInspectorMenu.minimalModeEnabled, value => VInspectorMenu.minimalModeEnabled = value, RefreshInspector);
                DrawToggle("可重置变量提示", "标记与默认值不同、可以重置的序列化字段。", () => VInspectorMenu.resettableVariablesEnabled, value => VInspectorMenu.resettableVariablesEnabled = value, RefreshInspector);
                DrawToggle("隐藏 Script 字段", "隐藏 MonoBehaviour 顶部只读的 Script 引用字段。", () => VInspectorMenu.hideScriptFieldEnabled, value => VInspectorMenu.hideScriptFieldEnabled = value, RefreshInspector);
                DrawToggle("隐藏帮助按钮", "隐藏组件标题栏中的帮助文档按钮。", () => VInspectorMenu.hideHelpButtonEnabled, value => VInspectorMenu.hideHelpButtonEnabled = value, RefreshInspector);
                DrawToggle("隐藏 Presets 按钮", "隐藏组件标题栏中的预设按钮。", () => VInspectorMenu.hidePresetsButtonEnabled, value => VInspectorMenu.hidePresetsButtonEnabled = value, RefreshInspector);

                DrawSection("快捷键");
                DrawToggle("A 切换激活", "切换鼠标悬停组件或对象的启用状态。", () => VInspectorMenu.toggleActiveEnabled, value => VInspectorMenu.toggleActiveEnabled = value);
                DrawToggle("X 删除组件", "删除鼠标悬停的组件；启用后请谨慎使用。", () => VInspectorMenu.deleteEnabled, value => VInspectorMenu.deleteEnabled = value);
                DrawToggle("E 展开/收起", "切换鼠标悬停组件的展开状态。", () => VInspectorMenu.toggleExpandedEnabled, value => VInspectorMenu.toggleExpandedEnabled = value);
                DrawToggle("Shift+E 隔离", "展开当前组件并收起其他组件。", () => VInspectorMenu.collapseEverythingElseEnabled, value => VInspectorMenu.collapseEverythingElseEnabled = value);
                DrawToggle("Ctrl+Shift+E 全部收起", "收起 Inspector 中的全部组件。", () => VInspectorMenu.collapseEverythingEnabled, value => VInspectorMenu.collapseEverythingEnabled = value);
            }
        }

        static void DrawTabsSettings()
        {
            DrawHeader("vTabs", "增强 Unity 编辑器标签页的外观、按钮和切换方式。");
            DrawModuleEnabled(!VTabsMenu.pluginDisabled, value => VTabsMenu.pluginDisabled = !value, RefreshTabs);

            using (new EditorGUI.DisabledScope(VTabsMenu.pluginDisabled))
            {
                DrawSection("外观与按钮");
                DrawToggle("新增标签按钮", "在标签栏显示创建新标签页的按钮。", () => VTabsMenu.addTabButtonEnabled, value => VTabsMenu.addTabButtonEnabled = value, RefreshTabs);
                DrawToggle("关闭标签按钮", "在标签页上显示快速关闭按钮。", () => VTabsMenu.closeTabButtonEnabled, value => VTabsMenu.closeTabButtonEnabled = value, RefreshTabs);
                DrawToggle("标签分隔线", "在相邻标签页之间绘制分隔线。", () => VTabsMenu.dividersEnabled, value => VTabsMenu.dividersEnabled = value, RefreshTabs);
                DrawToggle("隐藏锁定按钮", "隐藏 Project 和 Inspector 等窗口的锁定按钮。", () => VTabsMenu.hideLockButtonEnabled, value => VTabsMenu.hideLockButtonEnabled = value, RefreshTabs);
#if UNITY_6000_0_OR_NEWER
                DrawIntPopup("标签样式", "选择 Unity 6 标签页的尺寸和视觉样式。", () => VTabsMenu.tabStyle, value => VTabsMenu.tabStyle = value, new[] { "默认", "大尺寸", "紧凑" }, new[] { 0, 1, 2 }, RefreshTabStyle);
                DrawIntPopup("背景样式", "选择标签栏背景的视觉风格。", () => VTabsMenu.backgroundStyle, value => VTabsMenu.backgroundStyle = value, new[] { "默认", "经典", "灰色" }, new[] { 0, 1, 2 }, RefreshTabStyle);
#endif

                DrawSection("快捷键");
                DrawToggle("Shift+滚轮切换标签", "按住 Shift 滚动鼠标滚轮，在同一停靠区切换标签。", () => VTabsMenu.switchTabShortcutEnabled, value => VTabsMenu.switchTabShortcutEnabled = value);
                DrawToggle("Ctrl/Cmd+T 新增标签", "使用常见浏览器快捷键创建标签页。", () => VTabsMenu.addTabShortcutEnabled, value => VTabsMenu.addTabShortcutEnabled = value);
                DrawToggle("Ctrl/Cmd+W 关闭标签", "使用常见浏览器快捷键关闭当前标签页。", () => VTabsMenu.closeTabShortcutEnabled, value => VTabsMenu.closeTabShortcutEnabled = value);
                DrawToggle("Ctrl/Cmd+Shift+T 恢复标签", "重新打开最近关闭的标签页。", () => VTabsMenu.reopenTabShortcutEnabled, value => VTabsMenu.reopenTabShortcutEnabled = value);

#if UNITY_EDITOR_OSX
                DrawSection("触控板");
                DrawToggle("横向滚动切换标签", "使用触控板横向滚动手势切换标签页。", () => VTabsMenu.sidescrollEnabled, value => VTabsMenu.sidescrollEnabled = value);
                DrawSlider("滚动灵敏度", "调整横向滚动触发标签切换的灵敏度。", () => VTabsMenu.sidescrollSensitivity, value => VTabsMenu.sidescrollSensitivity = value, 0.2f, 3f);
#endif
            }
        }

        static void DrawActivationKey()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            var value = (VFavoritesActivationKey)EditorGUILayout.EnumPopup("收藏夹显示键", VFavoritesMenu.activationKey);
            if (EditorGUI.EndChangeCheck())
            {
                VFavoritesMenu.activationKey = value;
                RepaintProject();
            }

            var description = value == VFavoritesActivationKey.Alt
                ? "按住 Alt 显示收藏夹。此选项可能与文件夹颜色选择等 Unity Alt 交互冲突。"
                : value == VFavoritesActivationKey.Tab
                    ? "按住 Tab 显示收藏夹；松开后恢复 Project 窗口。"
                    : "按住 Space 显示收藏夹；这是默认选项，可避免占用 Unity 的 Alt 交互。";
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        static void DrawHeader(string title, string description)
        {
            EditorGUILayout.LabelField(title, EditorStyles.largeLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(4f);
        }

        static void DrawSection(string title)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        static void DrawModuleEnabled(bool value, Action<bool> setter, Action changed = null)
        {
            DrawToggle("启用此工具", "关闭后该模块停止注册编辑器增强功能；切换会触发脚本重新编译。", () => value, setter, () =>
            {
                changed?.Invoke();
                CompilationPipeline.RequestScriptCompilation();
            });
        }

        static void DrawToggle(string label, string description, Func<bool> getter, Action<bool> setter, Action changed = null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var value = getter();
            var newValue = EditorGUILayout.ToggleLeft(label, value, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            if (newValue == value) return;

            setter(newValue);
            changed?.Invoke();
        }

        static void DrawIntPopup(string label, string description, Func<int> getter, Action<int> setter, string[] labels, int[] values, Action changed = null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var value = getter();
            var newValue = EditorGUILayout.IntPopup(label, value, labels, values);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            if (newValue == value) return;

            setter(newValue);
            changed?.Invoke();
        }

        static void DrawSlider(string label, string description, Func<float> getter, Action<float> setter, float min, float max)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var value = getter();
            var newValue = EditorGUILayout.Slider(label, value, min, max);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            if (Mathf.Approximately(newValue, value)) return;

            setter(newValue);
        }

        static void RepaintProject() => EditorApplication.RepaintProjectWindow();
        static void RepaintHierarchy() => EditorApplication.RepaintHierarchyWindow();

        static void RefreshInspector()
        {
            VInspector.VInspector.UpdateHeaderButtons(null);
            VInspectorMenu.RepaintInspectors();
        }

        static void RefreshTabs() => VTabs.VTabs.RepaintAllDockAreas();

        static void RefreshTabStyle()
        {
            VTabs.VTabs.UpdateStyleSheet();
            RefreshTabs();
        }
    }
}
#endif
