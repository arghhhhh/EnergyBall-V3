using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeColorEditor
{
    /// <summary>
    /// Runtime (IMGUI) version of the editor's gradient editor: blend mode, alpha keys above
    /// the bar, color keys below it, and the selected key's color / alpha and location.
    /// Editing works like the editor: click a key bar to add a key, drag to move it, drag it off
    /// the bar (or press Delete) to remove it, double-click a color key (or click the Color
    /// swatch) to open the color picker. Max 8 color and 8 alpha keys.
    ///
    /// The gradient passed to <see cref="Show"/> is edited in place; the callback fires after
    /// every change.
    /// </summary>
    public class RuntimeGradientEditorWindow : RuntimePopupWindow
    {
        private class Swatch
        {
            public float time;
            public Color value;
            public bool isAlpha;

            public Swatch(float time, Color value, bool isAlpha)
            {
                this.time = time;
                this.value = value;
                this.isAlpha = isAlpha;
            }
        }

        private const int MaxKeys = 8;
        private const int PreviewResolution = 256;

        private static readonly string[] ModeLabels =
        {
            "Blend (Classic)",
            "Blend (Perceptual)",
            "Fixed",
        };
        private static readonly GradientMode[] ModeValues =
        {
            GradientMode.Blend,
            GradientMode.PerceptualBlend,
            GradientMode.Fixed,
        };
        private static readonly int s_SwatchArrayHash = "RuntimeGradientSwatches".GetHashCode();

        private static RuntimeGradientEditorWindow s_Instance;

        private Gradient gradient;
        private bool hdr;
        private Action<Gradient> onGradientChanged;
        private List<Swatch> rgbSwatches = new();
        private List<Swatch> alphaSwatches = new();
        private GradientMode mode;
        private Swatch selected;
        private bool doubleClickDetected;

        private Texture2D preview;
        private bool previewDirty = true;
        private Texture2D alphaSliderTexture;

        public static bool IsVisible => s_Instance != null && s_Instance.isVisible;

        private static RuntimeGradientEditorWindow Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("[RuntimeGradientEditor]");
                    DontDestroyOnLoad(go);
                    s_Instance = go.AddComponent<RuntimeGradientEditorWindow>();
                }
                return s_Instance;
            }
        }

        /// <param name="gradient">Edited in place.</param>
        /// <param name="hdr">Color keys may go above 1 (HDR color picker with Intensity).</param>
        /// <param name="onChanged">Called with <paramref name="gradient"/> after every edit.</param>
        public static void Show(Gradient gradient, bool hdr, Action<Gradient> onChanged)
        {
            if (gradient == null)
                return;
            var w = Instance;
            RuntimeColorPickerWindow.HideIfOwnedBy(w);
            w.gradient = gradient;
            w.hdr = hdr;
            w.onGradientChanged = onChanged;
            w.doubleClickDetected = false;
            w.BuildArrays();
            w.selected = w.rgbSwatches.Count > 0 ? w.rgbSwatches[0] : null;
            w.previewDirty = true;
            w.isVisible = true;
            w.PlaceWindow(440f * RuntimeColorGUI.UIScale, null);
        }

        public static void HideWindow()
        {
            if (s_Instance != null)
                s_Instance.Hide();
        }

        public override void Hide()
        {
            RuntimeColorPickerWindow.HideIfOwnedBy(this);
            base.Hide();
            onGradientChanged = null;
            gradient = null;
            selected = null;
        }

        protected override string Title => hdr ? "HDR Gradient Editor" : "Gradient Editor";

        protected override int Depth => -1;

        protected override bool InputBlocked => RuntimeColorPickerWindow.IsVisible;

        protected override void OnEscape() => Hide();

        private void OnDestroy()
        {
            RuntimeColorGUI.DestroyTexture(ref preview);
            RuntimeColorGUI.DestroyTexture(ref alphaSliderTexture);
            if (s_Instance == this)
                s_Instance = null;
        }

        // ---- Layout ----

        private static float S => RuntimeColorGUI.UIScale;
        private static float RowHeight => 20f * S;
        private static float SwatchRowHeight => 16f * S;
        private static float SwatchWidth => 10f * S;
        private static float PreviewHeight => 40f * S;

        protected override float CalcHeight()
        {
            return TitleBarHeight
                + Padding
                + RowHeight // mode
                + 8f * S
                + SwatchRowHeight
                + PreviewHeight
                + SwatchRowHeight
                + 10f * S
                + RowHeight // selected key
                + 8f * S
                + 28f * S // hint (two lines)
                + Padding;
        }

        protected override void DrawContents(Rect contentRect)
        {
            if (gradient == null)
                return;

            float x = contentRect.x + Padding;
            float y = contentRect.y + Padding;
            float w = contentRect.width - 2f * Padding;

            DrawModeRow(new Rect(x, y, w, RowHeight));
            y += RowHeight + 8f * S;

            // Keys sit centred on their time, so the bars are inset by half a swatch.
            float barX = x + SwatchWidth / 2f;
            float barWidth = w - SwatchWidth;

            ShowSwatchArray(new Rect(barX, y, barWidth, SwatchRowHeight), alphaSwatches, true);
            y += SwatchRowHeight;
            DrawPreview(new Rect(barX, y, barWidth, PreviewHeight));
            y += PreviewHeight;
            ShowSwatchArray(new Rect(barX, y, barWidth, SwatchRowHeight), rgbSwatches, false);
            y += SwatchRowHeight + 10f * S;

            if (selected != null)
                DrawSelectedKeyRow(new Rect(x, y, w, RowHeight));
            y += RowHeight + 8f * S;

            GUI.Label(
                new Rect(x, y, w, 28f * S),
                "Click a bar to add a key, drag it to move it. Drag a key off the bar or press Delete to remove it. Double-click a color key to edit its color.",
                RuntimeColorGUI.MiniLabel
            );

            HandleDeleteKey();
        }

        private void DrawModeRow(Rect row)
        {
            float labelWidth = 50f * S;
            GUI.Label(
                new Rect(row.x, row.y, labelWidth, row.height),
                "Mode",
                RuntimeColorGUI.Label
            );
            int current = Array.IndexOf(ModeValues, mode);
            int next = RuntimeColorGUI.Segmented(
                new Rect(row.x + labelWidth, row.y, row.width - labelWidth, row.height),
                Mathf.Max(0, current),
                ModeLabels
            );
            if (ModeValues[next] != mode)
            {
                mode = ModeValues[next];
                AssignBack();
            }
        }

        private void DrawPreview(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            UpdatePreview();
            RuntimeColorGUI.DrawChecker(rect);
            GUI.DrawTexture(rect, preview, ScaleMode.StretchToFill, true);
            RuntimeColorGUI.DrawBorder(rect, RuntimeColorGUI.ControlBorder);
            if (MaxColorComponent(gradient) > 1f)
                GUI.Label(
                    new Rect(rect.x, rect.y, rect.width - 4f * S, rect.height),
                    "HDR",
                    HdrLabelStyle
                );
        }

        private static GUIStyle s_HdrLabel;

        private static GUIStyle HdrLabelStyle
        {
            get
            {
                if (
                    s_HdrLabel == null
                    || s_HdrLabel.fontSize != RuntimeColorGUI.MiniCenteredLabel.fontSize
                )
                {
                    s_HdrLabel = new GUIStyle(RuntimeColorGUI.MiniCenteredLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                    };
                    s_HdrLabel.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                }
                return s_HdrLabel;
            }
        }

        private void UpdatePreview()
        {
            if (preview != null && !previewDirty)
                return;
            if (preview == null)
                preview = RuntimeColorGUI.CreateTexture(PreviewResolution, PreviewRows);
            previewDirty = false;
            preview.SetPixels(RenderGradientPixels(gradient, PreviewResolution));
            preview.Apply();
        }

        /// <summary>Rows in the editor's gradient preview texture.</summary>
        public const int PreviewRows = 4;

        /// <summary>
        /// Display pixels for a gradient, laid out like the editor's preview texture
        /// (<paramref name="width"/> x <see cref="PreviewRows"/>, row 0 at the bottom). The two
        /// middle rows show the color clamped; the two outer rows show an HDR color divided by
        /// its brightest channel, so a strip brighter than 1 still shows its hue. Drawn with
        /// bilinear filtering, the strip fades from the hue at its edges to the clamped color in
        /// the middle. LDR colors are the same in every row. Gamma-encoded only for a gradient
        /// marked as linear.
        /// </summary>
        public static Color[] RenderGradientPixels(Gradient gradient, int width)
        {
            var pixels = new Color[width * PreviewRows];
            bool linear = gradient.colorSpace == ColorSpace.Linear;
            for (int i = 0; i < width; i++)
            {
                Color c = gradient.Evaluate(i / (float)(width - 1));
                float alpha = Mathf.Clamp01(c.a);

                Color clamped = RuntimeColorGUI.ToDisplay(c, linear);
                clamped.a = alpha;

                float max = c.maxColorComponent;
                Color hue = max > 1f ? new Color(c.r / max, c.g / max, c.b / max, c.a) : c;
                hue = RuntimeColorGUI.ToDisplay(hue, linear);
                hue.a = alpha;

                pixels[i] = hue;
                pixels[width + i] = clamped;
                pixels[2 * width + i] = clamped;
                pixels[3 * width + i] = hue;
            }
            return pixels;
        }

        public static float MaxColorComponent(Gradient gradient)
        {
            float max = 0f;
            foreach (var key in gradient.colorKeys)
                max = Mathf.Max(max, key.color.maxColorComponent);
            return max;
        }

        // ---- Selected key ----

        private void DrawSelectedKeyRow(Rect row)
        {
            float locationLabelWidth = 56f * S;
            float locationFieldWidth = 50f * S;
            float percentWidth = 16f * S;
            float gap = 16f * S;
            float leftWidth =
                row.width - gap - locationLabelWidth - locationFieldWidth - percentWidth;
            float labelWidth = 44f * S;

            Rect left = new Rect(row.x, row.y, leftWidth, row.height);
            if (selected.isAlpha)
            {
                GUI.Label(
                    new Rect(left.x, left.y, labelWidth, left.height),
                    "Alpha",
                    RuntimeColorGUI.Label
                );
                float fieldWidth = 40f * S;
                float sliderHeight = 14f * S;
                Rect slider = new Rect(
                    left.x + labelWidth,
                    left.y + (left.height - sliderHeight) / 2f,
                    left.width - labelWidth - 6f * S - fieldWidth,
                    sliderHeight
                );
                float alpha = Mathf.Round(selected.value.r * 255f);
                bool changed = RuntimeColorGUI.Slider(
                    slider,
                    ref alpha,
                    0f,
                    255f,
                    AlphaSliderTexture(),
                    out _
                );
                changed |= RuntimeColorGUI.FloatField(
                    new Rect(left.xMax - fieldWidth, left.y, fieldWidth, left.height),
                    "RuntimeGradient.Alpha",
                    ref alpha,
                    "0"
                );
                if (changed)
                {
                    float a = Mathf.Clamp01(Mathf.Round(alpha) / 255f);
                    selected.value = new Color(a, a, a, 1f);
                    AssignBack();
                }
            }
            else
            {
                GUI.Label(
                    new Rect(left.x, left.y, labelWidth, left.height),
                    "Color",
                    RuntimeColorGUI.Label
                );
                Rect swatch = new Rect(
                    left.x + labelWidth,
                    left.y + 1f,
                    left.width - labelWidth,
                    left.height - 2f
                );
                RuntimeColorGUI.DrawColorSwatch(swatch, selected.value, hdr, false);
                if (GUI.Button(swatch, GUIContent.none, GUIStyle.none))
                    OpenColorPicker();
            }

            float lx = row.x + leftWidth + gap;
            GUI.Label(
                new Rect(lx, row.y, locationLabelWidth, row.height),
                "Location",
                RuntimeColorGUI.Label
            );
            float percent = selected.time * 100f;
            if (
                RuntimeColorGUI.FloatField(
                    new Rect(lx + locationLabelWidth, row.y, locationFieldWidth, row.height),
                    "RuntimeGradient.Location",
                    ref percent,
                    "0.0"
                )
            )
            {
                selected.time = Mathf.Clamp01(percent / 100f);
                AssignBack();
            }
            GUI.Label(
                new Rect(
                    lx + locationLabelWidth + locationFieldWidth + 3f * S,
                    row.y,
                    percentWidth,
                    row.height
                ),
                "%",
                RuntimeColorGUI.Label
            );
        }

        private Texture2D AlphaSliderTexture()
        {
            if (alphaSliderTexture == null)
            {
                alphaSliderTexture = RuntimeColorGUI.CreateTexture(2, 1);
                alphaSliderTexture.SetPixels(new[] { Color.black, Color.white });
                alphaSliderTexture.Apply();
            }
            return alphaSliderTexture;
        }

        private void OpenColorPicker()
        {
            if (selected == null || selected.isAlpha)
                return;
            Swatch editing = selected;
            RuntimeColorPickerWindow.Show(
                editing.value,
                hdr,
                showAlpha: false,
                onChanged: c =>
                {
                    c.a = 1f;
                    editing.value = c;
                    AssignBack();
                },
                nextTo: windowRect,
                dimBackground: false,
                owner: this
            );
        }

        private void HandleDeleteKey()
        {
            Event e = Event.current;
            if (
                e.type != EventType.KeyDown
                || (e.keyCode != KeyCode.Delete && e.keyCode != KeyCode.Backspace)
                || RuntimeColorGUI.IsEditingText
                || InputBlocked
                || selected == null
            )
                return;
            var list = selected.isAlpha ? alphaSwatches : rgbSwatches;
            if (list.Count > 1)
            {
                list.Remove(selected);
                selected = list[0];
                AssignBack();
            }
            e.Use();
        }

        // ---- Key bars ----

        private void ShowSwatchArray(Rect rect, List<Swatch> swatches, bool isAlpha)
        {
            int id = GUIUtility.GetControlID(s_SwatchArrayHash, FocusType.Passive, rect);
            Event e = Event.current;
            // The color picker on top owns input (the ID above is still taken, keeping IDs stable)
            if (InputBlocked && e.type != EventType.Repaint)
                return;
            float time = Mathf.Clamp01((e.mousePosition.x - rect.x) / rect.width);
            Vector2 point = new Vector2(rect.x + time * rect.width, e.mousePosition.y);

            switch (e.GetTypeForControl(id))
            {
                case EventType.Repaint:
                    foreach (var swatch in swatches)
                        if (swatch != selected)
                            DrawSwatch(rect, swatch, isAlpha);
                    if (selected != null && swatches.Contains(selected))
                        DrawSwatch(rect, selected, isAlpha);
                    break;

                case EventType.MouseDown:
                {
                    Rect hitRect = rect;
                    hitRect.xMin -= SwatchWidth;
                    hitRect.xMax += SwatchWidth;
                    if (e.button != 0 || !hitRect.Contains(e.mousePosition))
                        break;

                    GUIUtility.hotControl = id;
                    RuntimeColorGUI.EndTextEditing();
                    e.Use();

                    // Second click on the selected color key opens the picker (on mouse up)
                    if (
                        selected != null
                        && swatches.Contains(selected)
                        && !selected.isAlpha
                        && SwatchRect(rect, selected).Contains(e.mousePosition)
                    )
                    {
                        if (e.clickCount == 2)
                            doubleClickDetected = true;
                        break;
                    }

                    foreach (var swatch in swatches)
                    {
                        if (
                            SwatchRect(rect, swatch).Contains(point)
                            || SwatchRect(rect, swatch).Contains(e.mousePosition)
                        )
                        {
                            selected = swatch;
                            return;
                        }
                    }

                    if (swatches.Count < MaxKeys)
                    {
                        Color value = gradient.Evaluate(time);
                        value = isAlpha
                            ? new Color(value.a, value.a, value.a, 1f)
                            : new Color(value.r, value.g, value.b, 1f);
                        selected = new Swatch(time, value, isAlpha);
                        swatches.Add(selected);
                        AssignBack();
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"Max {MaxKeys} color keys and {MaxKeys} alpha keys are allowed in a gradient."
                        );
                    }
                    break;
                }

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id || selected == null)
                        break;
                    e.Use();
                    // Dragging well above/below the bar takes the key out (back in when you return)
                    float margin = 5f * S;
                    if (
                        e.mousePosition.y + margin < rect.y
                        || e.mousePosition.y - margin > rect.yMax
                    )
                    {
                        if (swatches.Count > 1 && swatches.Contains(selected))
                        {
                            swatches.Remove(selected);
                            AssignBack();
                        }
                        break;
                    }
                    if (!swatches.Contains(selected))
                        swatches.Add(selected);
                    selected.time = time;
                    AssignBack();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        if (!swatches.Contains(selected))
                            selected = swatches.Count > 0 ? swatches[0] : null;
                        RemoveDuplicateOverlappingSwatches();
                        if (doubleClickDetected)
                        {
                            doubleClickDetected = false;
                            OpenColorPicker();
                        }
                    }
                    break;
            }
        }

        private Rect SwatchRect(Rect bar, Swatch swatch)
        {
            return new Rect(
                bar.x + Mathf.Round(bar.width * swatch.time) - SwatchWidth / 2f,
                bar.y,
                SwatchWidth,
                bar.height
            );
        }

        private void DrawSwatch(Rect bar, Swatch swatch, bool isAlpha)
        {
            Rect r = SwatchRect(bar, swatch);
            // A small pointer towards the bar: alpha keys point down, color keys point up.
            float tip = 4f * S;
            Rect body = isAlpha
                ? new Rect(r.x, r.y, r.width, r.height - tip)
                : new Rect(r.x, r.y + tip, r.width, r.height - tip);
            Rect stem = isAlpha
                ? new Rect(r.center.x - S, body.yMax, 2f * S, tip)
                : new Rect(r.center.x - S, r.y, 2f * S, tip);

            bool isSelected = swatch == selected;
            Color outline = isSelected
                ? RuntimeColorGUI.SelectionColor
                : new Color(0.05f, 0.05f, 0.05f, 1f);
            Color fill = isAlpha
                ? swatch.value
                : RuntimeColorGUI.ToDisplay(swatch.value, gradient.colorSpace == ColorSpace.Linear);
            fill.a = 1f;

            RuntimeColorGUI.DrawRect(stem, outline);
            RuntimeColorGUI.DrawRect(body, fill);
            RuntimeColorGUI.DrawBorder(body, outline, isSelected ? 2f : 1f);
            if (isSelected)
                RuntimeColorGUI.DrawBorder(
                    new Rect(body.x + 2f, body.y + 2f, body.width - 4f, body.height - 4f),
                    new Color(1f, 1f, 1f, 0.6f)
                );
        }

        // ---- Gradient <-> swatches ----

        private void BuildArrays()
        {
            rgbSwatches = new List<Swatch>();
            foreach (var key in gradient.colorKeys)
            {
                Color c = key.color;
                c.a = 1f;
                rgbSwatches.Add(new Swatch(key.time, c, false));
            }
            alphaSwatches = new List<Swatch>();
            foreach (var key in gradient.alphaKeys)
                alphaSwatches.Add(
                    new Swatch(key.time, new Color(key.alpha, key.alpha, key.alpha, 1f), true)
                );
            mode = gradient.mode;
        }

        private int SwatchSort(Swatch lhs, Swatch rhs)
        {
            // Same time: the selected key sorts first, so it stays in place while dragged over another
            if (lhs.time == rhs.time && lhs == selected)
                return -1;
            if (lhs.time == rhs.time && rhs == selected)
                return 1;
            return lhs.time.CompareTo(rhs.time);
        }

        private void AssignBack()
        {
            rgbSwatches.Sort(SwatchSort);
            alphaSwatches.Sort(SwatchSort);

            var colorKeys = new GradientColorKey[rgbSwatches.Count];
            for (int i = 0; i < rgbSwatches.Count; i++)
                colorKeys[i] = new GradientColorKey(rgbSwatches[i].value, rgbSwatches[i].time);
            var alphaKeys = new GradientAlphaKey[alphaSwatches.Count];
            for (int i = 0; i < alphaSwatches.Count; i++)
                alphaKeys[i] = new GradientAlphaKey(
                    alphaSwatches[i].value.r,
                    alphaSwatches[i].time
                );

            gradient.colorKeys = colorKeys;
            gradient.alphaKeys = alphaKeys;
            gradient.mode = mode;
            previewDirty = true;
            onGradientChanged?.Invoke(gradient);
        }

        private void RemoveDuplicateOverlappingSwatches()
        {
            bool removed = RemoveDuplicates(rgbSwatches) | RemoveDuplicates(alphaSwatches);
            if (removed)
                AssignBack();
        }

        private bool RemoveDuplicates(List<Swatch> swatches)
        {
            bool removed = false;
            for (int i = 1; i < swatches.Count; i++)
            {
                if (Mathf.Approximately(swatches[i - 1].time, swatches[i].time))
                {
                    // The later one in sort order loses; the selected key sorts first at equal time
                    if (swatches[i] == selected)
                        selected = swatches[i - 1];
                    swatches.RemoveAt(i);
                    i--;
                    removed = true;
                }
            }
            return removed;
        }
    }
}
