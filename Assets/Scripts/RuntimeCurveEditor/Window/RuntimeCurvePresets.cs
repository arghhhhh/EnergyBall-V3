using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeCurveEditor
{
    public class RuntimeCurvePresets
    {
        public struct CurvePreset
        {
            public string name;
            public AnimationCurve curve;
            public bool isUserPreset;
        }

        private List<CurvePreset> defaultPresets;
        private List<CurvePreset> userPresets;
        private RuntimeCurveEditorSettings settings;

        private const float PRESET_WIDTH = 40f;
        private const float PRESET_HEIGHT = 25f;
        private const float PRESET_SPACING = 5f;
        private const float SEPARATOR_WIDTH = 2f;
        private const float SEPARATOR_MARGIN = 8f;
        private const float ADD_BUTTON_WIDTH = 25f;
        private const float SCROLL_BAR_HEIGHT = 14f;

        private float scrollOffset;

        private static GUIStyle s_PresetButtonStyle;
        private static GUIStyle s_PresetLabelStyle;
        private static GUIStyle s_AddButtonStyle;
        private static GUIStyle s_SaveDialogLabelStyle;
        private static GUIStyle s_SaveDialogFieldStyle;
        private static GUIStyle s_SaveDialogButtonStyle;
        private static Texture2D s_PresetBgTex;

        // Save dialog state
        private bool showSaveDialog;
        private string saveDialogName = "";
        private AnimationCurve saveDialogCurve;

        // Delete confirmation state
        private bool showDeleteConfirm;
        private int deletePresetIndex = -1;

        public RuntimeCurvePresets(RuntimeCurveEditorSettings settings)
        {
            this.settings = settings;
            defaultPresets = new List<CurvePreset>();
            CreateDefaultPresets();
            userPresets = RuntimeCurvePresetStorage.LoadAllPresets();
        }

        private void CreateDefaultPresets()
        {
            defaultPresets.Clear();

            defaultPresets.Add(new CurvePreset
            {
                name = "Constant",
                curve = CreateCurve(GetConstantKeys(1f))
            });

            defaultPresets.Add(new CurvePreset
            {
                name = "Linear",
                curve = CreateCurve(GetLinearKeys())
            });

            defaultPresets.Add(new CurvePreset
            {
                name = "Ease In",
                curve = CreateCurve(GetEaseInKeys())
            });

            defaultPresets.Add(new CurvePreset
            {
                name = "Ease Out",
                curve = CreateCurve(GetEaseOutKeys())
            });

            defaultPresets.Add(new CurvePreset
            {
                name = "Ease In-Out",
                curve = CreateCurve(GetEaseInOutKeys())
            });
        }

        public void RefreshUserPresets()
        {
            userPresets = RuntimeCurvePresetStorage.LoadAllPresets();
        }

        public void OnGUI(Rect barRect, Action<AnimationCurve> onPresetSelected, AnimationCurve currentCurve)
        {
            EnsureStyles();

            // Background
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(barRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Separator line at top
            Rect sepRect = new Rect(barRect.x, barRect.y, barRect.width, 1f);
            GUI.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Calculate total content width
            float padding = 10f;
            float contentWidth = padding;
            contentWidth += defaultPresets.Count * (PRESET_WIDTH + PRESET_SPACING);
            contentWidth += SEPARATOR_MARGIN + SEPARATOR_WIDTH + SEPARATOR_MARGIN;
            contentWidth += userPresets.Count * (PRESET_WIDTH + PRESET_SPACING);
            contentWidth += ADD_BUTTON_WIDTH + padding;

            bool needsScroll = contentWidth > barRect.width;
            float scrollBarHeight = needsScroll ? SCROLL_BAR_HEIGHT : 0f;
            float presetsAreaHeight = barRect.height - scrollBarHeight;

            // Clamp scroll offset
            float maxScroll = Mathf.Max(0f, contentWidth - barRect.width);
            scrollOffset = Mathf.Clamp(scrollOffset, 0f, maxScroll);

            // Handle mouse wheel scrolling within the bar area
            if (needsScroll && Event.current.type == EventType.ScrollWheel && barRect.Contains(Event.current.mousePosition))
            {
                scrollOffset = Mathf.Clamp(scrollOffset + Event.current.delta.y * 20f, 0f, maxScroll);
                Event.current.Use();
            }

            float presetY = barRect.y + (presetsAreaHeight - PRESET_HEIGHT) / 2f;
            float currentX = barRect.x + padding - scrollOffset;

            // Draw default presets (only if visible)
            for (int i = 0; i < defaultPresets.Count; i++)
            {
                Rect presetRect = new Rect(currentX, presetY, PRESET_WIDTH, PRESET_HEIGHT);
                if (IsRectVisible(presetRect, barRect))
                    DrawPresetButton(presetRect, defaultPresets[i], onPresetSelected, -1);
                currentX += PRESET_WIDTH + PRESET_SPACING;
            }

            // Draw separator
            float separatorX = currentX + SEPARATOR_MARGIN;
            if (separatorX >= barRect.x && separatorX <= barRect.xMax)
            {
                Rect vertSepRect = new Rect(separatorX, barRect.y + 8f, SEPARATOR_WIDTH, presetsAreaHeight - 16f);
                GUI.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                GUI.DrawTexture(vertSepRect, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            currentX = separatorX + SEPARATOR_WIDTH + SEPARATOR_MARGIN;

            // Draw user presets (only if visible)
            for (int i = 0; i < userPresets.Count; i++)
            {
                Rect presetRect = new Rect(currentX, presetY, PRESET_WIDTH, PRESET_HEIGHT);
                if (IsRectVisible(presetRect, barRect))
                    DrawPresetButton(presetRect, userPresets[i], onPresetSelected, i);
                currentX += PRESET_WIDTH + PRESET_SPACING;
            }

            // Draw "+" button (only if visible)
            Rect addRect = new Rect(currentX, presetY, ADD_BUTTON_WIDTH, PRESET_HEIGHT);
            if (IsRectVisible(addRect, barRect))
                DrawAddButton(addRect, currentCurve);

            // Draw horizontal scrollbar if needed
            if (needsScroll)
            {
                Rect scrollBarRect = new Rect(barRect.x, barRect.yMax - SCROLL_BAR_HEIGHT, barRect.width, SCROLL_BAR_HEIGHT);
                scrollOffset = GUI.HorizontalScrollbar(scrollBarRect, scrollOffset, barRect.width, 0f, contentWidth);
            }

            // Draw save dialog overlay if active
            if (showSaveDialog)
            {
                DrawSaveDialog(barRect);
            }

            // Draw delete confirmation overlay if active
            if (showDeleteConfirm && deletePresetIndex >= 0 && deletePresetIndex < userPresets.Count)
            {
                DrawDeleteConfirm(barRect);
            }
        }

        /// <summary>Returns true if the element rect is fully within the visible bar area horizontally.</summary>
        private static bool IsRectVisible(Rect elementRect, Rect clipRect)
        {
            return elementRect.x >= clipRect.x && elementRect.xMax <= clipRect.xMax;
        }

        private void DrawPresetButton(Rect presetRect, CurvePreset preset, Action<AnimationCurve> onPresetSelected, int userPresetIndex)
        {
            bool hovered = presetRect.Contains(Event.current.mousePosition);
            Color bgColor = hovered ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);

            // User presets get a slightly different tint
            if (preset.isUserPreset)
                bgColor = hovered ? new Color(0.30f, 0.35f, 0.30f, 1f) : new Color(0.22f, 0.27f, 0.22f, 1f);

            GUI.color = bgColor;
            GUI.DrawTexture(presetRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            DrawRectBorder(presetRect, new Color(0.4f, 0.4f, 0.4f, 0.5f));

            Color curveColor = preset.isUserPreset
                ? new Color(0.2f, 0.7f, 1.0f, 0.9f)
                : new Color(0.0f, 0.8f, 0.0f, 0.9f);
            RuntimeCurveRenderer.DrawMiniCurve(preset.curve, presetRect, curveColor);

            if (hovered)
            {
                Rect tooltipRect = new Rect(presetRect.x, presetRect.y - 16f, 80f, 14f);
                GUI.Label(tooltipRect, preset.name, s_PresetLabelStyle);
            }

            if (hovered && Event.current.type == EventType.MouseDown)
            {
                if (Event.current.button == 0)
                {
                    // Left-click: apply preset
                    AnimationCurve appliedCurve = DenormalizePreset(preset.curve);
                    onPresetSelected?.Invoke(appliedCurve);
                    Event.current.Use();
                }
                else if (Event.current.button == 1 && userPresetIndex >= 0)
                {
                    // Right-click on user preset: show delete confirmation
                    showDeleteConfirm = true;
                    deletePresetIndex = userPresetIndex;
                    Event.current.Use();
                }
            }
        }

        private void DrawAddButton(Rect addRect, AnimationCurve currentCurve)
        {
            bool hovered = addRect.Contains(Event.current.mousePosition);
            Color bgColor = hovered ? new Color(0.35f, 0.40f, 0.35f, 1f) : new Color(0.25f, 0.30f, 0.25f, 1f);
            GUI.color = bgColor;
            GUI.DrawTexture(addRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            DrawRectBorder(addRect, new Color(0.4f, 0.4f, 0.4f, 0.5f));

            // Draw "+" text
            GUI.Label(addRect, "+", new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.7f, 0.9f, 0.7f, 1f) }
            });

            if (hovered)
            {
                Rect tooltipRect = new Rect(addRect.x - 10f, addRect.y - 16f, 80f, 14f);
                GUI.Label(tooltipRect, "Save Preset", s_PresetLabelStyle);
            }

            if (hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                showSaveDialog = true;
                saveDialogName = "";
                saveDialogCurve = currentCurve;
                Event.current.Use();
            }
        }

        private void DrawSaveDialog(Rect barRect)
        {
            EnsureDialogStyles();

            // Overlay background
            Rect screenRect = new Rect(0, 0, Screen.width, Screen.height);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(screenRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Dialog panel
            float dialogWidth = 260f;
            float dialogHeight = 100f;
            float dialogX = barRect.x + (barRect.width - dialogWidth) / 2f;
            float dialogY = barRect.y - dialogHeight - 10f;

            Rect dialogRect = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);

            // Background
            GUI.color = new Color(0.18f, 0.18f, 0.18f, 1f);
            GUI.DrawTexture(dialogRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            DrawRectBorder(dialogRect, new Color(0.5f, 0.5f, 0.5f, 0.8f));

            // Title
            Rect titleRect = new Rect(dialogRect.x + 10f, dialogRect.y + 8f, dialogRect.width - 20f, 20f);
            GUI.Label(titleRect, "Save Curve Preset", s_SaveDialogLabelStyle);

            // Name field
            Rect fieldLabelRect = new Rect(dialogRect.x + 10f, dialogRect.y + 32f, 40f, 20f);
            GUI.Label(fieldLabelRect, "Name:", s_PresetLabelStyle);

            Rect fieldRect = new Rect(dialogRect.x + 55f, dialogRect.y + 30f, dialogRect.width - 70f, 20f);
            GUI.SetNextControlName("PresetNameField");
            saveDialogName = GUI.TextField(fieldRect, saveDialogName, s_SaveDialogFieldStyle);
            GUI.FocusControl("PresetNameField");

            // Buttons
            float buttonWidth = 60f;
            float buttonSpacing = 10f;
            float buttonsStartX = dialogRect.x + (dialogRect.width - (buttonWidth * 2 + buttonSpacing)) / 2f;
            float buttonsY = dialogRect.y + dialogHeight - 30f;

            Rect saveRect = new Rect(buttonsStartX, buttonsY, buttonWidth, 22f);
            Rect cancelRect = new Rect(buttonsStartX + buttonWidth + buttonSpacing, buttonsY, buttonWidth, 22f);

            bool shouldSave = false;
            bool shouldCancel = false;

            if (GUI.Button(saveRect, "Save", s_SaveDialogButtonStyle))
                shouldSave = true;

            if (GUI.Button(cancelRect, "Cancel", s_SaveDialogButtonStyle))
                shouldCancel = true;

            // Handle Enter/Escape keys
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    shouldSave = true;
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Escape)
                {
                    shouldCancel = true;
                    Event.current.Use();
                }
            }

            if (shouldSave && !string.IsNullOrEmpty(saveDialogName.Trim()))
            {
                RuntimeCurvePresetStorage.SavePreset(saveDialogName.Trim(), saveDialogCurve);
                RefreshUserPresets();
                showSaveDialog = false;
                saveDialogCurve = null;
            }

            if (shouldCancel)
            {
                showSaveDialog = false;
                saveDialogCurve = null;
            }

            // Consume mouse events on the dialog to prevent click-through
            if (Event.current.type == EventType.MouseDown && dialogRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
            }
        }

        private void DrawDeleteConfirm(Rect barRect)
        {
            EnsureDialogStyles();

            string presetName = userPresets[deletePresetIndex].name;

            // Overlay background
            Rect screenRect = new Rect(0, 0, Screen.width, Screen.height);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(screenRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Dialog panel
            float dialogWidth = 240f;
            float dialogHeight = 80f;
            float dialogX = barRect.x + (barRect.width - dialogWidth) / 2f;
            float dialogY = barRect.y - dialogHeight - 10f;

            Rect dialogRect = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);

            GUI.color = new Color(0.18f, 0.18f, 0.18f, 1f);
            GUI.DrawTexture(dialogRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            DrawRectBorder(dialogRect, new Color(0.5f, 0.5f, 0.5f, 0.8f));

            Rect labelRect = new Rect(dialogRect.x + 10f, dialogRect.y + 10f, dialogRect.width - 20f, 30f);
            GUI.Label(labelRect, $"Delete \"{presetName}\"?", s_SaveDialogLabelStyle);

            float buttonWidth = 60f;
            float buttonSpacing = 10f;
            float buttonsStartX = dialogRect.x + (dialogRect.width - (buttonWidth * 2 + buttonSpacing)) / 2f;
            float buttonsY = dialogRect.y + dialogHeight - 30f;

            Rect deleteRect = new Rect(buttonsStartX, buttonsY, buttonWidth, 22f);
            Rect cancelRect = new Rect(buttonsStartX + buttonWidth + buttonSpacing, buttonsY, buttonWidth, 22f);

            bool shouldDelete = false;
            bool shouldCancel = false;

            if (GUI.Button(deleteRect, "Delete", s_SaveDialogButtonStyle))
                shouldDelete = true;

            if (GUI.Button(cancelRect, "Cancel", s_SaveDialogButtonStyle))
                shouldCancel = true;

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                shouldCancel = true;
                Event.current.Use();
            }

            if (shouldDelete)
            {
                RuntimeCurvePresetStorage.DeletePreset(presetName);
                RefreshUserPresets();
                showDeleteConfirm = false;
                deletePresetIndex = -1;
            }

            if (shouldCancel)
            {
                showDeleteConfirm = false;
                deletePresetIndex = -1;
            }

            if (Event.current.type == EventType.MouseDown && dialogRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
            }
        }

        public bool IsDialogOpen => showSaveDialog || showDeleteConfirm;

        private AnimationCurve DenormalizePreset(AnimationCurve normalizedCurve)
        {
            if (settings.hasUnboundedRanges)
            {
                return CopyCurve(normalizedCurve);
            }

            float hMin = settings.hRangeMin;
            float hMax = settings.hRangeMax;
            float vMin = settings.vRangeMin;
            float vMax = settings.vRangeMax;
            float hRange = hMax - hMin;
            float vRange = vMax - vMin;

            if (hRange == 0f || vRange == 0f)
                return CopyCurve(normalizedCurve);

            float tangentScale = vRange / hRange;

            Keyframe[] keys = new Keyframe[normalizedCurve.length];
            for (int i = 0; i < normalizedCurve.length; i++)
            {
                Keyframe src = normalizedCurve[i];
                keys[i] = new Keyframe(
                    src.time * hRange + hMin,
                    src.value * vRange + vMin,
                    float.IsInfinity(src.inTangent) ? src.inTangent : src.inTangent * tangentScale,
                    float.IsInfinity(src.outTangent) ? src.outTangent : src.outTangent * tangentScale
                );
                keys[i].weightedMode = src.weightedMode;
                keys[i].inWeight = src.inWeight;
                keys[i].outWeight = src.outWeight;

                KeyframeTangentUtility.SetKeyBroken(ref keys[i], KeyframeTangentUtility.GetKeyBroken(src));
                KeyframeTangentUtility.SetKeyLeftTangentMode(ref keys[i], KeyframeTangentUtility.GetKeyLeftTangentMode(src));
                KeyframeTangentUtility.SetKeyRightTangentMode(ref keys[i], KeyframeTangentUtility.GetKeyRightTangentMode(src));
            }

            AnimationCurve result = new AnimationCurve(keys);
            result.preWrapMode = normalizedCurve.preWrapMode;
            result.postWrapMode = normalizedCurve.postWrapMode;
            return result;
        }

        private static AnimationCurve CopyCurve(AnimationCurve source)
        {
            Keyframe[] keys = new Keyframe[source.length];
            for (int i = 0; i < source.length; i++)
                keys[i] = source[i];
            AnimationCurve copy = new AnimationCurve(keys);
            copy.preWrapMode = source.preWrapMode;
            copy.postWrapMode = source.postWrapMode;
            return copy;
        }

        private static AnimationCurve CreateCurve(Keyframe[] keys)
        {
            return new AnimationCurve(keys);
        }

        private static void DrawRectBorder(Rect rect, Color color)
        {
            RuntimeCurveRenderer.DrawLine(new Vector2(rect.x, rect.y), new Vector2(rect.xMax, rect.y), color);
            RuntimeCurveRenderer.DrawLine(new Vector2(rect.xMax, rect.y), new Vector2(rect.xMax, rect.yMax), color);
            RuntimeCurveRenderer.DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.x, rect.yMax), color);
            RuntimeCurveRenderer.DrawLine(new Vector2(rect.x, rect.yMax), new Vector2(rect.x, rect.y), color);
        }

        // --- Preset keyframe factories ---

        private static Keyframe[] GetLinearKeys()
        {
            Keyframe[] keys = new Keyframe[]
            {
                new Keyframe(0f, 0f, 1f, 1f),
                new Keyframe(1f, 1f, 1f, 1f)
            };
            SetSmoothEditable(ref keys, KeyframeTangentUtility.TangentMode.Auto);
            return keys;
        }

        private static Keyframe[] GetEaseInKeys()
        {
            Keyframe[] keys = new Keyframe[]
            {
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(1f, 1f, 2f, 2f)
            };
            SetSmoothEditable(ref keys, KeyframeTangentUtility.TangentMode.Free);
            return keys;
        }

        private static Keyframe[] GetEaseOutKeys()
        {
            Keyframe[] keys = new Keyframe[]
            {
                new Keyframe(0f, 0f, 2f, 2f),
                new Keyframe(1f, 1f, 0f, 0f)
            };
            SetSmoothEditable(ref keys, KeyframeTangentUtility.TangentMode.Free);
            return keys;
        }

        private static Keyframe[] GetEaseInOutKeys()
        {
            Keyframe[] keys = new Keyframe[]
            {
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(1f, 1f, 0f, 0f)
            };
            SetSmoothEditable(ref keys, KeyframeTangentUtility.TangentMode.Free);
            return keys;
        }

        private static Keyframe[] GetConstantKeys(float value)
        {
            Keyframe[] keys = new Keyframe[]
            {
                new Keyframe(0f, value, 0f, 0f),
                new Keyframe(1f, value, 0f, 0f)
            };
            SetSmoothEditable(ref keys, KeyframeTangentUtility.TangentMode.Auto);
            return keys;
        }

        private static void SetSmoothEditable(ref Keyframe[] keys, KeyframeTangentUtility.TangentMode tangentMode)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                KeyframeTangentUtility.SetKeyBroken(ref keys[i], false);
                KeyframeTangentUtility.SetKeyLeftTangentMode(ref keys[i], tangentMode);
                KeyframeTangentUtility.SetKeyRightTangentMode(ref keys[i], tangentMode);
            }
        }

        private static void EnsureStyles()
        {
            if (s_PresetLabelStyle != null) return;

            s_PresetLabelStyle = new GUIStyle(GUI.skin.label);
            s_PresetLabelStyle.fontSize = 9;
            s_PresetLabelStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.9f);
            s_PresetLabelStyle.alignment = TextAnchor.LowerCenter;

            s_PresetButtonStyle = new GUIStyle(GUI.skin.button);
            s_PresetButtonStyle.padding = new RectOffset(1, 1, 1, 1);
            s_PresetButtonStyle.margin = new RectOffset(0, 0, 0, 0);
        }

        private static void EnsureDialogStyles()
        {
            if (s_SaveDialogLabelStyle != null) return;

            s_SaveDialogLabelStyle = new GUIStyle(GUI.skin.label);
            s_SaveDialogLabelStyle.fontSize = 12;
            s_SaveDialogLabelStyle.fontStyle = FontStyle.Bold;
            s_SaveDialogLabelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            s_SaveDialogLabelStyle.alignment = TextAnchor.MiddleLeft;

            s_SaveDialogFieldStyle = new GUIStyle(GUI.skin.textField);
            s_SaveDialogFieldStyle.fontSize = 11;

            s_SaveDialogButtonStyle = new GUIStyle(GUI.skin.button);
            s_SaveDialogButtonStyle.fontSize = 11;
        }
    }
}
