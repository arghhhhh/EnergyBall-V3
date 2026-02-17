using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeCurveEditor
{
    public class RuntimeContextMenu
    {
        public struct MenuItem
        {
            public string label;
            public bool isChecked;
            public bool isEnabled;
            public Action callback;
            public bool isSeparator;
            public string submenuParent; // null = top level, otherwise parent label

            public static MenuItem Separator()
            {
                return new MenuItem { isSeparator = true };
            }
        }

        private List<MenuItem> items = new List<MenuItem>();
        private bool isOpen;
        private Vector2 position;
        private string openSubmenu;
        private int windowId;

        private const float ITEM_HEIGHT = 22f;
        private const float MENU_WIDTH = 200f;
        private const float SUBMENU_WIDTH = 180f;
        private const float SEPARATOR_HEIGHT = 8f;

        private static GUIStyle s_MenuItemStyle;
        private static GUIStyle s_MenuItemCheckedStyle;
        private static GUIStyle s_MenuBgStyle;
        private static GUIStyle s_SeparatorStyle;

        public RuntimeContextMenu()
        {
            windowId = UnityEngine.Random.Range(10000, 99999);
        }

        public void AddItem(string label, bool isChecked, Action callback, string submenu = null)
        {
            items.Add(new MenuItem
            {
                label = label,
                isChecked = isChecked,
                isEnabled = true,
                callback = callback,
                submenuParent = submenu
            });
        }

        public void AddDisabledItem(string label, string submenu = null)
        {
            items.Add(new MenuItem
            {
                label = label,
                isChecked = false,
                isEnabled = false,
                submenuParent = submenu
            });
        }

        public void AddSeparator(string submenu = null)
        {
            items.Add(MenuItem.Separator());
        }

        public void Show(Vector2 pos)
        {
            position = pos;
            isOpen = true;
            openSubmenu = null;
        }

        public void Close()
        {
            isOpen = false;
            openSubmenu = null;
        }

        public bool IsOpen => isOpen;

        public Rect GetMenuRect()
        {
            float height = CalculateMenuHeight(null);
            return new Rect(position.x, position.y, MENU_WIDTH, height);
        }

        public bool OnGUI()
        {
            if (!isOpen) return false;

            EnsureStyles();

            // Draw main menu
            float mainHeight = CalculateMenuHeight(null);
            Rect mainRect = new Rect(position.x, position.y, MENU_WIDTH, mainHeight);

            // Clamp to screen
            if (mainRect.xMax > Screen.width)
                mainRect.x = Screen.width - mainRect.width;
            if (mainRect.yMax > Screen.height)
                mainRect.y = Screen.height - mainRect.height;

            GUI.Box(mainRect, GUIContent.none, s_MenuBgStyle);
            bool clicked = DrawMenuItems(mainRect, null);

            // Draw submenu if open
            Rect subRect = default;
            bool hasSubmenu = openSubmenu != null;
            if (hasSubmenu)
            {
                float subHeight = CalculateMenuHeight(openSubmenu);
                subRect = new Rect(mainRect.xMax - 2f, GetSubmenuY(mainRect, openSubmenu), SUBMENU_WIDTH, subHeight);

                if (subRect.xMax > Screen.width)
                    subRect.x = mainRect.x - SUBMENU_WIDTH + 2f;
                if (subRect.yMax > Screen.height)
                    subRect.y = Screen.height - subRect.height;

                GUI.Box(subRect, GUIContent.none, s_MenuBgStyle);
                bool subClicked = DrawMenuItems(subRect, openSubmenu);
                clicked = clicked || subClicked;
            }

            // Click outside to close
            if (!clicked && Event.current.type == EventType.MouseDown)
            {
                bool insideMenu = mainRect.Contains(Event.current.mousePosition);
                if (hasSubmenu)
                    insideMenu = insideMenu || subRect.Contains(Event.current.mousePosition);

                if (!insideMenu)
                {
                    Close();
                    Event.current.Use();
                    return true;
                }
            }

            return clicked;
        }

        private bool DrawMenuItems(Rect menuRect, string parentSubmenu)
        {
            float y = menuRect.y + 2f;
            bool clicked = false;

            // Collect unique submenus at this level
            HashSet<string> drawnSubmenus = new HashSet<string>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (item.isSeparator && item.submenuParent == parentSubmenu)
                {
                    Rect sepRect = new Rect(menuRect.x + 4f, y + 2f, menuRect.width - 8f, 1f);
                    GUI.color = new Color(0.4f, 0.4f, 0.4f, 0.8f);
                    GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    y += SEPARATOR_HEIGHT;
                    continue;
                }

                // Check if this item belongs to a submenu that should be shown as a parent entry
                if (parentSubmenu == null && item.submenuParent != null)
                {
                    if (drawnSubmenus.Contains(item.submenuParent)) continue;
                    drawnSubmenus.Add(item.submenuParent);

                    // Draw submenu parent label
                    Rect itemRect = new Rect(menuRect.x, y, menuRect.width, ITEM_HEIGHT);
                    bool isHovered = itemRect.Contains(Event.current.mousePosition);

                    if (isHovered)
                    {
                        GUI.color = new Color(0.24f, 0.49f, 0.91f, 1f);
                        GUI.DrawTexture(itemRect, Texture2D.whiteTexture);
                        GUI.color = Color.white;
                        openSubmenu = item.submenuParent;
                    }

                    GUI.Label(new Rect(itemRect.x + 20f, itemRect.y, itemRect.width - 40f, itemRect.height), item.submenuParent, s_MenuItemStyle);
                    GUI.Label(new Rect(itemRect.xMax - 20f, itemRect.y, 20f, itemRect.height), "\u25B6", s_MenuItemStyle);

                    y += ITEM_HEIGHT;
                    continue;
                }

                if (item.submenuParent != parentSubmenu) continue;

                Rect rect = new Rect(menuRect.x, y, menuRect.width, ITEM_HEIGHT);
                bool hovered = rect.Contains(Event.current.mousePosition);

                if (hovered && item.isEnabled)
                {
                    GUI.color = new Color(0.24f, 0.49f, 0.91f, 1f);
                    GUI.DrawTexture(rect, Texture2D.whiteTexture);
                    GUI.color = Color.white;

                    if (parentSubmenu == null)
                        openSubmenu = null;
                }

                // Checkmark
                if (item.isChecked)
                {
                    GUI.Label(new Rect(rect.x + 4f, rect.y, 16f, rect.height), "\u2713", s_MenuItemCheckedStyle);
                }

                // Label
                GUIStyle style = item.isEnabled ? s_MenuItemStyle : s_MenuItemCheckedStyle;
                Color prevColor = GUI.color;
                if (!item.isEnabled) GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                GUI.Label(new Rect(rect.x + 20f, rect.y, rect.width - 24f, rect.height), item.label, style);
                GUI.color = prevColor;

                // Click
                if (hovered && item.isEnabled && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    item.callback?.Invoke();
                    Close();
                    clicked = true;
                    Event.current.Use();
                }

                y += ITEM_HEIGHT;
            }

            return clicked;
        }

        private float CalculateMenuHeight(string parentSubmenu)
        {
            float height = 4f;
            HashSet<string> drawnSubmenus = new HashSet<string>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (item.isSeparator && item.submenuParent == parentSubmenu)
                {
                    height += SEPARATOR_HEIGHT;
                    continue;
                }

                if (parentSubmenu == null && item.submenuParent != null)
                {
                    if (drawnSubmenus.Contains(item.submenuParent)) continue;
                    drawnSubmenus.Add(item.submenuParent);
                    height += ITEM_HEIGHT;
                    continue;
                }

                if (item.submenuParent != parentSubmenu) continue;
                height += ITEM_HEIGHT;
            }

            return height;
        }

        private float GetSubmenuY(Rect mainRect, string submenuLabel)
        {
            float y = mainRect.y + 2f;
            HashSet<string> drawnSubmenus = new HashSet<string>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.isSeparator && item.submenuParent == null)
                {
                    y += SEPARATOR_HEIGHT;
                    continue;
                }

                if (item.submenuParent != null)
                {
                    if (drawnSubmenus.Contains(item.submenuParent)) continue;
                    drawnSubmenus.Add(item.submenuParent);

                    if (item.submenuParent == submenuLabel)
                        return y;

                    y += ITEM_HEIGHT;
                    continue;
                }

                y += ITEM_HEIGHT;
            }

            return mainRect.y;
        }

        private static void EnsureStyles()
        {
            if (s_MenuItemStyle != null) return;

            s_MenuItemStyle = new GUIStyle(GUI.skin.label);
            s_MenuItemStyle.fontSize = 12;
            s_MenuItemStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            s_MenuItemStyle.alignment = TextAnchor.MiddleLeft;
            s_MenuItemStyle.padding = new RectOffset(2, 2, 0, 0);

            s_MenuItemCheckedStyle = new GUIStyle(s_MenuItemStyle);
            s_MenuItemCheckedStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            s_MenuItemCheckedStyle.fontStyle = FontStyle.Bold;

            s_MenuBgStyle = new GUIStyle();
            Texture2D bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.22f, 0.22f, 0.22f, 0.97f));
            bgTex.Apply();
            bgTex.hideFlags = HideFlags.HideAndDontSave;
            s_MenuBgStyle.normal.background = bgTex;
            s_MenuBgStyle.border = new RectOffset(1, 1, 1, 1);

            s_SeparatorStyle = new GUIStyle();
        }

        public void Clear()
        {
            items.Clear();
            openSubmenu = null;
        }

        public static void CleanupStyles()
        {
            if (s_MenuBgStyle != null && s_MenuBgStyle.normal.background != null)
            {
                UnityEngine.Object.DestroyImmediate(s_MenuBgStyle.normal.background);
            }
            s_MenuItemStyle = null;
            s_MenuItemCheckedStyle = null;
            s_MenuBgStyle = null;
            s_SeparatorStyle = null;
        }
    }

    public class RuntimeCurveMenuManager
    {
        private RuntimeCurveEditorCore editor;
        private RuntimeContextMenu contextMenu;

        public RuntimeCurveMenuManager(RuntimeCurveEditorCore editor)
        {
            this.editor = editor;
            contextMenu = new RuntimeContextMenu();
        }

        public void ShowContextMenu(Vector2 position)
        {
            contextMenu.Clear();

            AnimationCurve curve = editor.curveWrapper.curve;
            if (curve == null) return;

            var selectedIndices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);
            bool hasSelection = selectedIndices.Count > 0;

            // Add key option
            contextMenu.AddItem("Add Key", false, () =>
            {
                Vector2 drawingPos = RuntimeCurveRenderer.ViewToDrawing(position, editor.curveArea, editor.shownAreaMin, editor.shownAreaMax);
                float value = curve.length > 0 ? curve.Evaluate(drawingPos.x) : drawingPos.y;
                Keyframe kf = new Keyframe(drawingPos.x, value);
                int idx = editor.curveWrapper.AddKey(kf);
                if (idx >= 0)
                {
                    KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(curve, idx);
                    editor.selection.SelectKey(editor.curveWrapper.id, idx, false);
                }
            });

            if (hasSelection)
            {
                contextMenu.AddItem("Delete Key", false, () =>
                {
                    var indices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);
                    indices.Sort((a, b) => b.CompareTo(a));
                    foreach (int idx in indices)
                    {
                        if (idx >= 0 && idx < curve.length)
                        {
                            curve.RemoveKey(idx);
                            editor.selection.UpdateKeyIndicesAfterRemoval(editor.curveWrapper.id, idx);
                        }
                    }
                    editor.selection.SelectNone();
                    editor.curveWrapper.changed = true;
                });
            }

            contextMenu.AddSeparator();

            if (hasSelection)
            {
                // Build tangent mode check states
                bool allClampedAuto = true, allAuto = true, allFreeSmooth = true, allFlat = true, allBroken = true;
                bool allLeftFree = true, allLeftLinear = true, allLeftConstant = true, allLeftWeighted = true;
                bool allRightFree = true, allRightLinear = true, allRightConstant = true, allRightWeighted = true;

                foreach (int idx in selectedIndices)
                {
                    if (idx < 0 || idx >= curve.length) continue;
                    Keyframe kf = curve[idx];
                    var leftMode = KeyframeTangentUtility.GetKeyLeftTangentMode(kf);
                    var rightMode = KeyframeTangentUtility.GetKeyRightTangentMode(kf);
                    bool broken = KeyframeTangentUtility.GetKeyBroken(kf);

                    if (leftMode != KeyframeTangentUtility.TangentMode.ClampedAuto || rightMode != KeyframeTangentUtility.TangentMode.ClampedAuto) allClampedAuto = false;
                    if (leftMode != KeyframeTangentUtility.TangentMode.Auto || rightMode != KeyframeTangentUtility.TangentMode.Auto) allAuto = false;
                    if (broken || leftMode != KeyframeTangentUtility.TangentMode.Free || rightMode != KeyframeTangentUtility.TangentMode.Free) allFreeSmooth = false;
                    if (broken || leftMode != KeyframeTangentUtility.TangentMode.Free || kf.inTangent != 0f || rightMode != KeyframeTangentUtility.TangentMode.Free || kf.outTangent != 0f) allFlat = false;
                    if (!broken) allBroken = false;

                    if (!broken || leftMode != KeyframeTangentUtility.TangentMode.Free) allLeftFree = false;
                    if (!broken || leftMode != KeyframeTangentUtility.TangentMode.Linear) allLeftLinear = false;
                    if (!broken || leftMode != KeyframeTangentUtility.TangentMode.Constant) allLeftConstant = false;
                    if ((kf.weightedMode & WeightedMode.In) == 0) allLeftWeighted = false;

                    if (!broken || rightMode != KeyframeTangentUtility.TangentMode.Free) allRightFree = false;
                    if (!broken || rightMode != KeyframeTangentUtility.TangentMode.Linear) allRightLinear = false;
                    if (!broken || rightMode != KeyframeTangentUtility.TangentMode.Constant) allRightConstant = false;
                    if ((kf.weightedMode & WeightedMode.Out) == 0) allRightWeighted = false;
                }

                contextMenu.AddItem("Clamped Auto", allClampedAuto, () => SetBothTangentMode(KeyframeTangentUtility.TangentMode.ClampedAuto));
                contextMenu.AddItem("Auto", allAuto, () => SetBothTangentMode(KeyframeTangentUtility.TangentMode.Auto));
                contextMenu.AddItem("Free Smooth", allFreeSmooth, () => SetFreeSmooth());
                contextMenu.AddItem("Flat", allFlat, () => SetFlat());
                contextMenu.AddItem("Broken", allBroken, () => SetBroken());

                contextMenu.AddSeparator();

                // Left Tangent submenu
                contextMenu.AddItem("Free", allLeftFree, () => SetTangent(0, KeyframeTangentUtility.TangentMode.Free), "Left Tangent");
                contextMenu.AddItem("Linear", allLeftLinear, () => SetTangent(0, KeyframeTangentUtility.TangentMode.Linear), "Left Tangent");
                contextMenu.AddItem("Constant", allLeftConstant, () => SetTangent(0, KeyframeTangentUtility.TangentMode.Constant), "Left Tangent");
                contextMenu.AddItem("Weighted", allLeftWeighted, () => ToggleWeighted(WeightedMode.In), "Left Tangent");

                // Right Tangent submenu
                contextMenu.AddItem("Free", allRightFree, () => SetTangent(1, KeyframeTangentUtility.TangentMode.Free), "Right Tangent");
                contextMenu.AddItem("Linear", allRightLinear, () => SetTangent(1, KeyframeTangentUtility.TangentMode.Linear), "Right Tangent");
                contextMenu.AddItem("Constant", allRightConstant, () => SetTangent(1, KeyframeTangentUtility.TangentMode.Constant), "Right Tangent");
                contextMenu.AddItem("Weighted", allRightWeighted, () => ToggleWeighted(WeightedMode.Out), "Right Tangent");

                // Both Tangents submenu
                contextMenu.AddItem("Free", allLeftFree && allRightFree, () => SetTangent(2, KeyframeTangentUtility.TangentMode.Free), "Both Tangents");
                contextMenu.AddItem("Linear", allLeftLinear && allRightLinear, () => SetTangent(2, KeyframeTangentUtility.TangentMode.Linear), "Both Tangents");
                contextMenu.AddItem("Constant", allLeftConstant && allRightConstant, () => SetTangent(2, KeyframeTangentUtility.TangentMode.Constant), "Both Tangents");
                contextMenu.AddItem("Weighted", allLeftWeighted && allRightWeighted, () => ToggleWeighted(WeightedMode.Both), "Both Tangents");
            }
            else
            {
                contextMenu.AddDisabledItem("Clamped Auto");
                contextMenu.AddDisabledItem("Auto");
                contextMenu.AddDisabledItem("Free Smooth");
                contextMenu.AddDisabledItem("Flat");
                contextMenu.AddDisabledItem("Broken");
            }

            contextMenu.Show(position);
        }

        public Rect GetMenuRect(Vector2 position)
        {
            if (contextMenu.IsOpen)
                return contextMenu.GetMenuRect();
            return new Rect(position.x, position.y, 200f, 300f);
        }

        public bool OnGUI()
        {
            return contextMenu.OnGUI();
        }

        public bool IsOpen => contextMenu.IsOpen;

        public void Close()
        {
            contextMenu.Close();
        }

        private void SetBothTangentMode(KeyframeTangentUtility.TangentMode mode)
        {
            AnimationCurve curve = editor.curveWrapper.curve;
            var indices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= curve.length) continue;
                Keyframe kf = curve[idx];
                KeyframeTangentUtility.SetKeyBroken(ref kf, false);
                KeyframeTangentUtility.SetKeyLeftTangentMode(ref kf, mode);
                KeyframeTangentUtility.SetKeyRightTangentMode(ref kf, mode);
                curve.MoveKey(idx, kf);
                KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(curve, idx);
            }

            editor.curveWrapper.changed = true;
        }

        private void SetFreeSmooth()
        {
            AnimationCurve curve = editor.curveWrapper.curve;
            var indices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= curve.length) continue;
                Keyframe kf = curve[idx];
                KeyframeTangentUtility.SetKeyBroken(ref kf, false);
                KeyframeTangentUtility.SetKeyLeftTangentMode(ref kf, KeyframeTangentUtility.TangentMode.Free);
                KeyframeTangentUtility.SetKeyRightTangentMode(ref kf, KeyframeTangentUtility.TangentMode.Free);
                float smoothTangent = CalculateSmoothTangent(kf);
                kf.inTangent = smoothTangent;
                kf.outTangent = smoothTangent;
                curve.MoveKey(idx, kf);
                KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(curve, idx);
            }

            editor.curveWrapper.changed = true;
        }

        private void SetFlat()
        {
            SetFreeSmooth();

            AnimationCurve curve = editor.curveWrapper.curve;
            var indices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= curve.length) continue;
                Keyframe kf = curve[idx];
                kf.inTangent = 0f;
                kf.outTangent = 0f;
                curve.MoveKey(idx, kf);
                KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(curve, idx);
            }

            editor.curveWrapper.changed = true;
        }

        private void SetBroken()
        {
            AnimationCurve curve = editor.curveWrapper.curve;
            var indices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= curve.length) continue;
                Keyframe kf = curve[idx];
                KeyframeTangentUtility.SetKeyBroken(ref kf, true);

                if (KeyframeTangentUtility.GetKeyLeftTangentMode(kf) == KeyframeTangentUtility.TangentMode.ClampedAuto ||
                    KeyframeTangentUtility.GetKeyLeftTangentMode(kf) == KeyframeTangentUtility.TangentMode.Auto)
                    KeyframeTangentUtility.SetKeyLeftTangentMode(ref kf, KeyframeTangentUtility.TangentMode.Free);

                if (KeyframeTangentUtility.GetKeyRightTangentMode(kf) == KeyframeTangentUtility.TangentMode.ClampedAuto ||
                    KeyframeTangentUtility.GetKeyRightTangentMode(kf) == KeyframeTangentUtility.TangentMode.Auto)
                    KeyframeTangentUtility.SetKeyRightTangentMode(ref kf, KeyframeTangentUtility.TangentMode.Free);

                curve.MoveKey(idx, kf);
                KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(curve, idx);
            }

            editor.curveWrapper.changed = true;
        }

        private void SetTangent(int leftRight, KeyframeTangentUtility.TangentMode mode)
        {
            AnimationCurve curve = editor.curveWrapper.curve;
            var indices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= curve.length) continue;
                Keyframe kf = curve[idx];
                KeyframeTangentUtility.SetKeyBroken(ref kf, true);

                if (leftRight == 0 || leftRight == 2)
                {
                    KeyframeTangentUtility.SetKeyLeftTangentMode(ref kf, mode);
                    if (mode == KeyframeTangentUtility.TangentMode.Constant)
                        kf.inTangent = float.PositiveInfinity;
                }
                if (leftRight == 1 || leftRight == 2)
                {
                    KeyframeTangentUtility.SetKeyRightTangentMode(ref kf, mode);
                    if (mode == KeyframeTangentUtility.TangentMode.Constant)
                        kf.outTangent = float.PositiveInfinity;
                }

                // When setting one side, ensure the other side is not auto/clamped
                if (leftRight == 0)
                {
                    var rm = KeyframeTangentUtility.GetKeyRightTangentMode(kf);
                    if (rm == KeyframeTangentUtility.TangentMode.ClampedAuto || rm == KeyframeTangentUtility.TangentMode.Auto)
                        KeyframeTangentUtility.SetKeyRightTangentMode(ref kf, KeyframeTangentUtility.TangentMode.Free);
                }
                else if (leftRight == 1)
                {
                    var lm = KeyframeTangentUtility.GetKeyLeftTangentMode(kf);
                    if (lm == KeyframeTangentUtility.TangentMode.ClampedAuto || lm == KeyframeTangentUtility.TangentMode.Auto)
                        KeyframeTangentUtility.SetKeyLeftTangentMode(ref kf, KeyframeTangentUtility.TangentMode.Free);
                }

                curve.MoveKey(idx, kf);
                KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(curve, idx);
            }

            editor.curveWrapper.changed = true;
        }

        private void ToggleWeighted(WeightedMode targetMode)
        {
            AnimationCurve curve = editor.curveWrapper.curve;
            var indices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);

            // Check if all selected keys already have this weighted mode
            bool allWeighted = true;
            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= curve.length) continue;
                Keyframe kf = curve[idx];
                if ((kf.weightedMode & targetMode) != targetMode)
                {
                    allWeighted = false;
                    break;
                }
            }

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= curve.length) continue;
                Keyframe kf = curve[idx];

                if (allWeighted)
                    kf.weightedMode = (WeightedMode)((int)kf.weightedMode & ~(int)targetMode);
                else
                    kf.weightedMode = (WeightedMode)((int)kf.weightedMode | (int)targetMode);

                // Set default weights when enabling
                if ((kf.weightedMode & WeightedMode.In) != 0 && kf.inWeight == 0f)
                    kf.inWeight = 1f / 3f;
                if ((kf.weightedMode & WeightedMode.Out) != 0 && kf.outWeight == 0f)
                    kf.outWeight = 1f / 3f;

                // Ensure tangent modes are Free when weighted
                if ((kf.weightedMode & WeightedMode.In) != 0)
                {
                    var lm = KeyframeTangentUtility.GetKeyLeftTangentMode(kf);
                    if (lm == KeyframeTangentUtility.TangentMode.Linear || lm == KeyframeTangentUtility.TangentMode.Constant)
                        KeyframeTangentUtility.SetKeyLeftTangentMode(ref kf, KeyframeTangentUtility.TangentMode.Free);
                }
                if ((kf.weightedMode & WeightedMode.Out) != 0)
                {
                    var rm = KeyframeTangentUtility.GetKeyRightTangentMode(kf);
                    if (rm == KeyframeTangentUtility.TangentMode.Linear || rm == KeyframeTangentUtility.TangentMode.Constant)
                        KeyframeTangentUtility.SetKeyRightTangentMode(ref kf, KeyframeTangentUtility.TangentMode.Free);
                }

                curve.MoveKey(idx, kf);
                KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(curve, idx);
            }

            editor.curveWrapper.changed = true;
        }

        private static float CalculateSmoothTangent(Keyframe key)
        {
            float inT = key.inTangent;
            float outT = key.outTangent;
            if (float.IsInfinity(inT)) inT = 0f;
            if (float.IsInfinity(outT)) outT = 0f;
            return (inT + outT) * 0.5f;
        }
    }
}
