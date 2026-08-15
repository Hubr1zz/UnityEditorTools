#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditor.IMGUI.Controls;
using Type = System.Type;
using static VHierarchy.Libs.VUtils;
using static VHierarchy.Libs.VGUI;
// using static VTools.VDebug;
using static VHierarchy.VHierarchyData;
using static VHierarchy.VHierarchy;



namespace VHierarchy
{
    public class VHierarchyNavbar
    {

        public void OnGUI(Rect navbarRect)
        {
            void updateState()
            {
                if (!curEvent.isLayout) return;



                var isTreeFocused = window.GetFieldValue("m_SceneHierarchy").GetMemberValue<int>("m_TreeViewKeyboardControlID") == GUIUtility.keyboardControl;

                var isWindowFocused = window == EditorWindow.focusedWindow;



                if (!isTreeFocused && isSearchActive)
                    EditorGUI.FocusTextInControl("SearchFilter");


                if (isTreeFocused || !isWindowFocused)
                    if (window.GetMemberValue("m_SearchFilter").ToString().IsNullOrEmpty())
                        isSearchActive = false;


                // in vFolders the following is used to check if search is active:
                // GUI.GetNameOfFocusedControl() == "SearchFilter";
                // but in hierarchy focused control changes erratically when multiple scene headers are visible
                // so a bool state is used instead




                this.defaultParent = typeof(SceneView).InvokeMethod<Transform>("GetDefaultParentObjectIfSet")?.gameObject;

            }

            var topBarRect = navbarRect.SetHeight(baseNavbarHeight);

            void background()
            {
                var backgroundColor = Greyscale(isDarkTheme ? .235f : .8f);
                var lineColor = Greyscale(isDarkTheme ? .13f : .58f);

                navbarRect.Draw(backgroundColor);

                topBarRect.SetHeightFromBottom(1).MoveY(1).Draw(lineColor);

            }
            void hiddenMenu()
            {
                if (!curEvent.holdingAlt) return;
                if (!curEvent.isMouseUp) return;
                if (curEvent.mouseButton != 1) return;
                if (!topBarRect.IsHovered()) return;


                void selectData()
                {
                    Selection.activeObject = data;
                }
                void selectPalette()
                {
                    Selection.activeObject = palette;
                }
                void clearCache()
                {
                    VHierarchyCache.Clear();
                }



                GenericMenu menu = new();

                menu.AddDisabledItem(new GUIContent("vHierarchy hidden menu"));

                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Select data"), false, selectData);
                menu.AddItem(new GUIContent("Select palette"), false, selectPalette);

                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Clear cache"), false, clearCache);

                menu.ShowAsContext();

            }


            void plusButton()
            {

                var buttonRect = topBarRect.SetWidth(28).MoveX(4.5f);

                if (Application.unityVersion.StartsWith("6000"))
                    buttonRect = buttonRect.MoveY(-.49f);


                var iconName = "Plus Thicker";
                var iconSize = 16;
                var colorNormal = Greyscale(isDarkTheme ? .7f : .44f);
                var colorHovered = Greyscale(isDarkTheme ? 1f : .42f);
                var colorPressed = Greyscale(isDarkTheme ? .75f : .6f);

                if (!IconButton(buttonRect, iconName, iconSize, colorNormal, colorHovered, colorPressed)) return;


                GUIUtility.hotControl = 0;

                var sceneHierarchy = window.GetMemberValue("m_SceneHierarchy");
                var m_CustomParentForNewGameObjects = window.GetMemberValue("m_SceneHierarchy").GetMemberValue<Transform>("m_CustomParentForNewGameObjects");
                var targetSceneHandle = m_CustomParentForNewGameObjects != null ? (int)m_CustomParentForNewGameObjects.gameObject.scene.handle : 0;


                var menu = new GenericMenu();

                sceneHierarchy.GetType().GetMethod("AddCreateGameObjectItemsToMenu", maxBindingFlags).Invoke(sceneHierarchy, new object[] { menu, null, true, true, false, targetSceneHandle, 3 });

                typeof(UnityEditor.SceneManagement.SceneHierarchyHooks).InvokeMethod("AddCustomItemsToCreateMenu", menu);

                menu.DropDown(buttonRect);


            }

            void searchButton()
            {
                if (searchAnimationT == 1) return;


                var buttonRect = topBarRect.SetWidthFromRight(28).MoveX(-5);

                var iconName = "Search_";
                var iconSize = 16;
                var colorNormal = Greyscale(isDarkTheme ? .75f : .2f);
                var colorHovered = Greyscale(isDarkTheme ? 1f : .2f);
                var colorPressed = Greyscale(isDarkTheme ? .75f : .5f);


                if (!IconButton(buttonRect, iconName, iconSize, colorNormal, colorHovered, colorPressed)) return;

                EditorGUI.FocusTextInControl("SearchFilter");

                EditorApplication.delayCall += () => EditorGUI.FocusTextInControl("SearchFilter");

                isSearchActive = true;

            }
            void searchOnCtrlF()
            {
                if (searchAnimationT == 1) return;

                if (!curEvent.isKeyDown) return;
                if (!curEvent.holdingCmd && !curEvent.holdingCtrl) return;
                if (curEvent.keyCode != KeyCode.F) return;


                EditorGUI.FocusTextInControl("SearchFilter");

                EditorApplication.delayCall += () => EditorGUI.FocusTextInControl("SearchFilter");

                isSearchActive = true;


                curEvent.Use();

            }
            void collapseAllButton()
            {
                if (searchAnimationT == 1) return;


                var buttonRect = topBarRect.SetWidthFromRight(28).MoveX(-33);

                var iconName = "Collapse";
                var iconSize = 16;
                var colorNormal = Greyscale(isDarkTheme ? .71f : .44f);
                var colorHovered = Greyscale(isDarkTheme ? 1f : .42f);
                var colorPressed = Greyscale(isDarkTheme ? .75f : .6f);


                if (!IconButton(buttonRect, iconName, iconSize, colorNormal, colorHovered, colorPressed)) return;

                controller.CollapseAll();

            }
            void bookmarks()
            {
                if (searchAnimationT == 1) return;
                if (isSearchActive && !curEvent.isRepaint) return;

                // Bookmark button box: fixed-size square on the left side of the navbar (after the plus button)
                var bookmarkButtonRect = topBarRect.SetWidth(bookmarkBoxWidth).SetHeightFromMid(bookmarkBoxHeight).MoveX(36);
                
                void createData()
                {
                    if (data) return;
                    if (!bookmarkButtonRect.IsHovered()) return;
                    if (!DragAndDrop.objectReferences.Any()) return;

                    data = ScriptableObject.CreateInstance<VHierarchyData>();

                    AssetDatabase.CreateAsset(data, GetScriptPath("VHierarchy").GetParentPath().CombinePath("vHierarchy Data.asset"));

                }

                

                void drawBookmarkBox()
                {
                    var isDragHovered = bookmarkButtonRect.IsHovered() && DragAndDrop.objectReferences.Any(r => r is GameObject);

                    var borderColor = isDragHovered
                        ? Greyscale(isDarkTheme ? .9f : .3f)
                        : Greyscale(isDarkTheme ? .45f : .55f);

                    var bgColor = isDragHovered
                        ? Greyscale(isDarkTheme ? .32f : .75f)
                        : Greyscale(isDarkTheme ? .27f : .82f);

                    bookmarkButtonRect.DrawRounded(borderColor, 4);
                    bookmarkButtonRect.Resize(1).DrawRounded(bgColor, 3);

                    // Star icon
                    var iconName = "Favorite";
                    var iconTex = EditorIcons.GetIcon(iconName);
                    if (iconTex)
                    {
                        var iconRect = bookmarkButtonRect.SetSizeFromMid(14, 14);
                        var iconColor = isDragHovered
                            ? new Color(1f, .85f, .2f)
                            : new Color(.85f, .7f, .2f);

                        SetGUIColor(iconColor);
                        GUI.DrawTexture(iconRect, iconTex);
                        ResetGUIColor();
                    }

                    // Accept drag-and-drop
                    if (isDragHovered && curEvent.isDragUpdate)
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (isDragHovered && curEvent.isDragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        if (data == null)
                        {
                            data = ScriptableObject.CreateInstance<VHierarchyData>();
                            AssetDatabase.CreateAsset(data, GetScriptPath("VHierarchy").GetParentPath().CombinePath("vHierarchy Data.asset"));
                        }

                        foreach (var obj in DragAndDrop.objectReferences.OfType<GameObject>())
                        {
                            if (!data.bookmarks.Any(b => b.go == obj))
                            {
                                data.RecordUndo();
                                data.bookmarks.Add(new Bookmark(obj));
                                data.Dirty();
                            }
                        }
                    }

                    // Click to open/close panel
                    if (bookmarkButtonRect.IsHovered() && curEvent.isMouseUp && curEvent.mouseButton == 0 && !DragAndDrop.objectReferences.Any())
                    {
                        bookmarkPanelOpen = !bookmarkPanelOpen;
                        curEvent.Use();
                    }

                }

                void drawBookmarkPanel()
                {
                    if (!bookmarkPanelOpen) return;
                    if (data == null || !data.bookmarks.Any(b => b.go)) return;

                    const float rowHeight  = 24f;
                    const float lerpSpeed  = 12f;

                    // ── Sync gaps list ────────────────────────────────────────────
                    var allBookmarks   = data.bookmarks.Where(b => b.go).ToList();
                    var validBookmarks = bookmarkDragging
                        ? allBookmarks.Where(b => b != bookmarkDraggedItem).ToList()
                        : allBookmarks;

                    while (bookmarkRowGaps.Count < validBookmarks.Count + 1) bookmarkRowGaps.Add(0f);
                    while (bookmarkRowGaps.Count > validBookmarks.Count + 1) bookmarkRowGaps.RemoveAt(bookmarkRowGaps.Count - 1);

                    // panelRect spans full window width, matching the navbar
                    var panelX    = navbarRect.x;
                    var panelY    = (float)baseNavbarHeight;
                    var panelRect = new Rect(panelX, panelY, navbarRect.width, navbarRect.height - baseNavbarHeight);

                    // ── Scroll metrics (computed early so drag logic can exclude scrollbar) ─
                    var gapsTotal          = bookmarkRowGaps.Sum();
                    var totalContentHeight = validBookmarks.Count * rowHeight + gapsTotal;
                    var needsScroll        = totalContentHeight > panelRect.height;
                    var scrollbarWidth     = needsScroll ? 15f : 0f;
                    var contentWidth       = panelRect.width - scrollbarWidth;

                    // contentAreaRect excludes the scrollbar region
                    var contentAreaRect = new Rect(panelRect.x, panelRect.y, contentWidth, panelRect.height);

                    // ── Click blocker ─────────────────────────────────────────────
                    panelRect.MarkInteractive();

                    // ══════════════════════════════════════════════════════════════
                    // ── Drag event handling (OUTSIDE ScrollView) ──────────────────
                    // All mouseDown/drag/up detection lives here so events are never
                    // swallowed by the ScrollView when the cursor leaves its rect.
                    // ══════════════════════════════════════════════════════════════

                    void recordPressedItem()
                    {
                        if (!curEvent.isMouseDown) return;
                        if (curEvent.mouseButton != 0) return;
                        if (!contentAreaRect.Contains(curEvent.mousePosition)) return;

                        bookmarkMouseDownPosition = curEvent.mousePosition;

                        float localY   = curEvent.mousePosition.y - panelY + bookmarkPanelScroll.y;
                        int   pressIdx = Mathf.FloorToInt(localY / rowHeight);
                        bookmarkPressedItem = (pressIdx >= 0 && pressIdx < validBookmarks.Count)
                            ? validBookmarks[pressIdx] : null;
                    }
                    void startDragging()
                    {
                        if (bookmarkDragging) return;
                        if (!curEvent.isMouseDrag) return;
                        if (bookmarkPressedItem == null) return;
                        if ((curEvent.mousePosition - bookmarkMouseDownPosition).magnitude < 4) return;

                        bookmarkDragging    = true;
                        bookmarkDraggedItem = bookmarkPressedItem;
                        bookmarkPressedItem = null;

                        int   hovIdx  = allBookmarks.IndexOf(bookmarkDraggedItem);
                        float rowTopY = panelY + hovIdx * rowHeight - bookmarkPanelScroll.y;
                        bookmarkDragHoldOffsetY = rowTopY - curEvent.mousePosition.y;
                    }
                    void updateDragging()
                    {
                        if (!bookmarkDragging) return;

                        float mouseLocalY = curEvent.mousePosition.y - panelY + bookmarkPanelScroll.y;
                        bookmarkDragInsertIndex = Mathf.Clamp(
                            Mathf.FloorToInt(mouseLocalY / rowHeight), 0, validBookmarks.Count);
                    }
                    void acceptDragging()
                    {
                        if (!bookmarkDragging) return;
                        if (!curEvent.isMouseUp) return;
                        if (!panelRect.Contains(curEvent.mousePosition)) return;

                        // Reorder: map insert index from validBookmarks back to data.bookmarks
                        data.RecordUndo();

                        int oldIndex = data.bookmarks.IndexOf(bookmarkDraggedItem);
                        data.bookmarks.RemoveAt(oldIndex);

                        var validAfterRemove = data.bookmarks.Where(b => b.go).ToList();
                        int insertAt;
                        if (bookmarkDragInsertIndex >= validAfterRemove.Count)
                            insertAt = data.bookmarks.Count;
                        else
                            insertAt = data.bookmarks.IndexOf(validAfterRemove[bookmarkDragInsertIndex]);

                        insertAt = Mathf.Clamp(insertAt, 0, data.bookmarks.Count);
                        data.bookmarks.Insert(insertAt, bookmarkDraggedItem);
                        data.Dirty();

                        bookmarkDragging        = false;
                        bookmarkPressedItem     = null;
                        bookmarkDraggedItem     = null;
                        bookmarkDragInsertIndex = 0;
                        curEvent.Use();
                    }
                    void rightClickDelete()
                    {
                        if (bookmarkDragging) return;
                        if (!curEvent.isMouseUp) return;
                        if (curEvent.mouseButton != 1) return;
                        if (!contentAreaRect.Contains(curEvent.mousePosition)) return;

                        float localY = curEvent.mousePosition.y - panelY + bookmarkPanelScroll.y;
                        int clickIdx = Mathf.FloorToInt(localY / rowHeight);
                        if (clickIdx < 0 || clickIdx >= validBookmarks.Count) return;

                        var bmToEdit = validBookmarks[clickIdx];

                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Edit note"), false, () =>
                        {
                            BookmarkNoteWindow.Show(data, bmToEdit);
                        });
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent("Remove bookmark"), false, () =>
                        {
                            data.RecordUndo();
                            data.bookmarks.Remove(bmToEdit);
                            data.Dirty();
                        });
                        menu.ShowAsContext();

                        curEvent.Use();
                    }
                    void clickToReveal()
                    {
                        if (bookmarkDragging) return;
                        if (!curEvent.isMouseUp) return;
                        if (curEvent.mouseButton != 0) return;
                        if (!contentAreaRect.Contains(curEvent.mousePosition)) return;

                        // Find which row was clicked using panel-space coordinates
                        float localY = curEvent.mousePosition.y - panelY + bookmarkPanelScroll.y;
                        int clickIdx = Mathf.FloorToInt(localY / rowHeight);
                        if (clickIdx < 0 || clickIdx >= validBookmarks.Count) return;

                        var targetGo = validBookmarks[clickIdx].go;

                        bookmarkPressedItem = null;

                        // Select the object first so Unity's tree view frames it,
                        // then use RevealObject for expand + highlight animation.
                        // The extra panel height is accounted for by adjusting the
                        // scroll target after RevealObject finishes.
                        Selection.activeGameObject = targetGo;
                        EditorGUIUtility.PingObject(targetGo);

                        // Also call RevealObject for the highlight animation,
                        // but with expand: false to avoid scroll miscalculation
                        controller.RevealObject(targetGo, expand: false, highlight: true, snapToTopMargin: true);

                        curEvent.Use();
                    }

                    recordPressedItem();
                    startDragging();
                    updateDragging();
                    acceptDragging();
                    rightClickDelete();
                    clickToReveal();

                    // ── Animate gaps ──────────────────────────────────────────────
                    if (curEvent.isLayout)
                    {
                        for (int gi = 0; gi < bookmarkRowGaps.Count; gi++)
                        {
                            float target = (bookmarkDragging && panelRect.Contains(curEvent.mousePosition) && gi == bookmarkDragInsertIndex)
                                ? rowHeight : 0f;
                            bookmarkRowGaps[gi] = MathUtil.Lerp(bookmarkRowGaps[gi], target, lerpSpeed, editorDeltaTime);
                            if (bookmarkRowGaps[gi] < 0.5f) bookmarkRowGaps[gi] = 0f;
                        }
                    }

                    // ══════════════════════════════════════════════════════════════
                    // ── Drawing (ScrollView only handles rendering, no events) ────
                    // ══════════════════════════════════════════════════════════════

                    // ── Draw background ───────────────────────────────────────────
                    panelRect.DrawBlurred(Greyscale(0, .35f), 12);
                    panelRect.Resize(-1).DrawRounded(Greyscale(isDarkTheme ? .13f : .6f), 5);
                    panelRect.DrawRounded(Greyscale(isDarkTheme ? .22f : .88f), 4);

                    // ── Scroll view (rendering only) ──────────────────────────────
                    var viewRect = panelRect.Resize(1);
                    var scrollContentWidth = viewRect.width - scrollbarWidth;
                    bookmarkPanelScroll = GUI.BeginScrollView(
                        viewRect,
                        bookmarkPanelScroll,
                        new Rect(0, 0, scrollContentWidth, totalContentHeight),
                        GUIStyle.none,    // 禁用水平滚动条
                        GUI.skin.verticalScrollbar);  // 保留垂直滚动条

                    float curY = 0f;
                    for (int i = 0; i < validBookmarks.Count; i++)
                    {
                        curY += bookmarkRowGaps[i];

                        var bm      = validBookmarks[i];
                        var rowRect = new Rect(0, curY, contentWidth, rowHeight);

                        // row hover highlight (skip while dragging)
                        if (rowRect.IsHovered() && !bookmarkDragging)
                            rowRect.Resize(1).DrawRounded(Greyscale(isDarkTheme ? .32f : .78f), 3);

                        // icon
                        var iconName = "";
                        if (VHierarchy.GetGameObjectData(bm.go, createDataIfDoesntExist: false) is GameObjectData goData && !goData.iconNameOrGuid.IsNullOrEmpty())
                            iconName = goData.iconNameOrGuid.Length == 32 ? goData.iconNameOrGuid.ToPath() : goData.iconNameOrGuid;
                        else
                            iconName = AssetPreview.GetMiniThumbnail(bm.go).name;
                        if (iconName.IsNullOrEmpty()) iconName = "GameObject icon";

                        var iconTex = EditorIcons.GetIcon(iconName);
                        if (iconTex)
                        {
                            SetGUIColor(Color.white);
                            GUI.DrawTexture(new Rect(5, curY + (rowHeight - 16) / 2f, 16, 16), iconTex);
                            ResetGUIColor();
                        }

                        // label (name truncated if note exists)
                        var hasNote = !bm.note.IsNullOrEmpty();
                        var nameWidth = hasNote ? Mathf.Min(bm.name.GetLabelWidth(12) + 8, (scrollContentWidth - 26) * 0.45f) : scrollContentWidth - 26;
                        var labelRect = new Rect(26, curY, nameWidth, rowHeight);
                        SetLabelFontSize(12);
                        GUI.skin.label.alignment = TextAnchor.MiddleLeft;
                        GUI.skin.label.clipping = TextClipping.Clip;
                        SetGUIColor(Greyscale(isDarkTheme ? .9f : .1f));
                        GUI.Label(labelRect, bm.name);
                        ResetGUIColor();
                        ResetLabelStyle();

                        // note (right side, dimmed)
                        if (hasNote)
                        {
                            var noteX = 26 + nameWidth + 4;
                            var noteRect = new Rect(noteX, curY, scrollContentWidth - noteX - 4, rowHeight);
                            SetLabelFontSize(11);
                            GUI.skin.label.alignment = TextAnchor.MiddleLeft;
                            GUI.skin.label.clipping = TextClipping.Clip;
                            GUI.skin.label.fontStyle = FontStyle.Italic;
                            SetGUIColor(Greyscale(isDarkTheme ? .5f : .45f));
                            GUI.Label(noteRect, bm.note);
                            ResetGUIColor();
                            ResetLabelStyle();
                        }

                        curY += rowHeight;
                    }

                    GUI.EndScrollView();

                    // ── Dragged item ghost (drawn outside ScrollView, follows mouse) ─
                    if (bookmarkDragging && bookmarkDraggedItem != null)
                    {
                        var ghostY    = curEvent.mousePosition.y + bookmarkDragHoldOffsetY;
                        var ghostRect = new Rect(panelX + 1, ghostY, contentWidth, rowHeight);

                        ghostRect.DrawBlurred(Greyscale(0, .3f), 10);
                        ghostRect.DrawRounded(Greyscale(isDarkTheme ? .38f : .68f), 3);

                        // ghost icon
                        var gIconName = "";
                        if (VHierarchy.GetGameObjectData(bookmarkDraggedItem.go, createDataIfDoesntExist: false) is GameObjectData gd && !gd.iconNameOrGuid.IsNullOrEmpty())
                            gIconName = gd.iconNameOrGuid.Length == 32 ? gd.iconNameOrGuid.ToPath() : gd.iconNameOrGuid;
                        else
                            gIconName = AssetPreview.GetMiniThumbnail(bookmarkDraggedItem.go).name;
                        if (gIconName.IsNullOrEmpty()) gIconName = "GameObject icon";

                        var gIconTex = EditorIcons.GetIcon(gIconName);
                        if (gIconTex)
                        {
                            SetGUIColor(Greyscale(1, .7f));
                            GUI.DrawTexture(new Rect(panelX + 6, ghostY + (rowHeight - 16) / 2f, 16, 16), gIconTex);
                            ResetGUIColor();
                        }

                        var gLabelRect = new Rect(panelX + 27, ghostY, contentWidth - 26, rowHeight);
                        SetLabelFontSize(12);
                        GUI.skin.label.alignment = TextAnchor.MiddleLeft;
                        SetGUIColor(Greyscale(isDarkTheme ? .7f : .4f));
                        GUI.Label(gLabelRect, bookmarkDraggedItem.name);
                        ResetGUIColor();
                        ResetLabelStyle();
                    }

                    // ── Close on outside click ────────────────────────────────────
                    if (curEvent.isMouseDown && !bookmarkDragging
                        && !panelRect.IsHovered() && !bookmarkButtonRect.IsHovered())
                        bookmarkPanelOpen = false;

                    if (bookmarkDragging || bookmarkRowGaps.Any(g => g > 0.5f))
                        window.Repaint();

                }


                this.navbarRect = navbarRect;
                this.bookmarksRect = bookmarkButtonRect;

                createData();
                drawBookmarkBox();
                drawBookmarkPanel();

            }

            void searchField()
            {
                if (searchAnimationT == 0) return;

                var searchFieldRect = topBarRect.SetHeightFromMid(20).AddWidth(-33).SetWidthFromRight(200f.Min(window.position.width - 120)).Move(-1, 2);


                GUILayout.BeginArea(searchFieldRect);
                GUILayout.BeginHorizontal();

                Space(2);
                window.InvokeMethod("SearchFieldGUI");

                GUILayout.EndHorizontal();
                GUILayout.EndArea();

            }
            void closeSearchButton()
            {
                if (searchAnimationT == 0) return;


                var buttonRect = topBarRect.SetWidthFromRight(30).MoveX(-4);

                var iconName = "CrossIcon";
                var iconSize = 15;
                var colorNormal = Greyscale(isDarkTheme ? .9f : .2f);
                var colorHovered = Greyscale(isDarkTheme ? 1f : .2f);
                var colorPressed = Greyscale(isDarkTheme ? .75f : .5f);


                if (!IconButton(buttonRect, iconName, iconSize, colorNormal, colorHovered, colorPressed)) return;

                window.InvokeMethod("ClearSearchFilter");

                GUIUtility.keyboardControl = 0;

                isSearchActive = false;

            }
            void closeSearchOnEsc()
            {
                if (!isSearchActive) return;
                if (curEvent.keyCode != KeyCode.Escape) return;

                window.InvokeMethod("ClearSearchFilter");

                GUIUtility.keyboardControl = 0;

                isSearchActive = false;

            }

            void searchAnimation()
            {
                if (!curEvent.isLayout) return;


                var lerpSpeed = 8f;

                if (isSearchActive)
                    MathUtil.SmoothDamp(ref searchAnimationT, 1, lerpSpeed, ref searchAnimationDerivative, editorDeltaTime);
                else
                    MathUtil.SmoothDamp(ref searchAnimationT, 0, lerpSpeed, ref searchAnimationDerivative, editorDeltaTime);


                if (isSearchActive && searchAnimationT > .99f)
                    searchAnimationT = 1;

                if (!isSearchActive && searchAnimationT < .01f)
                    searchAnimationT = 0;


                animatingSearch = searchAnimationT != 0 && searchAnimationT != 1;

            }

            void buttonsAndBookmarks()
            {
                SetGUIColor(Greyscale(1, (1 - searchAnimationT).Pow(2)));
                GUI.BeginGroup(window.position.SetPos(0, 0).MoveX(-searchAnimationDistance * searchAnimationT));

                searchButton();
                searchOnCtrlF();
                collapseAllButton();
                bookmarks();

                GUI.EndGroup();
                ResetGUIColor();

            }
            void search()
            {
                SetGUIColor(Greyscale(1, searchAnimationT.Pow(2)));
                GUI.BeginGroup(window.position.SetPos(0, 0).MoveX(searchAnimationDistance * (1 - searchAnimationT)));

                searchField();
                closeSearchButton();
                closeSearchOnEsc();

                GUI.EndGroup();
                ResetGUIColor();

            }



            updateState();

            background();
            hiddenMenu();

            plusButton();

            searchAnimation();
            buttonsAndBookmarks();
            search();



            if (animatingSearch || bookmarkPanelOpen || bookmarkDragging || bookmarkRowGaps.Any(g => g > 0.5f))
                window.Repaint();

        }

        bool animatingSearch;
        float searchAnimationDistance = 90;
        float searchAnimationT;
        float searchAnimationDerivative;

        string openedFolderPath;

        public bool isSearchActive;

        bool isDefaultParentTextPressed;

        GameObject defaultParent;

        GUIStyle defaultParentTextGUIStyle;

        Rect navbarRect;
        Rect bookmarksRect;

        const int baseNavbarHeight = 26;
        const int listRowHeight    = 22;
        const int maxVisibleRows   = 8;

        public int navbarHeight
        {
            get
            {
                if (!bookmarkPanelOpen) return baseNavbarHeight;
                if (data == null)       return baseNavbarHeight;

                var count = data.bookmarks.Count(b => b.go);
                if (count == 0)         return baseNavbarHeight;

                var rows = Mathf.Min(count, maxVisibleRows);
                return baseNavbarHeight + rows * listRowHeight + 2;
            }
        }

        float bookmarkBoxHeight => 22f;
        float bookmarkBoxWidth => 60f;
        
        bool    bookmarkPanelOpen;
        Vector2 bookmarkPanelScroll;

        bool     bookmarkDragging;
        Bookmark bookmarkDraggedItem;
        Bookmark bookmarkPressedItem;
        Vector2  bookmarkMouseDownPosition;
        float    bookmarkDragHoldOffsetY;
        int      bookmarkDragInsertIndex;
        List<float> bookmarkRowGaps = new();
        
        public VHierarchyNavbar(EditorWindow window) => this.window = window;

        public EditorWindow window;

        public VHierarchyController controller => VHierarchy.controllers_byWindow[window];


    }

    public class BookmarkNoteWindow : EditorWindow
    {
        static VHierarchyData targetData;
        static VHierarchyData.Bookmark targetBookmark;
        string noteText = "";
        bool focusNeeded = true;

        public static void Show(VHierarchyData data, VHierarchyData.Bookmark bookmark)
        {
            targetData = data;
            targetBookmark = bookmark;

            var win = GetWindow<BookmarkNoteWindow>(true, "Edit Note", true);
            win.noteText = bookmark.note ?? "";
            win.focusNeeded = true;
            win.minSize = new Vector2(260, 60);
            win.maxSize = new Vector2(260, 60);
            win.ShowUtility();
        }

        void OnGUI()
        {
            if (targetBookmark == null) { Close(); return; }

            GUI.SetNextControlName("NoteField");
            noteText = EditorGUI.TextField(new Rect(8, 8, position.width - 16, 18), noteText);

            if (focusNeeded)
            {
                EditorGUI.FocusTextInControl("NoteField");
                focusNeeded = false;
            }

            var btnWidth = 60f;
            var okRect     = new Rect(position.width - btnWidth * 2 - 14, 34, btnWidth, 20);
            var cancelRect = new Rect(position.width - btnWidth - 8, 34, btnWidth, 20);

            var confirmed = GUI.Button(okRect, "OK")
                         || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return);
            var cancelled = GUI.Button(cancelRect, "Cancel")
                         || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape);

            if (confirmed)
            {
                targetData.RecordUndo();
                targetBookmark.note = noteText;
                targetData.Dirty();
                Close();
            }
            if (cancelled)
            {
                Close();
            }
        }
    }
}
#endif