#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Reflection;
using System.Linq;
using Type = System.Type;
using static VFavorites.Libs.VUtils;
using static VFavorites.Libs.VGUI;
using static VFavorites.VFavoritesData;


namespace VFavorites
{
    public class VFavoritesWindow : EditorWindow
    {
        [MenuItem("Window/vFavorites Panel", false)]
        public static void ShowWindow()
        {
            var window = GetWindow<VFavoritesWindow>();
            window.titleContent = new GUIContent("vFavorites", EditorGUIUtility.IconContent("d_Favorite Icon").image);
        }


        void OnEnable()
        {
            loadData();
            // 允许窗口收缩到只剩navbar高度
            minSize = new Vector2(100, 1);
            
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }
        
        /// <summary>
        /// Runs every editor frame regardless of window focus.
        /// Handles: 1) hover detection via screen coordinates (no focus needed)
        ///          2) driving collapse/expand repaint when OnGUI isn't firing
        /// </summary>
        void OnEditorUpdate()
        {
            if (!isFloatingWindow) return;
            
            #if UNITY_EDITOR_WIN
            // --- Windows: screen-coordinate hover detection (works without focus) ---
            var mouseScreen = GetMouseScreenPosition();
            if (mouseScreen != Vector2.zero)
            {
                var windowRect = position;
                // when enabled, use the original expanded height for detection; otherwise only the visible window
                var detectHeight = useExpandedAreaDetection && savedExpandedHeight > windowRect.height 
                    ? savedExpandedHeight 
                    : windowRect.height;
                
                // safety padding: mouse must move this far outside the area before triggering collapse
                var padding = 30f;
                var hoverRect = new Rect(
                    windowRect.x - padding, 
                    windowRect.y - padding, 
                    windowRect.width + padding * 2, 
                    detectHeight + padding * 2);
                
                var isHovering = hoverRect.Contains(mouseScreen);
                var wasInWindow = mouseInWindow;
                
                if (isHovering && !mouseInWindow)
                {
                    mouseInWindow = true;
                    mouseLeaveTime = double.MaxValue;
                    
                    Repaint();
                }
                else if (!isHovering && mouseInWindow)
                {
                    mouseInWindow = false;
                    mouseLeaveTime = EditorApplication.timeSinceStartup;
                    Repaint();
                }
            }
            #endif
            
            // --- drive repaint during collapse animation or delay ---
            // OnGUI won't fire if we don't have focus, so we must Repaint from here
            if (contentExpandRatio > 0f && contentExpandRatio < 1f)
            {
                Repaint();
            }
            else if (!mouseInWindow && !shouldCollapse && contentExpandRatio > 0f)
            {
                Repaint();
            }
        }
        
        Vector2 GetMouseScreenPosition()
        {
            #if UNITY_EDITOR_WIN
            return GetCursorPosWindows();
            #else
            return Vector2.zero;
            #endif
        }
        
        #if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);
        
        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(System.IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern System.IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        
        struct POINT { public int X; public int Y; }
        
        Vector2 GetCursorPosWindows()
        {
            GetCursorPos(out POINT p);
            float scale = GetDpiScale(p);
            return new Vector2(p.X / scale, p.Y / scale);
        }
        
        float GetDpiScale(POINT p)
        {
            try
            {
                const uint MONITOR_DEFAULTTONEAREST = 2;
                var monitor = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
                GetDpiForMonitor(monitor, 0, out uint dpiX, out uint _);
                return dpiX / 96f;
            }
            catch
            {
                return 1f;
            }
        }
        #endif
        

        void OnGUI()
        {
            createData();

            if (!data) return;

            var navbarHeight = 24f;

            // --- floating window mouse tracking ---
            void updateFloatingWindowState()
            {
                if (!isFloatingWindow) return;
                
                var wasInWindow = mouseInWindow;
                
                #if !UNITY_EDITOR_WIN
                // macOS/Linux: use OnGUI events to detect mouse enter/leave on the visible window
                // (tab bar + navbar area, since that's always visible even when collapsed)
                if (curEvent.type == EventType.MouseLeaveWindow)
                {
                    mouseInWindow = false;
                }
                else if (curEvent.type == EventType.MouseEnterWindow
                    || curEvent.type == EventType.MouseMove
                    || curEvent.type == EventType.MouseDown
                    || curEvent.type == EventType.MouseDrag)
                {
                    mouseInWindow = true;
                }
                #endif
                
                // drag operations always count as "in window" (both platforms)
                if (draggingItem || curEvent.type == EventType.DragUpdated)
                    mouseInWindow = true;
                
                #if !UNITY_EDITOR_WIN
                // macOS: handle enter/leave timing here since OnEditorUpdate doesn't do it
                if (wasInWindow && !mouseInWindow)
                {
                    mouseLeaveTime = EditorApplication.timeSinceStartup;
                    Debug.Log($"[vFav] OnGUI: mouse left (macOS). expandRatio={contentExpandRatio:F2}");
                }
                if (!wasInWindow && mouseInWindow)
                {
                    mouseLeaveTime = double.MaxValue;
                    Debug.Log($"[vFav] OnGUI: mouse entered (macOS). expandRatio={contentExpandRatio:F2}");
                }
                if (wasInWindow != mouseInWindow)
                    Repaint();
                #endif
            }
            updateFloatingWindowState();

            // --- floating window resize ---
            void updateFloatingWindowSize()
            {
                if (!isFloatingWindow) 
                {
                    contentExpandRatio = 1f;
                    savedExpandedHeight = -1f;
                    return;
                }
    
                // save expanded height ONLY when fully expanded and stable (ratio == 1)
                // this prevents overwriting with mid-animation values
                if (contentExpandRatio >= 1f && position.height > navbarHeight + 20)
                    savedExpandedHeight = position.height;
    
                // initialize saved height if not yet set (first time or after reset)
                if (savedExpandedHeight < navbarHeight + 20 && contentExpandRatio >= 1f)
                    savedExpandedHeight = position.height;
                    
                // if we still don't have a valid saved height, skip resizing
                if (savedExpandedHeight < navbarHeight + 20)
                    return;
    
                // calculate target height
                var targetHeight = Mathf.Lerp(navbarHeight, savedExpandedHeight, contentExpandRatio);
    
                // only modify if height difference is significant
                if (Mathf.Abs(position.height - targetHeight) > 0.5f)
                {
                    // cache position values before modification
                    var cachedX = position.x;
                    var cachedY = position.y;
                    var cachedWidth = position.width;
        
                    position = new Rect(cachedX, cachedY, cachedWidth, targetHeight);
                }
            }
            updateFloatingWindowSize();

            // cache window dimensions at start of frame to avoid inconsistency between events
            var windowWidth = position.width;
            var windowHeight = position.height;
            cachedWindowWidth = windowWidth;  // also cache for use in properties
            
            // use stable rect calculations - navbar always at top with fixed height
            var navbarRect = new Rect(0, 0, windowWidth, navbarHeight);
            var groupRect = new Rect(0, navbarHeight, windowWidth, Mathf.Max(0, windowHeight - navbarHeight));


            // --- ctrl+scroll to zoom ---
            void handleZoom()
            {
                if (!curEvent.isScroll) return;

                var holdingCtrlOrCmd = curEvent.holdingCmdOrCtrl;

                if (!holdingCtrlOrCmd) return;

                var scrollDelta = curEvent.mouseDelta.y;

                if (scrollDelta == 0 && curEvent.holdingShift)
                    scrollDelta = curEvent.mouseDelta.x;

                if (scrollDelta == 0) return;

                if (isGridMode)
                {
                    // 使用当前页面的 gridScale
                    data.curPage.gridScale -= scrollDelta * 0.1f;
                    data.curPage.gridScale = Mathf.Clamp(data.curPage.gridScale, minGridScale, maxGridScale);
                }
                else
                {
                    // 使用当前页面的 rowScale
                    data.curPage.rowScale -= scrollDelta * 0.05f;
                    data.curPage.rowScale = Mathf.Clamp(data.curPage.rowScale, minRowScale, maxRowScale);
                }

                curEvent.Use();
            }


            // --- scroll ---
            void handleScroll()
            {
                if (!curEvent.isScroll) return;
                if (!groupRect.IsHovered()) return;

                var scrollDelta = curEvent.mouseDelta.y;

                if (scrollDelta == 0 && curEvent.holdingShift)
                    scrollDelta = curEvent.mouseDelta.x;

                if (scrollDelta == 0) return;

                var scrollSpeed = 20f;
                data.curPage.scrollPos += scrollDelta * scrollSpeed;
                data.curPage.scrollPos = Mathf.Max(0, data.curPage.scrollPos);

                curEvent.Use();
            }


            // --- background ---
            void background()
            {
                var color = isDarkTheme ? Greyscale(.2f) : Greyscale(.78f);
                groupRect.Draw(color);
            }


            // --- pages ---
            void pages()
            {
                void page(Rect pageRect, Page page)
                {
                    void findSelectedItem()
                    {
                        if (!curEvent.isLayout) return;

                        foreach (var item in page.items)
                            item.isSelected = false;

                        if (draggingItem) return;
                        if (mousePresesdOnItem) return;
                        if (page.lastItemDragTime_ticks > page.lastItemSelectTime_ticks) return;

                        Item lastSelectedItem = null;

                        foreach (var item in page.items)
                        {
                            if (!item.isLoadable) continue;
                            if (lastSelectedItem?.lastSelectTime_ticks > item.lastSelectTime_ticks) continue;

                            var isSelected = Selection.activeObject == item.obj;

                            if (isSelected)
                                lastSelectedItem = item;
                        }

                        if (lastSelectedItem != null)
                            lastSelectedItem.isSelected = true;
                    }

                    // ============ LIST MODE ============
                    void rowsListMode()
                    {
                        void row(float y, Item item)
                        {
                            var rowRect = pageRect.SetHeight(listRowHeight).SetY(y).SetX(0);

                            var iconOffset = 6;
                            var iconSz = 25 * Mathf.Min(1, page.rowScale);
                            var nameOffset = 3;
                            var deletedOrNotLoadedLabelOffset = 1;

                            float highlightAmount = 0f;

                            void set_highlightAmount()
                            {
                                if (animatingDroppedItem && item == droppedItem)
                                    highlightAmount = droppedItemHighlightAmount;

                                if (item.isSelected)
                                    highlightAmount = 1;

                                if (draggingItem && item == draggedItem)
                                    highlightAmount = 1;

                                if (mousePresesdOnItem && item == pressedItem)
                                    highlightAmount = 1;
                            }

                            void shadow()
                            {
                                if (item != draggedItem && item != droppedItem) return;

                                var amount = item == droppedItem ? droppedItemShadowAmount : 1;

                                if (amount.Approx(0)) return;

                                rowRect.AddWidthFromMid(30).DrawBlurred(Greyscale(0, .55f * amount), 22);
                            }
                            void rowBackground()
                            {
                                var evenColor = isDarkTheme ? Greyscale(.249f) : Greyscale(.82f);
                                var oddColor = isDarkTheme ? Greyscale(.228f) : Greyscale(.85f);
                                var highlightedColor = isDarkTheme ? Greyscale(.335f) : Greyscale(.9f);

                                var rowColor = Lerp(evenColor, oddColor, rowRect.y.PingPong(listRowHeight) / listRowHeight);

                                Lerp(ref rowColor, highlightedColor, highlightAmount);

                                rowRect.Draw(rowColor);
                            }
                            void icon()
                            {
                                var iconRect = rowRect.MoveX(iconOffset).SetWidth(iconSz).SetHeightFromMid(iconSz);

                                if (item.isSceneGameObject)
                                    iconRect = iconRect.MoveX(1).Resize(.5f);

                                DrawItemIcon(item, iconRect, highlightAmount);
                            }
                            void name()
                            {
                                var nameRect = rowRect.MoveX(iconOffset + iconSz + nameOffset).MoveY(-.5f).SetHeightFromMid(16);

                                void normal()
                                {
                                    if (isDarkTheme)
                                        if (highlightAmount == 1) return;

                                    GUI.Label(nameRect, item.name);
                                }
                                void highlighted()
                                {
                                    if (!isDarkTheme) return;
                                    if (highlightAmount != 1) return;
                                    if (!curEvent.isRepaint) return;

                                    SetGUIColor(Greyscale(.91f));

                                    GUI.skin.GetStyle("WhiteLabel").Draw(nameRect, item.name, false, false, false, false);

                                    ResetGUIColor();
                                }

                                normal();
                                highlighted();
                            }
                            void deletedOrNotLoaded()
                            {
                                var labelRect = rowRect.MoveX(iconOffset + iconSz + nameOffset + item.name.GetLabelWidth() + deletedOrNotLoadedLabelOffset).MoveY(.5f);

                                SetGUIEnabled(false);
                                SetLabelFontSize(10);

                                if (item.isDeleted)
                                    GUI.Label(labelRect, "Deleted");
                                else if (!item.isLoadable)
                                    GUI.Label(labelRect, "Not loaded");

                                ResetLabelStyle();
                                ResetGUIEnabled();
                            }
                            void crossButton()
                            {
                                if (!rowRect.IsHovered()) return;
                                if (draggingItem) return;

                                var buttonRect = rowRect.SetWidthFromRight(0).MoveX(-crossButtonOffsetFromRight).SetWidthFromMid(crossButtonSize);
                                var iconRect_ = buttonRect.SetSizeFromMid(16);

                                var normalColor = Greyscale(item.isSelected ? .48f : .4f);
                                var hoveredColor = isDarkTheme ? Greyscale(.8f) : normalColor;
                                var pressedColor = Greyscale(.6f);

                                SetGUIColor(buttonRect.IsHovered() ? (mousePressed ? pressedColor : hoveredColor) : normalColor);
                                GUI.Label(iconRect_, EditorGUIUtility.IconContent("CrossIcon"));
                                ResetGUIColor();

                                buttonRect.MarkInteractive();


                                if (!mousePressedOnCrossButtonArea) return;
                                if (!curEvent.isMouseUp) return;
                                if (!buttonRect.IsHovered()) return;

                                CancelRowAnimations();
                                data.curPage.rowGaps[data.curPage.items.IndexOf(item)] = listRowHeight;

                                data.curPage.items.Remove(item);

                                CleanupEmptyPage();

                                data.Dirty();
                                data.Save();

                                curEvent.Use();
                            }
                            void click()
                            {
                                if (!rowRect.IsHovered()) return;
                                if (!curEvent.isMouseUp) return;

                                curEvent.Use();

                                if (draggingItem) return;
                                if (mouseDragDistance > 2) return;
                                if (!item.isLoadable) return;

                                SelectItem(item);
                            }
                            void doubleclick()
                            {
                                if (!rowRect.IsHovered()) return;
                                if (!doubleclickUnhandled) return;

                                OpenItem(item);

                                doubleclickUnhandled = false;
                            }


                            rowRect.MarkInteractive();

                            set_highlightAmount();

                            shadow();
                            rowBackground();
                            icon();
                            name();
                            deletedOrNotLoaded();
                            crossButton();
                            click();
                            doubleclick();
                        }

                        void normalRow(int i)
                        {
                            Space(page.rowGaps[i]);
                            Space(listRowHeight);

                            if (page.items[i] == droppedItem && animatingDroppedItem && page == data.curPage) return;

                            row(lastRect.y, page.items[i]);
                        }
                        void draggedRow()
                        {
                            if (!draggingItem) return;
                            if (page != data.curPage) return;

                            row(draggedItemY_rowsSpace, draggedItem);
                        }
                        void droppedRow()
                        {
                            if (!animatingDroppedItem) return;
                            if (page != data.curPage) return;

                            row(droppedItemY_rowsSpace, droppedItem);
                        }


                        GUILayout.BeginArea(pageRect);
                        page.scrollPos = EditorGUILayout.BeginScrollView(new Vector2(0, page.scrollPos), GUIStyle.none, GUIStyle.none).y;

                        for (int i = 0; i < page.items.Count; i++)
                            normalRow(i);

                        Space(page.rowGaps.Last());

                        Space(60);

                        draggedRow();
                        droppedRow();

                        EditorGUILayout.EndScrollView();
                        GUILayout.EndArea();
                    }

                    // ============ GRID MODE ============
                    void rowsGridMode()
                    {
                        
                        
                        var cellSize = gridCellSize;
                        var iconSz = cellSize - gridLabelHeight - gridPadding * 2;
                        var cols = Mathf.Max(1, Mathf.FloorToInt(pageRect.width / cellSize));

                        void gridCell(Rect cellRect, Item item)
                        {
                            float highlightAmount = 0f;

                            void set_highlightAmount()
                            {
                                if (animatingDroppedItem && item == droppedItem)
                                    highlightAmount = droppedItemHighlightAmount;

                                if (item.isSelected)
                                    highlightAmount = 1;

                                if (draggingItem && item == draggedItem)
                                    highlightAmount = 1;

                                if (mousePresesdOnItem && item == pressedItem)
                                    highlightAmount = 1;
                            }

                            void cellBackground()
                            {
                                if (highlightAmount <= 0) return;

                                var highlightedColor = isDarkTheme ? Greyscale(.335f) : Greyscale(.9f);
                                var bgColor = isDarkTheme ? Greyscale(.2f) : Greyscale(.78f);

                                Lerp(ref bgColor, highlightedColor, highlightAmount);

                                cellRect.Resize(2).DrawWithRoundedCorners(bgColor, 4);
                            }
                            void icon()
                            {
                                var iconRect = cellRect.SetHeight(iconSz).MoveY(gridPadding).SetWidthFromMid(iconSz);

                                DrawItemIcon(item, iconRect, highlightAmount);
                            }
                            void name()
                            {
                                var nameRect = cellRect.SetHeightFromBottom(gridLabelHeight).MoveY(-2);

                                var style = new GUIStyle(GUI.skin.label);
                                style.fontSize = gridFontSize;
                                style.alignment = TextAnchor.UpperCenter;
                                style.wordWrap = true;
                                style.clipping = TextClipping.Clip;

                                var contentHeight = style.CalcHeight(new GUIContent(item.name), cellRect.width - 4);
                                var maxLines = 3;
                                var lineHeight = style.lineHeight > 0 ? style.lineHeight : (gridFontSize + 2);
                                var maxHeight = lineHeight * maxLines;
                                var wrapHeight = Mathf.Min(contentHeight, maxHeight);

                                var wrapRect = new Rect(cellRect.x + 2, nameRect.y, cellRect.width - 4, wrapHeight);

                                if (isDarkTheme && highlightAmount == 1)
                                {
                                    if (curEvent.isRepaint)
                                    {
                                        SetGUIColor(Greyscale(.91f));
                                        var whiteStyle = new GUIStyle(GUI.skin.GetStyle("WhiteLabel"));
                                        whiteStyle.fontSize = gridFontSize;
                                        whiteStyle.alignment = TextAnchor.UpperCenter;
                                        whiteStyle.wordWrap = true;
                                        whiteStyle.clipping = TextClipping.Clip;
                                        whiteStyle.Draw(wrapRect, item.name, false, false, false, false);
                                        ResetGUIColor();
                                    }
                                }
                                else
                                {
                                    GUI.Label(wrapRect, item.name, style);
                                }
                            }
                            void crossButton()
                            {
                                if (!cellRect.IsHovered()) return;
                                if (draggingItem) return;

                                var btnSize = 16;
                                var buttonRect = cellRect.SetWidthFromRight(btnSize).SetHeight(btnSize).MoveX(-2).MoveY(2);

                                var normalColor = Greyscale(item.isSelected ? .48f : .4f);
                                var hoveredColor = isDarkTheme ? Greyscale(.8f) : normalColor;
                                var pressedColor = Greyscale(.6f);

                                SetGUIColor(buttonRect.IsHovered() ? (mousePressed ? pressedColor : hoveredColor) : normalColor);
                                GUI.Label(buttonRect, EditorGUIUtility.IconContent("CrossIcon"));
                                ResetGUIColor();

                                buttonRect.MarkInteractive();

                                if (!curEvent.isMouseUp) return;
                                if (!buttonRect.IsHovered()) return;

                                data.curPage.items.Remove(item);

                                CleanupEmptyPage();

                                data.Dirty();
                                data.Save();

                                curEvent.Use();
                            }
                            void click()
                            {
                                if (!cellRect.IsHovered()) return;
                                if (!curEvent.isMouseUp) return;

                                curEvent.Use();

                                if (draggingItem) return;
                                if (mouseDragDistance > 2) return;
                                if (!item.isLoadable) return;

                                SelectItem(item);
                            }
                            void doubleclick()
                            {
                                if (!cellRect.IsHovered()) return;
                                if (!doubleclickUnhandled) return;

                                OpenItem(item);

                                doubleclickUnhandled = false;
                            }

                            cellRect.MarkInteractive();

                            set_highlightAmount();
                            cellBackground();
                            icon();
                            name();
                            crossButton();
                            click();
                            doubleclick();
                        }


                        GUILayout.BeginArea(pageRect);
                        page.scrollPos = EditorGUILayout.BeginScrollView(new Vector2(0, page.scrollPos), GUIStyle.none, GUIStyle.none).y;

                        var itemCount = page.items.Count;
                        var insertIdx = draggingItem ? insertDraggedItemAtIndex_grid : -1;
                        
                        // calculate row count considering insertion gap
                        var totalSlots = itemCount + (draggingItem ? 1 : 0);
                        var rowCount = totalSlots > 0 ? Mathf.CeilToInt((float)totalSlots / cols) : 0;

                        // reserve total grid height
                        if (rowCount > 0)
                            Space(rowCount * cellSize);

                        Space(60);

                        // draw cells with insertion gap
                        for (int i = 0; i < page.items.Count; i++)
                        {
                            var item = page.items[i];
                            if (item == draggedItem && draggingItem) continue;

                            // calculate display position, shifting items after insert point
                            var displayIdx = i;
                            if (draggingItem && insertIdx >= 0 && i >= insertIdx)
                                displayIdx = i + 1;

                            var r = displayIdx / cols;
                            var c = displayIdx % cols;
                            var cellRect = new Rect(c * cellSize, r * cellSize, cellSize, cellSize);

                            gridCell(cellRect, item);
                        }

                        // draw insertion indicator
                        if (draggingItem && insertIdx >= 0)
                        {
                            var ir = insertIdx / cols;
                            var ic = insertIdx % cols;
                            var indicatorRect = new Rect(ic * cellSize, ir * cellSize, cellSize, cellSize);
                            var indicatorColor = isDarkTheme ? Greyscale(.4f, .5f) : Greyscale(.7f, .5f);
                            indicatorRect.Resize(4).DrawWithRoundedCorners(indicatorColor, 4);
                        }

                        // dragged item follows mouse
                        if (draggingItem && draggedItem != null)
                        {
                            var relativeX = mousePosition.x - cellSize / 2;
                            var relativeY = mousePosition.y - navbarHeightCached - cellSize / 2;
                            var dragRect = new Rect(relativeX, relativeY, cellSize, cellSize);
    
                            gridCell(dragRect, draggedItem);
                        }

                        EditorGUILayout.EndScrollView();
                        GUILayout.EndArea();
                    }

                    void curtains()
                    {
                        var height = 25;
                        var color = isDarkTheme ? Greyscale(.2f) : Greyscale(.78f);

                        pageRect.SetHeight(height).DrawCurtainDown(color.SetAlpha((page.scrollPos / 20).Smoothstep()));
                        pageRect.SetHeightFromBottom(height).DrawCurtainUp(color);
                    }
                    void tutor()
                    {
                        if (page.items.Any() || draggingItem) return;

                        SetGUIEnabled(false);
                        SetLabelFontSize(11);
                        SetLabelAlignmentCenter();

                        GUI.Label(pageRect.MoveY(-13), "Drop folders, assets");
                        GUI.Label(pageRect.MoveY(5), "or GameObjects");

                        ResetGUIEnabled();
                        ResetLabelStyle();
                    }


                    findSelectedItem();
                    
                    if (isGridMode)
                        rowsGridMode();
                    else
                        rowsListMode();

                    curtains();
                    tutor();
                }

                page(groupRect, data.curPage);
            }


            // --- widget (moved to navbar) ---
            void widget() { }


            // --- keys ---
            void keys()
            {
                void prevPage()
                {
                    if (!curEvent.isKeyDown) return;
                    if (curEvent.keyCode != KeyCode.LeftArrow) return;
                    if (!VFavoritesMenu.arrowKeysEnabled) return;
                    if (data.curPageIndex == 0) return;

                    CancelDragging();
                    CancelRowAnimations();

                    data.curPageIndex--;
                    EnsureTabVisible(data.curPageIndex);

                    prevPageButtonBrightness = 2;

                    curEvent.Use();
                }
                void nextPage()
                {
                    if (curEvent.keyCode != KeyCode.RightArrow) return;
                    if (!curEvent.isKeyDown) return;
                    if (!VFavoritesMenu.arrowKeysEnabled) return;
                    if (data.curPageIndex >= pageCount - 1) return;

                    CancelDragging();
                    CancelRowAnimations();

                    data.curPageIndex++;
                    EnsureTabVisible(data.curPageIndex);

                    nextPageButtonBrightness = 2;

                    curEvent.Use();
                }
                void selectPrev()
                {
                    if (!curEvent.isKeyDown) return;
                    if (curEvent.keyCode != KeyCode.UpArrow) return;
                    if (!VFavoritesMenu.arrowKeysEnabled) return;

                    var iToSelect = data.curPage.items.IndexOfFirst(r => r.isSelected) - 1;

                    if (iToSelect < 0)
                        iToSelect = data.curPage.items.LastIndex();

                    if (iToSelect.IsInRangeOf(data.curPage.items))
                        SelectItem(data.curPage.items[iToSelect]);

                    curEvent.Use();
                }
                void selectNext()
                {
                    if (!curEvent.isKeyDown) return;
                    if (curEvent.keyCode != KeyCode.DownArrow) return;
                    if (!VFavoritesMenu.arrowKeysEnabled) return;

                    var iToSelect = data.curPage.items.IndexOfFirst(r => r.isSelected) + 1;

                    if (iToSelect >= data.curPage.items.Count)
                        iToSelect = 0;

                    if (iToSelect.IsInRangeOf(data.curPage.items))
                        SelectItem(data.curPage.items[iToSelect]);

                    curEvent.Use();
                }
                void numberKeys()
                {
                    if (!curEvent.isKeyDown) return;
                    if (!VFavoritesMenu.numberKeysEnabled) return;
                    if (EditorGUIUtility.editingTextField) return;


                    var i = ((int)curEvent.keyCode - 48);

                    if (i == 0) i = 10;

                    if (!i.IsInRange(1, 10)) return;
                    if (i - 1 >= pageCount) return;


                    data.curPageIndex = i - 1;
                    EnsureTabVisible(data.curPageIndex);

                    curEvent.Use();
                }

                prevPage();
                nextPage();
                selectPrev();
                selectNext();
                numberKeys();
            }


            // --- mouse ---
            void updateMouseState()
            {
                void setDefaultState()
                {
                    mousePressed = false;
                    mousePressedOnCrossButtonArea = false;
                    mousePressedOnWidget = false;
                    pressedItem = null;
                    doubleclickUnhandled = false;
                }

                void pos()
                {
                    mousePosition = curEvent.mousePosition;
                }
                
                // always update position so drag preview keeps tracking the mouse
                pos();
                
                if (!groupRect.IsHovered()) 
                { 
                    // don't reset state if we're actively dragging — 
                    // the mouse may briefly leave groupRect during drag
                    if (!draggingItem)
                        setDefaultState(); 
                    return; 
                }

                void down()
                {
                    if (!curEvent.isMouseDown) return;

                    mousePressed = true;
                    mousePressedOnCrossButtonArea = !isGridMode && groupRect.SetWidthFromRight(0).MoveX(-crossButtonOffsetFromRight).SetWidthFromMid(crossButtonSize).IsHovered();
                    mousePressedOnWidget = curEvent.mousePosition.y < navbarRect.yMax;

                    mouseDownPosition = curEvent.mousePosition;

                    if (!mousePressedOnCrossButtonArea && !mousePressedOnWidget)
                    {
                        if (isGridMode)
                        {
                            var idx = GetGridIndexAtPosition(mouseDownPosition_rows, windowWidth);
                            if (idx >= 0 && idx < data.curPage.items.Count)
                                pressedItem = data.curPage.items[idx];
                        }
                        else
                        {
                            var pressedItemIndex = (mouseDownPosition_rows.y / listRowHeight).FloorToInt();
                            if (pressedItemIndex.IsInRangeOf(data.curPage.items))
                                pressedItem = data.curPage.items[pressedItemIndex];
                        }
                    }

                    doubleclickUnhandled = !mousePressedOnCrossButtonArea && curEvent.clickCount == 2;

                    curEvent.Use();
                }
                void up()
                {
                    if (!curEvent.isMouseUp) return;

                    mousePressed = false;
                    doubleclickUnhandled = false;
                    pressedItem = null;
                }

                down();
                up();
            }


            // --- dragging ---
            void updateDragging()
            {
                void initFromOutside()
                {
                    if (draggingItem) return;
                    if (!groupRect.IsHovered()) return;
                    if (!curEvent.isDragUpdate) return;
                    if (!DragAndDrop.objectReferences.FirstOrDefault()) return;
                    if (draggingItemFromPageToOutside) return;


                    animatingDroppedItem = false;

                    draggingItem = true;
                    draggingItemFromPage = false;

                    draggedItem = new Item(DragAndDrop.objectReferences.FirstOrDefault());
                    draggedItemHoldOffset = 0;

                    data.curPage.lastItemDragTime_ticks = System.DateTime.UtcNow.Ticks;
                }
                void initFromPage()
                {
                    if (draggingItem) return;
                    if (!groupRect.IsHovered()) return;
                    if (!curEvent.isMouseDrag) return;
                    if (mouseDragDistance < 2) return;

                    int i;
                    if (isGridMode)
                        i = GetGridIndexAtPosition(mouseDownPosition_rows, windowWidth);
                    else
                        i = (mouseDownPosition_rows.y / listRowHeight).FloorToInt();

                    if (i >= data.curPage.items.Count) return;
                    if (i < 0) return;


                    animatingDroppedItem = false;

                    draggingItem = true;
                    draggingItemFromPage = true;
                    draggingItemFromPageAtIndex = i;

                    draggedItem = data.curPage.items[i];

                    if (isGridMode)
                        draggedItemHoldOffset = 0;
                    else
                        draggedItemHoldOffset = (i * listRowHeight + listRowHeight / 2) - mouseDownPosition_rows.y;

                    data.curPage.lastItemDragTime_ticks = System.DateTime.UtcNow.Ticks;

                    data.curPage.items.Remove(draggedItem);

                    if (!isGridMode)
                        data.curPage.rowGaps[draggingItemFromPageAtIndex] = listRowHeight;

                    data.Dirty();
                    data.Save();
                }

                void acceptFromOutside()
                {
                    if (!draggingItem) return;
                    if (!curEvent.isDragPerform) return;

                    DragAndDrop.AcceptDrag();
                    curEvent.Use();

                    AcceptDragging(windowWidth);
                }
                void acceptFromPage()
                {
                    if (!draggingItem) return;
                    if (!curEvent.isMouseUp) return;

                    curEvent.Use();

                    AcceptDragging(windowWidth);
                }

                void cancelFromOutside()
                {
                    if (draggingItemFromPage) return;
                    if (!draggingItem) return;
                    if (groupRect.IsHovered()) return;

                    CancelDragging();
                }
                void cancelFromPageAndInitToOutside()
                {
                    if (!curEvent.isMouseDrag) return;
                    if (!draggingItemFromPage) return;
                    if (groupRect.IsHovered()) return;
                    if (DragAndDrop.objectReferences.Any()) return;


                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new[] { draggedItem.obj };
                    DragAndDrop.StartDrag(draggedItem.name);

                    CancelDragging();

                    draggingItemFromPageToOutside = true;
                }

                void setVisualMode()
                {
                    if (!curEvent.isDragUpdate) return;
                    
                    // must set visualMode on EVERY DragUpdated event,
                    // otherwise Unity reverts to None and stops sending drag events
                    DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
                }
                void setHotControl()
                {
                    // CRITICAL: GetControlID must be called every layout pass unconditionally
                    // to keep IMGUI's internal ID counter consistent.
                    // Conditionally skipping it shifts all subsequent control IDs,
                    // corrupting event dispatch and causing the drag preview to freeze.
                    var controlId = EditorGUIUtility.GetControlID(FocusType.Passive);
                    
                    if (!draggingItem) return;

                    if (dragHotControlId == 0)
                        dragHotControlId = controlId;
                    
                    EditorGUIUtility.hotControl = dragHotControlId;
                }


                initFromOutside();
                initFromPage();

                acceptFromOutside();
                acceptFromPage();

                cancelFromOutside();
                cancelFromPageAndInitToOutside();

                setVisualMode();
                setHotControl();

                EditorApplication.delayCall -= ResetDraggingItemFromPageToOutside;
                EditorApplication.delayCall += ResetDraggingItemFromPageToOutside;
            }


            // --- navbar ---
            void navbar()
            {
                var bgColor = Greyscale(isDarkTheme ? .235f : .8f);
                var lineColor = Greyscale(isDarkTheme ? .13f : .58f);
                navbarRect.Draw(bgColor);
                navbarRect.SetHeightFromBottom(1).Draw(lineColor);

                var chevronSize = 14;
                var colorNormal = Greyscale(isDarkTheme ? .75f : .35f);
                var colorHovered = Greyscale(isDarkTheme ? 1f : .15f);
                var colorPressed = Greyscale(isDarkTheme ? .55f : .5f);
                var colorDisabled = Greyscale(isDarkTheme ? .42f : .6f);
                var curX = 4f;
                var btnSize = 20f;

                void leftArrow() {
                    if (renamingPage) return;
                    var r = new Rect(curX, navbarRect.y + (navbarRect.height - btnSize) / 2, btnSize, btnSize);
                    var active = data.curPageIndex > 0;
                    SetGUIColor(!active ? colorDisabled : (!r.IsHovered() ? colorNormal : (mousePressed ? colorPressed : colorHovered)));
                    GUI.DrawTexture(r.SetSizeFromMid(chevronSize, chevronSize), EditorGUIUtility.IconContent("NodeChevronLeft@2x").image);
                    ResetGUIColor(); r.MarkInteractive();
                    if (curEvent.isMouseUp && r.IsHovered() && active) { CancelDragging(); CancelRowAnimations(); data.curPageIndex--; EnsureTabVisible(data.curPageIndex); prevPageButtonBrightness = 2; curEvent.Use(); }
                    curX = r.xMax;
                }
                void rightArrow() {
                    if (renamingPage) return;
                    var r = new Rect(curX, navbarRect.y + (navbarRect.height - btnSize) / 2, btnSize, btnSize);
                    var active = data.curPageIndex < pageCount - 1;
                    SetGUIColor(!active ? colorDisabled : (!r.IsHovered() ? colorNormal : (mousePressed ? colorPressed : colorHovered)));
                    GUI.DrawTexture(r.SetSizeFromMid(chevronSize, chevronSize), EditorGUIUtility.IconContent("NodeChevronRight@2x").image);
                    ResetGUIColor(); r.MarkInteractive();
                    if (curEvent.isMouseUp && r.IsHovered() && active) { CancelDragging(); CancelRowAnimations(); data.curPageIndex++; EnsureTabVisible(data.curPageIndex); nextPageButtonBrightness = 2; curEvent.Use(); }
                    curX = r.xMax + 2;
                }
                void plusButton() {
                    if (renamingPage) return;
                    var r = new Rect(curX, navbarRect.y + (navbarRect.height - btnSize) / 2, btnSize, btnSize);
                    SetGUIColor(!r.IsHovered() ? colorNormal : (mousePressed ? colorPressed : colorHovered));
                    SetLabelAlignmentCenter(); SetLabelFontSize(14); SetLabelBold();
                    GUI.Label(r, "+"); ResetGUIColor(); ResetLabelStyle(); r.MarkInteractive();
                    if (curEvent.isMouseUp && r.IsHovered()) {
                        var ei = -1; for (int i = 0; i < data.pages.Count; i++) if (data.pages[i].items.Count == 0) { ei = i; break; }
                        if (ei >= 0) data.curPageIndex = ei;
                        else
                        {
                            data.pages.Add(new Page("Page " + (data.pages.Count + 1)));
                            data.curPageIndex = data.pages.Count - 1;
                        }
                        CancelDragging(); CancelRowAnimations(); EnsureTabVisible(data.curPageIndex); data.Dirty(); data.Save(); curEvent.Use();
                    }
                    curX = r.xMax + 4;
                }
                void dividerLine() {
                    if (pageCount <= 0) return;
                    new Rect(curX, navbarRect.y + (navbarRect.height - 14) / 2, 1, 14).Draw(Greyscale(isDarkTheme ? .33f : .64f));
                    curX += 5;
                }
                void scrollableTabs() {
                    if (renamingPage) return;
                    var tabH = 18f; var aS = curX; var aE = navbarRect.xMax - 52;
                    navbarTabsAreaStartX = aS; navbarTabsAreaEndX = aE;
                    var aW = aE - aS; if (aW <= 0) return;
                    var tW = pageCount * (navbarTabBtnWidth + navbarTabSpacing) - navbarTabSpacing;
                    var mS = Mathf.Max(0, tW - aW);
                    navbarTabsScrollOffset = Mathf.Clamp(navbarTabsScrollOffset, 0, mS);
                    var aR = new Rect(aS, navbarRect.y, aW, navbarRect.height);
                    if (curEvent.isScroll && aR.IsHovered() && tW > aW) {
                        var d = curEvent.mouseDelta.y; if (d == 0) d = curEvent.mouseDelta.x;
                        navbarTabsScrollOffset = Mathf.Clamp(navbarTabsScrollOffset + d * 20f, 0, mS); curEvent.Use();
                    }
                    GUI.BeginClip(aR);
                    for (int i = 0; i < pageCount; i++) {
                        var tx = i * (navbarTabBtnWidth + navbarTabSpacing) - navbarTabsScrollOffset;
                        var tr = new Rect(tx, (navbarRect.height - tabH) / 2, navbarTabBtnWidth, tabH);
                        if (tr.xMax < 0 || tr.x > aW) continue;
                        var ic = i == data.curPageIndex;
                        tr.DrawWithRoundedCorners(ic ? Greyscale(isDarkTheme ? .35f : .95f) : (tr.IsHovered() ? Greyscale(isDarkTheme ? .3f : .88f) : Greyscale(isDarkTheme ? .26f : .83f)), 4);
                        var tn = data.pages[i].name; if (tn.IsNullOrEmpty()) tn = "Page " + (i + 1);
                        var ts = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, fontStyle = ic ? FontStyle.Bold : FontStyle.Normal, padding = new RectOffset(5, 5, 0, 0) };
                        var mw = navbarTabBtnWidth - 10; var dn = tn;
                        if (ts.CalcSize(new GUIContent(dn)).x > mw) { while (dn.Length > 1 && ts.CalcSize(new GUIContent(dn + "…")).x > mw) dn = dn.Substring(0, dn.Length - 1); dn += "…"; }
                        GUI.Label(tr, dn, ts); tr.MarkInteractive();
                        if (curEvent.isMouseUp && tr.IsHovered() && !ic) { data.curPageIndex = i; CancelDragging(); CancelRowAnimations(); curEvent.Use(); }
                    }
                    GUI.EndClip();
                }
                void renameField() {
                    if (!renamingPage) return;
                    var fr = new Rect(curX, navbarRect.y + (navbarRect.height - 18) / 2, navbarRect.xMax - curX - 54, 18);
                    var s = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleLeft, fontSize = 11 };
                    GUI.SetNextControlName("vFavNavRename"); EditorGUI.FocusTextInControl("vFavNavRename");
                    data.curPage.name = EditorGUI.TextField(fr, data.curPage.name, s);
                    if (data.curPage.name.IsNullOrEmpty()) data.curPage.name = "Page " + (data.curPageIndex + 1);
                    if (curEvent.isKeyDown && curEvent.keyCode == KeyCode.Return) { renamingPage = false; data.Dirty(); data.Save(); }
                    if (curEvent.isKeyDown && curEvent.keyCode == KeyCode.Escape) { data.curPage.name = prevPageName; renamingPage = false; }
                }
                void modeToggle() {
                    if (renamingPage) return;
                    var mr = new Rect(navbarRect.xMax - 48, navbarRect.y + (navbarRect.height - btnSize) / 2, btnSize, btnSize);
                    var iconName = isGridMode ? "d_GridLayoutGroup Icon" : "d_HorizontalLayoutGroup Icon";
                    SetGUIColor(!mr.IsHovered() ? colorNormal : (mousePressed ? colorPressed : colorHovered));
                    GUI.DrawTexture(mr.SetSizeFromMid(14, 14), EditorGUIUtility.IconContent(iconName).image);
                    ResetGUIColor(); mr.MarkInteractive();
                    if (curEvent.isMouseUp && mr.IsHovered()) { isGridMode = !isGridMode; curEvent.Use(); }
                }
                void renameButton() {
                    if (renamingPage) {
                        var ar = new Rect(navbarRect.xMax - 24, navbarRect.y + (navbarRect.height - btnSize) / 2, btnSize, btnSize);
                        SetGUIColor(!ar.IsHovered() ? colorNormal : (mousePressed ? colorPressed : colorHovered));
                        GUI.DrawTexture(ar.SetSizeFromMid(14, 14), EditorGUIUtility.IconContent("check").image);
                        ResetGUIColor(); ar.MarkInteractive();
                        if (curEvent.isMouseUp && ar.IsHovered()) { renamingPage = false; data.Dirty(); data.Save(); curEvent.Use(); }
                        return;
                    }
                    var er = new Rect(navbarRect.xMax - 24, navbarRect.y + (navbarRect.height - btnSize) / 2, btnSize, btnSize);
                    SetGUIColor(!er.IsHovered() ? colorNormal : (mousePressed ? colorPressed : colorHovered));
                    GUI.DrawTexture(er.SetSizeFromMid(12, 12), EditorGUIUtility.IconContent("editicon.sml").image);
                    ResetGUIColor(); er.MarkInteractive();
                    if (curEvent.isMouseUp && er.IsHovered()) { renamingPage = true; prevPageName = data.curPage.name; curEvent.Use(); }
                }

                void navbarContextMenu() {
                    if (!curEvent.isMouseDown || curEvent.mouseButton != 1) return;
                    if (!navbarRect.IsHovered()) return;
                    
                    var menu = new UnityEditor.GenericMenu();
                    menu.AddItem(
                        new GUIContent("Use expanded area for hover detection"), 
                        useExpandedAreaDetection, 
                        () => { useExpandedAreaDetection = !useExpandedAreaDetection; Repaint(); });
                    menu.ShowAsContext();
                    curEvent.Use();
                }

                leftArrow(); rightArrow(); plusButton(); dividerLine(); scrollableTabs(); renameField(); modeToggle(); renameButton(); navbarContextMenu();
            }



            // --- main flow ---
            updateMouseState();
            updateDragging();
            handleZoom();
            handleScroll();
            keys();

            background();
            navbar();
            pages();
            widget();

            if (curEvent.isMouseUp || curEvent.isMouseDrag || curEvent.isScroll)
                if (groupRect.IsHovered())
                    curEvent.Use();


            // --- animations ---
            if (animatingDroppedItem || animatingRowGaps)
                Repaint();
            
            // keep repainting during active drag so preview follows mouse
            if (draggingItem)
                Repaint();

            // reset per-frame preview flag before drawing; DrawItemIcon will set it if any preview is still loading
            anyPreviewLoading = false;

            // trim extra empty pages auto-created by data.curPage getter
            if (curEvent.isLayout)
                TrimExtraPages();
        }


        void Update()
        {
            updateAnimations();
        }


        // =============================================
        //  SHARED HELPERS
        // =============================================

        VFavoritesData data => VFavorites.data;


        void loadData()
        {
            if (VFavorites.data) return;

            VFavorites.data = AssetDatabase.LoadAssetAtPath<VFavoritesData>(EditorPrefsCached.GetString("vFavorites-lastKnownDataPath-" + GetProjectId()));

            if (VFavorites.data) return;

            VFavorites.data = AssetDatabase.FindAssets("t:VFavoritesData").Select(guid => AssetDatabase.LoadAssetAtPath<VFavoritesData>(guid.ToPath())).FirstOrDefault();
        }

        void createData()
        {
            if (data) return;

            loadData();

            if (data) return;

            VFavorites.data = ScriptableObject.CreateInstance<VFavoritesData>();

            AssetDatabase.CreateAsset(VFavorites.data, GetScriptPath("VFavorites").GetParentPath().CombinePath("vFavorites Data.asset"));
        }


        // --- draw icon (shared between list and grid) ---
        void DrawItemIcon(Item item, Rect iconRect, float highlightAmount)
        {
            if (item.isSceneGameObject)
                iconRect = iconRect.MoveX(1).Resize(.5f);

            void asset()
            {
                if (!item.isAsset) return;

                Texture2D iconTexture;
                if (item.isLoadable)
                {
                    var preview = AssetPreview.GetAssetPreview(item.obj);
                    if (preview != null)
                    {
                        iconTexture = preview;
                    }
                    else
                    {
                        iconTexture = AssetPreview.GetMiniThumbnail(item.obj);
                        if (AssetPreview.IsLoadingAssetPreview(item.obj.GetInstanceID()))
                            anyPreviewLoading = true;
                    }
                }
                else
                {
                    iconTexture = AssetPreview.GetMiniTypeThumbnail(item.type);
                }

                GUI.DrawTexture(iconRect, iconTexture);

            }
            void sceneGameObject()
            {
                if (!item.isSceneGameObject) return;

                void getIconNameFromAssetPreview()
                {
                    if (!item.isLoadable) return;

                    item.sceneGameObjectIconName = AssetPreview.GetMiniThumbnail(item.obj).name;

                }
                void getIconNameFromVHierarchy()
                {
                    if (!item.isLoadable) return;
                    if (!(item.obj is GameObject gameObject)) return;
                    if (VFavorites.mi_VHierarchy_GetIconName == null) return;

                    var iconNameFromVHierarchy = (string)VFavorites.mi_VHierarchy_GetIconName.Invoke(null, new object[] { gameObject });

                    if (!iconNameFromVHierarchy.IsNullOrEmpty())
                        item.sceneGameObjectIconName = iconNameFromVHierarchy;

                }

                getIconNameFromAssetPreview();
                getIconNameFromVHierarchy();

                var iconTexture = EditorGUIUtility.IconContent(item.sceneGameObjectIconName.IsNullOrEmpty() ? "GameObject icon" : item.sceneGameObjectIconName).image;

                GUI.DrawTexture(iconRect, iconTexture);

            }
            void folder()
            {
                if (!item.isFolder) return;

                iconRect = iconRect.Resize(-1.5f);

                void drawNormal()
                {
                    if (isDarkTheme)
                        if (highlightAmount == 1) return;

                    GUI.DrawTexture(iconRect, EditorGUIUtility.IconContent("Folder icon").image);

                }
                void drawHighlighted()
                {
                    if (!isDarkTheme) return;
                    if (highlightAmount != 1) return;

                    SetGUIColor(Greyscale(.84f));

                    GUI.DrawTexture(iconRect, EditorGUIUtility.IconContent("Folder On icon").image);

                    ResetGUIColor();

                }
                void drawViaVFolders()
                {
                    VFavorites.mi_VFolders_DrawBigFolderIcon?.Invoke(null, new object[] { iconRect, item.globalId.guid });
                }

                drawNormal();
                drawHighlighted();
                drawViaVFolders();

            }

            asset();
            sceneGameObject();
            folder();
        }


        // --- item actions ---
        void SelectItem(Item item)
        {
            Selection.activeObject = item.obj;

            item.lastSelectTime_ticks = data.curPage.lastItemSelectTime_ticks = System.DateTime.UtcNow.Ticks;
        }

        void OpenItem(Item item)
        {
            if (item.assetPath.GetExtension() == ".cs"
             || item.assetPath.GetExtension() == ".shader"
             || item.assetPath.GetExtension() == ".compute"
             || item.assetPath.GetExtension() == ".cginc"
             || item.assetPath.GetExtension() == ".json")
            {
                AssetDatabase.OpenAsset(item.globalId.guid.LoadGuid());
                return;
            }

            if (item.type == typeof(GameObject) && item.isLoadable && (item.obj as GameObject).scene.rootCount == 0)
            {
                AssetDatabase.OpenAsset(item.obj);
                return;
            }

            if (item.type == typeof(SceneAsset))
            {
                EditorSceneManager.SaveOpenScenes();
                EditorSceneManager.OpenScene(item.assetPath);
                return;
            }

            if (item.isSceneGameObject && !item.isLoadable && !item.isDeleted)
            {
                EditorSceneManager.SaveOpenScenes();
                EditorSceneManager.OpenScene(item.assetPath);
                Selection.activeObject = item.obj;
            }
        }


        // --- grid helpers ---
        int GetGridIndexAtPosition(Vector2 pos, float areaWidth)
        {
            var cellSize = gridCellSize;
            var cols = Mathf.Max(1, Mathf.FloorToInt(areaWidth / cellSize));

            var col = Mathf.FloorToInt(pos.x / cellSize);
            var row = Mathf.FloorToInt(pos.y / cellSize);

            if (col < 0 || col >= cols) return -1;

            return row * cols + col;
        }

        int GetGridInsertIndex(Vector2 pos, float areaWidth, int itemCount)
        {
            var cellSize = gridCellSize;
            var cols = Mathf.Max(1, Mathf.FloorToInt(areaWidth / cellSize));
    
            // 确保列索引不会超出有效范围
            var col = Mathf.Clamp(Mathf.FloorToInt(pos.x / cellSize), 0, cols - 1);
            var row = Mathf.FloorToInt(pos.y / cellSize);
    
            var idx = row * cols + col;
    
            // 更精确的边界处理
            if (idx < 0) return 0;
            if (idx > itemCount) return itemCount;
    
            return idx;
        }


        // --- drag helpers ---
        void ResetDraggingItemFromPageToOutside()
        {
            if (!DragAndDrop.objectReferences.Any())
                draggingItemFromPageToOutside = false;
        }

        void AcceptDragging(float areaWidth)
        {
            var wasFromPage = draggingItemFromPage;
            
            draggingItem = false;
            draggingItemFromPage = false;
            mousePressed = false;

            // duplicate check: skip if item already exists on this page (only for items from outside)
            if (!wasFromPage && draggedItem != null)
            {
                var newGuid = draggedItem.globalId.guid;
                var newfileId = draggedItem.globalId.fileId;
                //var newLocalId = draggedItem.globalId.localId;
                // var isDuplicate = data.curPage.items.Any(existingItem => 
                //     existingItem.globalId.guid == newGuid && existingItem.globalId.localId == newLocalId);
                var isDuplicate = data.curPage.items.Any(existingItem => 
                    existingItem.globalId.guid == newGuid && existingItem.globalId.fileId == newfileId);
                
                if (isDuplicate)
                {
                    draggedItem = null;
                    EditorGUIUtility.hotControl = 0;
                    return;
                }
            }

            int insertIdx;
            if (isGridMode)
                insertIdx = GetGridInsertIndex(mousePosition_rows, areaWidth, data.curPage.items.Count);
            else
                insertIdx = insertDraggedItemAtIndex_list;

            data.curPage.items.AddAt(draggedItem, insertIdx);

            if (!isGridMode)
            {
                data.curPage.rowGaps[insertIdx] -= listRowHeight;
                data.curPage.rowGaps.AddAt(0, insertIdx);
            }

            droppedItem = draggedItem;

            droppedItemY_rowsSpace = draggedItemY_group + data.curPage.scrollPos;
            droppedItemYDerivative = 0;
            droppedItemShadowAmount = droppedItemHighlightAmount = 1;
            animatingDroppedItem = !isGridMode; // list mode only for drop animation

            draggedItem = null;

            EditorGUIUtility.hotControl = 0;
            dragHotControlId = 0;

            data.Dirty();
            data.Save();
        }

        void CancelDragging()
        {
            if (!draggingItem) return;

            draggingItem = false;
            mousePressed = false;


            if (!draggingItemFromPage) { draggedItem = null; return; }

            data.curPage.items.AddAt(draggedItem, draggingItemFromPageAtIndex);

            if (!isGridMode)
                data.curPage.rowGaps[draggingItemFromPageAtIndex] -= listRowHeight;

            droppedItem = draggedItem;
            droppedItemY_rowsSpace = draggedItemY_group - data.curPage.scrollPos;
            droppedItemShadowAmount = droppedItemHighlightAmount = 1;
            animatingDroppedItem = !isGridMode;

            draggingItemFromPage = false;

            draggedItem = null;

            EditorGUIUtility.hotControl = 0;
            dragHotControlId = 0;

            data.Dirty();
            data.Save();
        }

        void CancelRowAnimations()
        {
            if (data == null) return;

            for (int i = 0; i < data.curPage.rowGaps.Count; i++)
                data.curPage.rowGaps[i] = 0;

            animatingDroppedItem = false;
            droppedItem = null;
        }

        // remove current page if it became empty and it's not the only page
        void CleanupEmptyPage()
        {
            if (data == null) return;
            if (data.curPage.items.Count > 0) return;
            if (pageCount <= 1) return;

            var idx = data.curPageIndex;
            data.pages.RemoveAt(idx);

            // move to previous page, or stay at 0
            data.curPageIndex = Mathf.Max(0, idx - 1);

            EnsureTabVisible(data.curPageIndex);
        }

        // trim trailing empty pages that were auto-created by data.curPage getter
        // keep at most: all pages with items + 1 empty page if it's the current page
        void TrimExtraPages()
        {
            if (data == null || data.pages == null) return;

            // remove trailing empty pages that aren't the current page
            for (int i = data.pages.Count - 1; i >= 0; i--)
            {
                if (data.pages[i].items.Count > 0) break;
                if (i == data.curPageIndex) continue;
                // don't remove if it's the only page
                if (data.pages.Count <= 1) break;

                data.pages.RemoveAt(i);

                // adjust curPageIndex if needed
                if (data.curPageIndex > i)
                    data.curPageIndex--;
            }
        }


        // --- animations ---
        void updateAnimations()
        {
            var dt = (float)(EditorApplication.timeSinceStartup - lastLayoutTime);

            if (dt > .05f)
                dt = .0166f;

            lastLayoutTime = EditorApplication.timeSinceStartup;

            if (!data) return;

            if (!isGridMode)
            {
                // row gaps (list mode only)
                var lerpSpeed = 10;
                for (int i = 0; i < data.curPage.rowGaps.Count; i++)
                    data.curPage.rowGaps[i] = Lerp(data.curPage.rowGaps[i], draggingItem && i == insertDraggedItemAtIndex_list ? listRowHeight : 0, lerpSpeed, dt);
            }

            // dropped item
            if (animatingDroppedItem)
            {
                SmoothDamp(ref droppedItemY_rowsSpace, data.curPage.items.IndexOf(droppedItem) * listRowHeight, 8, ref droppedItemYDerivative, dt);
                Lerp(ref droppedItemShadowAmount, 0, 8, dt);
                Lerp(ref droppedItemHighlightAmount, 0, 10, dt);

                if (droppedItemShadowAmount < .01f)
                    animatingDroppedItem = false;
            }

            // page buttons
            Lerp(ref prevPageButtonBrightness, 1, 7, dt);
            Lerp(ref nextPageButtonBrightness, 1, 7, dt);

            // floating window content collapse/expand
            if (isFloatingWindow)
            {
                // determine target: expand if mouse in window, collapse after delay
                var targetRatio = mouseInWindow ? 1f : (shouldCollapse ? 0f : contentExpandRatio);
                var prevRatio = contentExpandRatio;
                Lerp(ref contentExpandRatio, targetRatio, contentAnimSpeed, dt);
                
                // clamp to avoid floating point issues
                if (contentExpandRatio < 0.001f) contentExpandRatio = 0f;
                if (contentExpandRatio > 0.999f) contentExpandRatio = 1f;
                
                
                // keep repainting during animation or while waiting for delay
                if (!contentExpandRatio.Approx(prevRatio) || (!mouseInWindow && !shouldCollapse))
                    Repaint();
            }
            else
            {
                contentExpandRatio = 1f;
            }

            if (animatingDroppedItem || animatingRowGaps)
                Repaint();

            if (anyPreviewLoading)
                Repaint();
        }


        // =============================================
        //  STATE
        // =============================================

        // --- scale / mode ---
        bool isGridMode
        {
            get => data != null && data.curPage.isGridMode;
            set { if (data != null) { data.curPage.isGridMode = value; data.Dirty(); } }
        }
        const float minGridScale = 1f;
        const float maxGridScale = 4f;
        const float minRowScale = 0.5f;
        const float maxRowScale = 2f;

        // --- page count: number of pages that exist (at least 1, includes all pages up to the last one with items) ---
        int pageCount
        {
            get
            {
                if (data == null || data.pages == null || data.pages.Count == 0) return 1;

                // find last page with items
                var lastWithItems = -1;
                for (int i = 0; i < data.pages.Count; i++)
                    if (data.pages[i].items.Count > 0)
                        lastWithItems = i;

                // count = all pages with content + at most 1 empty page after them
                // also always include the current page
                var count = lastWithItems + 1;
                
                for (int i = 0; i < data.pages.Count; i++)
                    if (data.pages[i].items.Count == 0)
                    {
                        // include this empty page in count if it's beyond lastWithItems
                        count = Mathf.Max(count, i + 1);
                        break; // only 1 empty page
                    }

                // always include current page
                count = Mathf.Max(count, data.curPageIndex + 1);

                return Mathf.Max(1, count);
            }
        }

        // list mode metrics
        float listRowHeight => 44 * data.curPage.rowScale;

        // grid mode metrics
        float gridCellSize => data.curPage.gridScale * 48f;
        const float gridLabelHeight = 18f;
        const float gridPadding = 4f;
        const int gridFontSize = 10;

        float crossButtonOffsetFromRight = 23;
        float crossButtonSize = 16;

        bool renamingPage;
        string prevPageName;

        bool mousePressed;
        bool mousePresesdOnItem => pressedItem != null;
        bool mousePressedOnCrossButtonArea;
        bool mousePressedOnWidget;
        bool doubleclickUnhandled;

        Vector2 mousePosition;
        Vector2 mousePosition_rows => new Vector2(mousePosition.x, mousePosition.y - navbarHeightCached + data.curPage.scrollPos);

        Vector2 mouseDownPosition;
        Vector2 mouseDownPosition_rows => new Vector2(mouseDownPosition.x, mouseDownPosition.y - navbarHeightCached + data.curPage.scrollPos);

        float navbarHeightCached = 24f;

        float mouseDragDistance => (mousePosition - mouseDownPosition).magnitude;

        Item pressedItem;

        // drag state
        bool draggingItem;
        int dragHotControlId;
        bool draggingItemFromPage;
        bool draggingItemFromPageToOutside;
        int draggingItemFromPageAtIndex;
        Item draggedItem;
        float draggedItemHoldOffset;
        float draggedItemY_group => (mousePosition.y - navbarHeightCached - listRowHeight / 2 + draggedItemHoldOffset).Clamp(0, 12321);
        float draggedItemY_rowsSpace => draggedItemY_group + data.curPage.scrollPos;
        int insertDraggedItemAtIndex_list => ((mousePosition_rows.y + draggedItemHoldOffset) / listRowHeight).FloorToInt().Clamp(0, data.curPage.items.Count);
        int insertDraggedItemAtIndex_grid => draggingItem ? GetGridInsertIndex(mousePosition_rows, cachedWindowWidth, data.curPage.items.Count) : -1;

        // cached window dimensions (updated at start of OnGUI)
        float cachedWindowWidth;

        Item droppedItem;
        float droppedItemY_rowsSpace;
        float droppedItemYDerivative;
        float droppedItemShadowAmount;
        float droppedItemHighlightAmount;
        bool animatingDroppedItem;

        float prevPageButtonBrightness = 1;
        float nextPageButtonBrightness = 1;

        // navbar tab scroll
        float navbarTabsScrollOffset;
        float navbarTabsAreaStartX; // set during navbar draw
        float navbarTabsAreaEndX;   // set during navbar draw
        const float navbarTabBtnWidth = 64f;
        const float navbarTabSpacing = 2f;

        void EnsureTabVisible(int tabIndex)
        {
            var tabLeft = tabIndex * (navbarTabBtnWidth + navbarTabSpacing);
            var tabRight = tabLeft + navbarTabBtnWidth;
            var areaWidth = navbarTabsAreaEndX - navbarTabsAreaStartX;

            if (areaWidth <= 0) return;

            if (tabLeft < navbarTabsScrollOffset)
                navbarTabsScrollOffset = tabLeft;
            else if (tabRight > navbarTabsScrollOffset + areaWidth)
                navbarTabsScrollOffset = tabRight - areaWidth;
        }

        double lastLayoutTime;

        bool animatingRowGaps => !isGridMode && data != null && data.curPage.rowGaps.Any(r => r > .1f && r < listRowHeight - .1f);

        // asset preview async loading
        bool anyPreviewLoading;

        // floating window auto-collapse state
        bool isFloatingWindow => !docked;
        bool mouseInWindow;
        float contentExpandRatio = 1f;  // 0 = collapsed, 1 = fully expanded
        // collapse delay timer
        double mouseLeaveTime;
        bool shouldCollapse => !mouseInWindow && (EditorApplication.timeSinceStartup - mouseLeaveTime) > collapseDelay;
        // remember expanded window height
        float savedExpandedHeight = -1f;
        // when true, mouse re-entering the original expanded area triggers expand (Windows only)
        // when false, only hovering over tab bar + navbar triggers expand
        bool useExpandedAreaDetection
        {
            get => EditorPrefs.GetBool("vFavoritesWindow-useExpandedAreaDetection", true);
            set => EditorPrefs.SetBool("vFavoritesWindow-useExpandedAreaDetection", value);
        }
        
        float collapseDelay => data.collapseDelay;
        float contentAnimSpeed => data.contentAnimSpeed;
    }
}
#endif