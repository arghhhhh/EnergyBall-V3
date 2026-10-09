using System;
using System.Collections.Generic;
using RuntimeCurveEditor;
using UnityEngine;

namespace RuntimeColorEditor
{
    /// <summary>
    /// The Presets section of the runtime gradient editor, laid out like the editor's gradient
    /// preset library: a "Presets" header with a settings (⋮) menu, then a grid (or list) of
    /// 8:1 gradient swatches ending in a "New" button that adds the current gradient.
    ///
    /// Click a preset to apply it, Alt+click to delete it, right-click for Replace / Delete /
    /// Rename / Move To First. The settings menu switches Grid / List view and the library.
    /// Libraries are stored by <see cref="RuntimeGradientPresetStorage"/>.
    /// </summary>
    public class RuntimeGradientPresets
    {
        // Editor metrics: 14px tall previews at an 8:1 aspect, 5px grid margins, 2px row gap
        private static float S => RuntimeColorGUI.UIScale;
        private static float PreviewHeight => 14f * S;
        private static float PreviewWidth => PreviewHeight * 8f;
        private static float GridMargin => 5f * S;
        private static float ListMargin => 10f * S;
        private static float VerticalSpacing => 2f * S;
        private static float MinHorizontalSpacing => 1f * S;
        public static float TopAreaHeight => 20f * S;
        private static float ScrollbarWidth => 16f * S;
        private static float MaxListHeight => 110f * S;

        private const int PreviewTextureWidth = 128;

        private string libraryName;
        private GradientPresetLibrary library;
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private Vector2 scroll;
        private int hoverIndex = -1;

        private readonly RuntimeContextMenu menu = new RuntimeContextMenu();

        private int renameIndex = -1;
        private string renameText = "";
        private bool renameFocusSet;
        private Rect renameRect;

        private static GUIStyle s_NewLabel;
        private static GUIStyle s_ListLabel;
        private static GUIStyle s_RenameField;
        private static GUIStyle s_HdrLabel;
        private static float s_StyleScale;

        public RuntimeGradientPresets()
        {
            Reload();
        }

        public bool IsPopupOpen => menu.IsOpen || renameIndex >= 0;

        /// <summary>True while a menu opened from the presets covers this point (it can extend past the window).</summary>
        public bool ContainsPoint(Vector2 point) => menu.ContainsPoint(point);

        /// <summary>Closes an open menu or cancels a rename. Returns false when neither was open.</summary>
        public bool CancelPopup()
        {
            if (menu.IsOpen)
            {
                menu.Close();
                return true;
            }
            if (renameIndex >= 0)
            {
                renameIndex = -1;
                return true;
            }
            return false;
        }

        /// <summary>Re-reads the current library from disk (another window may have changed it).</summary>
        public void Reload()
        {
            libraryName = RuntimeGradientPresetStorage.CurrentLibraryName;
            library = RuntimeGradientPresetStorage.Load(libraryName);
            menu.Close();
            renameIndex = -1;
            InvalidateTextures();
        }

        public void Cleanup()
        {
            menu.Close();
            renameIndex = -1;
            InvalidateTextures();
        }

        // ---- Layout ----

        private static bool ListView => RuntimeGradientPresetStorage.ListView;

        /// <summary>Height of the whole section (header + preset area) at this width.</summary>
        public float CalcHeight(float width)
        {
            float content = ContentHeight(width, library.presets.Count + 1);
            return TopAreaHeight + Mathf.Min(content, MaxListHeight);
        }

        private float ContentHeight(float width, int itemCount)
        {
            GetGridMetrics(
                width,
                out int columns,
                out _,
                out Vector2 itemSize,
                out RectOffsetF margins
            );
            int rows = Mathf.CeilToInt(itemCount / (float)columns);
            return margins.top
                + rows * itemSize.y
                + Mathf.Max(0, rows - 1) * VerticalSpacing
                + margins.bottom;
        }

        private struct RectOffsetF
        {
            public float left,
                right,
                top,
                bottom;
        }

        private static void GetGridMetrics(
            float width,
            out int columns,
            out float horizontalSpacing,
            out Vector2 itemSize,
            out RectOffsetF margins
        )
        {
            if (ListView)
            {
                margins = new RectOffsetF
                {
                    left = ListMargin,
                    right = ListMargin,
                    top = GridMargin,
                    bottom = GridMargin,
                };
                columns = 1;
                horizontalSpacing = 0f;
                itemSize = new Vector2(width - margins.left - margins.right, PreviewHeight);
                return;
            }

            margins = new RectOffsetF
            {
                left = GridMargin,
                right = GridMargin,
                top = GridMargin,
                bottom = GridMargin,
            };
            itemSize = new Vector2(PreviewWidth, PreviewHeight);
            float available = width - margins.left - margins.right;
            columns = Mathf.Max(
                1,
                Mathf.FloorToInt(
                    (available + MinHorizontalSpacing) / (itemSize.x + MinHorizontalSpacing)
                )
            );
            // Spread the columns over the width, as the editor's grid does
            horizontalSpacing =
                columns > 1 ? (available - columns * itemSize.x) / (columns - 1) : 0f;
        }

        private static Rect ItemRect(
            int index,
            int columns,
            float horizontalSpacing,
            Vector2 itemSize,
            RectOffsetF margins
        )
        {
            int row = index / columns;
            int column = index % columns;
            return new Rect(
                margins.left + column * (itemSize.x + horizontalSpacing),
                margins.top + row * (itemSize.y + VerticalSpacing),
                itemSize.x,
                itemSize.y
            );
        }

        // ---- GUI ----

        /// <param name="rect">The section (header + presets), in window coordinates.</param>
        /// <param name="current">The gradient being edited ("New" and "Replace" use it).</param>
        /// <param name="currentPreview">Its preview texture, shown on the "New" button.</param>
        /// <param name="onPresetClicked">Called with the clicked preset's gradient.</param>
        public void OnGUI(
            Rect rect,
            Gradient current,
            Texture2D currentPreview,
            Action<Gradient> onPresetClicked
        )
        {
            EnsureStyles();
            Event e = Event.current;

            // While a menu or rename is open it owns all input
            if (IsPopupOpen && e.type != EventType.Repaint && e.type != EventType.Layout)
            {
                HandlePopups(e);
                return;
            }

            Rect topArea = new Rect(rect.x, rect.y, rect.width, TopAreaHeight);
            Rect listArea = new Rect(rect.x, topArea.yMax, rect.width, rect.height - TopAreaHeight);

            DrawTopArea(topArea);
            DrawListArea(listArea, current, currentPreview, onPresetClicked);

            if (e.type == EventType.Repaint)
                HandlePopups(e);
        }

        private void HandlePopups(Event e)
        {
            if (renameIndex >= 0)
                DrawRenameField(e);
            if (menu.IsOpen)
                menu.OnGUI();

            // Swallow leftover mouse input so nothing underneath reacts
            if (IsPopupOpen && e.isMouse)
                e.Use();
        }

        private void DrawTopArea(Rect rect)
        {
            // Two-tone separator above the section, like the editor's gradient picker
            RuntimeColorGUI.DrawRect(
                new Rect(rect.x, rect.y - 1f, rect.width, 1f),
                new Color(0f, 0f, 0f, 0.3f)
            );
            RuntimeColorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, 1f),
                new Color(1f, 1f, 1f, 0.1f)
            );

            GUI.Label(
                new Rect(rect.x + 10f * S, rect.y, rect.width - 20f * S, rect.height),
                "Presets",
                RuntimeColorGUI.Label
            );

            // Settings button: three vertical dots
            Rect button = new Rect(rect.xMax - 24f * S - 4f * S, rect.y, 24f * S, rect.height);
            if (Event.current.type == EventType.Repaint)
            {
                bool hovered = button.Contains(Event.current.mousePosition);
                Color dot = hovered ? Color.white : new Color(0.75f, 0.75f, 0.75f, 1f);
                float size = Mathf.Max(2f, Mathf.Round(2f * S));
                float cx = Mathf.Round(button.center.x - size / 2f);
                float cy = button.center.y;
                for (int i = -1; i <= 1; i++)
                    RuntimeColorGUI.DrawRect(
                        new Rect(cx, Mathf.Round(cy + i * size * 2f - size / 2f), size, size),
                        dot
                    );
            }
            if (GUI.Button(button, GUIContent.none, GUIStyle.none))
                ShowSettingsMenu(new Vector2(button.x, button.yMax));
        }

        private void DrawListArea(
            Rect area,
            Gradient current,
            Texture2D currentPreview,
            Action<Gradient> onPresetClicked
        )
        {
            Event e = Event.current;
            int itemCount = library.presets.Count + 1; // + "New"

            float contentHeight = ContentHeight(area.width, itemCount);
            bool scrolling = contentHeight > area.height;
            float layoutWidth = scrolling ? area.width - ScrollbarWidth : area.width;
            contentHeight = ContentHeight(layoutWidth, itemCount);
            GetGridMetrics(
                layoutWidth,
                out int columns,
                out float hSpacing,
                out Vector2 itemSize,
                out RectOffsetF margins
            );

            // Thin lines above and below the scroll area (editor: alwaysShowScrollAreaHorizontalLines)
            RuntimeColorGUI.DrawRect(
                new Rect(area.x, area.y, area.width, 1f),
                new Color(0f, 0f, 0f, 0.3f)
            );

            scroll = GUI.BeginScrollView(
                area,
                scroll,
                new Rect(0f, 0f, layoutWidth, contentHeight),
                false,
                false
            );
            if (e.type == EventType.Repaint)
                hoverIndex = -1;

            for (int i = 0; i < itemCount; i++)
            {
                Rect itemRect = ItemRect(i, columns, hSpacing, itemSize, margins);
                // Skip items scrolled out of view
                if (itemRect.yMax < scroll.y || itemRect.y > scroll.y + area.height)
                    continue;

                Rect previewRect = itemRect;
                Rect labelRect = itemRect;
                if (ListView)
                {
                    previewRect.width = PreviewWidth;
                    labelRect.x += previewRect.width + 8f * S;
                    labelRect.width -= previewRect.width + 10f * S;
                }

                if (i == library.presets.Count)
                {
                    DrawNewButton(
                        previewRect,
                        current,
                        currentPreview,
                        area.position - scroll,
                        labelRect
                    );
                    continue;
                }

                var preset = library.presets[i];
                bool hovered = itemRect.Contains(e.mousePosition);
                switch (e.type)
                {
                    case EventType.Repaint:
                        DrawGradientWithBackground(previewRect, PresetTexture(i), preset.gradient);
                        if (ListView && renameIndex != i)
                            GUI.Label(labelRect, preset.name, s_ListLabel);
                        if (hovered)
                            hoverIndex = i;
                        break;

                    case EventType.MouseDown:
                        if (!hovered)
                            break;
                        if (e.button == 0 && e.alt)
                        {
                            DeletePreset(i);
                            e.Use();
                            GUIUtility.ExitGUI();
                        }
                        else if (e.button == 0)
                        {
                            onPresetClicked?.Invoke(preset.gradient);
                            e.Use();
                        }
                        else if (e.button == 1)
                        {
                            Rect screenLabel = ToWindow(
                                ListView ? labelRect : itemRect,
                                area.position - scroll
                            );
                            ShowPresetMenu(
                                i,
                                area.position - scroll + e.mousePosition,
                                current,
                                screenLabel
                            );
                            e.Use();
                        }
                        break;
                }
            }
            GUI.EndScrollView();

            RuntimeColorGUI.DrawRect(
                new Rect(area.x, area.yMax - 1f, area.width, 1f),
                new Color(0f, 0f, 0f, 0.3f)
            );

            // Name tooltip in grid view (the list view shows names already)
            if (e.type == EventType.Repaint && !ListView && hoverIndex >= 0 && !IsPopupOpen)
            {
                string name = library.presets[hoverIndex].name;
                if (!string.IsNullOrEmpty(name))
                {
                    Rect item = ToWindow(
                        ItemRect(hoverIndex, columns, hSpacing, itemSize, margins),
                        area.position - scroll
                    );
                    DrawTooltip(item, name);
                }
            }
        }

        private static Rect ToWindow(Rect local, Vector2 offset) =>
            new Rect(local.x + offset.x, local.y + offset.y, local.width, local.height);

        private void DrawNewButton(
            Rect rect,
            Gradient current,
            Texture2D currentPreview,
            Vector2 offset,
            Rect labelRect
        )
        {
            if (GUI.Button(rect, GUIContent.none, RuntimeColorGUI.Button))
            {
                library.presets.Add(
                    new GradientPreset { name = "", gradient = ColorSettingsUtility.Clone(current) }
                );
                SaveLibrary();
                // The list view names a new preset right away, like the editor
                if (ListView)
                    BeginRename(library.presets.Count - 1, ToWindow(labelRect, offset));
                GUIUtility.ExitGUI();
            }

            if (Event.current.type != EventType.Repaint)
                return;
            Rect inner = new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f);
            DrawGradientWithBackground(inner, currentPreview, current, drawFrame: false);
            if (rect.width > 30f)
                LabelWithOutline(rect, "New", new Color(0.1f, 0.1f, 0.1f), s_NewLabel);
        }

        /// <summary>
        /// The editor's preset/strip drawing: checkerboard, the gradient preview texture, a box
        /// frame and a centred grey "HDR" label when a key is brighter than 1.
        /// </summary>
        private static void DrawGradientWithBackground(
            Rect rect,
            Texture2D texture,
            Gradient gradient,
            bool drawFrame = true
        )
        {
            Rect inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            RuntimeColorGUI.DrawChecker(inner);
            if (texture != null)
                GUI.DrawTexture(inner, texture, ScaleMode.StretchToFill, true);
            if (drawFrame)
                RuntimeColorGUI.DrawBorder(rect, RuntimeColorGUI.ControlBorder);
            if (RuntimeGradientEditorWindow.MaxColorComponent(gradient) > 1f)
                GUI.Label(
                    new Rect(rect.x, rect.y, rect.width - 3f, rect.height),
                    "HDR",
                    s_HdrLabel
                );
        }

        private static void LabelWithOutline(Rect rect, string text, Color outline, GUIStyle style)
        {
            Color previous = GUI.color;
            GUI.color = outline;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x != 0 || y != 0)
                    GUI.Label(
                        new Rect(rect.x + x, rect.y + y, rect.width, rect.height),
                        text,
                        style
                    );
            }
            GUI.color = previous;
            GUI.Label(rect, text, style);
        }

        private static void DrawTooltip(Rect item, string text)
        {
            var size = RuntimeColorGUI.MiniLabel.CalcSize(new GUIContent(text));
            float width = size.x + 8f * S;
            float height = 16f * S;
            Rect tip = new Rect(
                item.center.x - width / 2f,
                item.y - height - 2f * S,
                width,
                height
            );
            RuntimeColorGUI.DrawRect(tip, new Color(0.1f, 0.1f, 0.1f, 0.95f));
            RuntimeColorGUI.DrawBorder(tip, new Color(0.4f, 0.4f, 0.4f, 0.8f));
            GUI.Label(tip, text, s_ListLabel);
        }

        // ---- Menus ----

        private void ShowSettingsMenu(Vector2 position)
        {
            menu.Clear();
            menu.AddItem("Grid", !ListView, () => SetListView(false));
            menu.AddItem("List", ListView, () => SetListView(true));
            menu.AddSeparator();
            foreach (string name in RuntimeGradientPresetStorage.ListLibraries())
            {
                string library = name;
                menu.AddItem(library, library == libraryName, () => SwitchLibrary(library));
            }
            menu.Show(position);
        }

        private void ShowPresetMenu(int index, Vector2 position, Gradient current, Rect labelRect)
        {
            menu.Clear();
            menu.AddItem("Replace", false, () => ReplacePreset(index, current));
            menu.AddItem("Delete", false, () => DeletePreset(index));
            menu.AddItem("Rename", false, () => BeginRename(index, labelRect));
            menu.AddItem("Move To First", false, () => MoveToFirst(index));
            menu.Show(position);
        }

        private void SetListView(bool listView)
        {
            RuntimeGradientPresetStorage.ListView = listView;
            scroll = Vector2.zero;
        }

        private void SwitchLibrary(string name)
        {
            RuntimeGradientPresetStorage.CurrentLibraryName = name;
            scroll = Vector2.zero;
            Reload();
        }

        // ---- Library edits ----

        private void ReplacePreset(int index, Gradient current)
        {
            if (index < 0 || index >= library.presets.Count)
                return;
            library.presets[index].gradient = ColorSettingsUtility.Clone(current);
            SaveLibrary();
        }

        private void DeletePreset(int index)
        {
            if (index < 0 || index >= library.presets.Count)
                return;
            library.presets.RemoveAt(index);
            SaveLibrary();
        }

        private void MoveToFirst(int index)
        {
            if (index <= 0 || index >= library.presets.Count)
                return;
            var preset = library.presets[index];
            library.presets.RemoveAt(index);
            library.presets.Insert(0, preset);
            SaveLibrary();
        }

        private void SaveLibrary()
        {
            RuntimeGradientPresetStorage.Save(libraryName, library);
            InvalidateTextures();
        }

        // ---- Rename ----

        private void BeginRename(int index, Rect rect)
        {
            if (index < 0 || index >= library.presets.Count)
                return;
            renameIndex = index;
            renameText = library.presets[index].name;
            renameFocusSet = false;
            // The grid has no label: rename over the swatch, a little wider
            renameRect = ListView
                ? rect
                : new Rect(rect.x, rect.y - 2f * S, Mathf.Max(rect.width, 140f * S), 18f * S);
            renameRect.height = Mathf.Max(renameRect.height, 18f * S);
        }

        private void DrawRenameField(Event e)
        {
            // Enter commits; Escape is routed through CancelPopup by the window
            if (
                e.type == EventType.KeyDown
                && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            )
            {
                CommitRename();
                e.Use();
                return;
            }
            // Clicking elsewhere commits, as in the editor
            if (e.type == EventType.MouseDown && !renameRect.Contains(e.mousePosition))
            {
                CommitRename();
                e.Use();
                return;
            }

            GUI.SetNextControlName("RuntimeGradientPresets.Rename");
            renameText = GUI.TextField(renameRect, renameText, s_RenameField);
            if (!renameFocusSet)
            {
                GUI.FocusControl("RuntimeGradientPresets.Rename");
                renameFocusSet = true;
            }
        }

        private void CommitRename()
        {
            if (renameIndex >= 0 && renameIndex < library.presets.Count)
            {
                library.presets[renameIndex].name = renameText.Trim();
                RuntimeGradientPresetStorage.Save(libraryName, library);
            }
            renameIndex = -1;
            GUI.FocusControl(null);
        }

        // ---- Textures ----

        private Texture2D PresetTexture(int index)
        {
            while (textures.Count <= index)
                textures.Add(null);
            if (textures[index] == null)
            {
                var tex = RuntimeColorGUI.CreateTexture(
                    PreviewTextureWidth,
                    RuntimeGradientEditorWindow.PreviewRows
                );
                tex.SetPixels(
                    RuntimeGradientEditorWindow.RenderGradientPixels(
                        library.presets[index].gradient,
                        PreviewTextureWidth
                    )
                );
                tex.Apply();
                textures[index] = tex;
            }
            return textures[index];
        }

        private void InvalidateTextures()
        {
            for (int i = 0; i < textures.Count; i++)
            {
                var tex = textures[i];
                RuntimeColorGUI.DestroyTexture(ref tex);
            }
            textures.Clear();
        }

        // ---- Styles ----

        private static void EnsureStyles()
        {
            if (s_NewLabel != null && Mathf.Approximately(s_StyleScale, S))
                return;
            s_StyleScale = S;

            s_NewLabel = new GUIStyle(RuntimeColorGUI.Label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(11 * S),
            };
            s_NewLabel.normal.textColor = Color.white;

            s_ListLabel = new GUIStyle(RuntimeColorGUI.Label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.RoundToInt(11 * S),
                padding = new RectOffset(Mathf.RoundToInt(3 * S), Mathf.RoundToInt(3 * S), 0, 0),
            };

            s_RenameField = new GUIStyle(RuntimeColorGUI.TextField);

            s_HdrLabel = new GUIStyle(RuntimeColorGUI.MiniCenteredLabel)
            {
                fontStyle = FontStyle.Normal,
                wordWrap = false,
            };
            s_HdrLabel.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        }
    }
}
