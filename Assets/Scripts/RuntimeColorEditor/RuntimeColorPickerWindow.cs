using System;
using UnityEngine;

namespace RuntimeColorEditor
{
    /// <summary>
    /// Runtime (IMGUI) version of the editor's color picker: hue ring + saturation/value box,
    /// RGB 0-255 / RGB 0-1 (0-Inf for HDR) / HSV sliders, alpha, hex (LDR) and, for HDR
    /// colors, the Intensity slider with the exposure swatches. Open it with
    /// <see cref="Show"/>; every edit is reported through the callback.
    ///
    /// Keys: Esc reverts to the original color and closes, Enter closes. Clicking the left
    /// (original) swatch reverts without closing.
    /// </summary>
    public class RuntimeColorPickerWindow : RuntimePopupWindow
    {
        private enum SliderMode
        {
            Rgb,
            RgbFloat,
            Hsv,
        }

        private const string SliderModePrefKey = "RuntimeColorPicker.SliderMode";
        private const string SliderModeHdrPrefKey = "RuntimeColorPicker.SliderModeHDR";
        private const float DefaultExposureSliderMax = 10f;
        private const int ColorBoxResolution = 32;
        private const int SliderResolution = 64;

        private static RuntimeColorPickerWindow s_Instance;

        private RuntimeColorMutator mutator;
        private bool hdr;
        private bool showAlpha;
        private bool dimBackground;
        private object owner;
        private Action<Color> onColorChanged;
        private SliderMode sliderMode;
        private float exposureSliderMax = DefaultExposureSliderMax;

        // Max of the RGB 0-Inf sliders, frozen while one of them is dragged so the range
        // doesn't move under the cursor (the editor does the same).
        private float frozenFloatSliderMax;
        private bool floatSliderActive;

        private Texture2D colorBox;
        private float colorBoxHue = -1f;
        private readonly Texture2D[] sliderTextures = new Texture2D[4];
        private readonly Vector4[] sliderTextureKeys = new Vector4[4];

        private static Texture2D s_HueRing;
        private static Texture2D s_HueRingHdr;
        private static Texture2D s_Circle;
        private static Texture2D s_CircleOutline;

        private static readonly int s_HueRingHash = "RuntimeColorHueRing".GetHashCode();
        private static readonly int s_ColorBoxHash = "RuntimeColorBox".GetHashCode();

        public static bool IsVisible => s_Instance != null && s_Instance.isVisible;

        private static RuntimeColorPickerWindow Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("[RuntimeColorPicker]");
                    DontDestroyOnLoad(go);
                    s_Instance = go.AddComponent<RuntimeColorPickerWindow>();
                }
                return s_Instance;
            }
        }

        /// <param name="color">The starting color (linear when <paramref name="hdr"/>).</param>
        /// <param name="onChanged">Called with the new color on every edit.</param>
        /// <param name="nextTo">Open beside this screen rect (another popup) instead of centred.</param>
        /// <param name="dimBackground">Dim the screen behind (off when stacked on another popup).</param>
        /// <param name="owner">Lets the opener close it again with <see cref="HideIfOwnedBy"/>.</param>
        public static void Show(
            Color color,
            bool hdr,
            bool showAlpha,
            Action<Color> onChanged,
            Rect? nextTo = null,
            bool dimBackground = true,
            object owner = null
        )
        {
            var w = Instance;
            w.hdr = hdr;
            w.showAlpha = showAlpha;
            w.dimBackground = dimBackground;
            w.owner = owner;
            w.onColorChanged = onChanged;
            w.mutator = new RuntimeColorMutator(color);
            if (!hdr)
                w.mutator.ExposureValue = 0f;
            w.exposureSliderMax = Mathf.Max(DefaultExposureSliderMax, w.mutator.ExposureValue);
            w.sliderMode = (SliderMode)
                Mathf.Clamp(
                    PlayerPrefs.GetInt(hdr ? SliderModeHdrPrefKey : SliderModePrefKey, 0),
                    0,
                    2
                );
            w.colorBoxHue = -1f;
            for (int i = 0; i < w.sliderTextureKeys.Length; i++)
                w.sliderTextureKeys[i] = new Vector4(-1f, -1f, -1f, -1f);
            w.isVisible = true;
            w.PlaceWindow(250f * RuntimeColorGUI.UIScale, nextTo);
        }

        public static void HideWindow()
        {
            if (s_Instance != null)
                s_Instance.Hide();
        }

        /// <summary>Closes the picker if <paramref name="owner"/> opened it.</summary>
        public static void HideIfOwnedBy(object owner)
        {
            if (s_Instance != null && s_Instance.isVisible && s_Instance.owner == owner)
                s_Instance.Hide();
        }

        public override void Hide()
        {
            if (isVisible)
                PlayerPrefs.SetInt(hdr ? SliderModeHdrPrefKey : SliderModePrefKey, (int)sliderMode);
            base.Hide();
            onColorChanged = null;
            owner = null;
        }

        protected override string Title => hdr ? "HDR Color" : "Color";

        protected override int Depth => -2;

        protected override bool DimBackground => dimBackground;

        protected override void OnEscape()
        {
            mutator.Reset();
            OnColorChanged();
            Hide();
        }

        private void OnDestroy()
        {
            RuntimeColorGUI.DestroyTexture(ref colorBox);
            for (int i = 0; i < sliderTextures.Length; i++)
                RuntimeColorGUI.DestroyTexture(ref sliderTextures[i]);
            if (s_Instance == this)
                s_Instance = null;
        }

        // ---- Layout ----

        private static float S => RuntimeColorGUI.UIScale;
        private static float RowHeight => 20f * S;
        private static float RowGap => 5f * S;
        private float InnerWidth => windowRect.width - 2f * Padding;

        protected override float CalcHeight()
        {
            float h = TitleBarHeight + Padding;
            h += 24f * S + 8f * S; // swatches
            h += (250f * S - 2f * Padding) + 8f * S; // hue ring
            h += RowHeight + 8f * S; // slider mode
            h += 3 * (RowHeight + RowGap); // channels
            if (showAlpha)
                h += RowHeight + RowGap;
            if (hdr)
                h += 4f * S + RowHeight + RowGap + 22f * S + RowGap; // intensity + swatches
            else
                h += 4f * S + RowHeight + RowGap; // hex
            return h + Padding - RowGap;
        }

        protected override void DrawContents(Rect contentRect)
        {
            if (mutator == null)
                return;

            float x = contentRect.x + Padding;
            float y = contentRect.y + Padding;
            float w = InnerWidth;

            DrawSwatches(new Rect(x, y, w, 24f * S));
            y += 24f * S + 8f * S;

            DrawHueRingAndBox(new Rect(x, y, w, w));
            y += w + 8f * S;

            DrawSliderModes(new Rect(x, y, w, RowHeight));
            y += RowHeight + 8f * S;

            y = DrawChannelSliders(x, y, w);

            y += 4f * S;
            if (hdr)
            {
                DrawIntensity(new Rect(x, y, w, RowHeight));
                y += RowHeight + RowGap;
                DrawExposureSwatches(new Rect(x, y, w, 22f * S));
            }
            else
            {
                DrawHex(new Rect(x, y, w, RowHeight));
            }
        }

        // ---- Original / current swatches ----

        private void DrawSwatches(Rect rect)
        {
            float half = Mathf.Floor(rect.width / 2f);
            Rect original = new Rect(rect.x, rect.y, half, rect.height);
            Rect current = new Rect(rect.x + half, rect.y, rect.width - half, rect.height);

            RuntimeColorGUI.DrawColorSwatch(original, mutator.OriginalColor, hdr, showAlpha);
            RuntimeColorGUI.DrawColorSwatch(current, mutator.ExposureAdjustedColor, hdr, showAlpha);

            // The original swatch resets the picker to the color it opened with.
            if (GUI.Button(original, GUIContent.none, GUIStyle.none))
            {
                mutator.Reset();
                OnColorChanged();
            }
        }

        // ---- Hue ring + saturation/value box ----

        private void DrawHueRingAndBox(Rect area)
        {
            float ringThickness = 16f * S;
            Vector2 center = area.center;
            float outerRadius = area.width / 2f;
            float innerRadius = outerRadius - ringThickness;

            float hue = mutator.GetChannel(HsvChannel.H);
            float sat = mutator.GetChannel(HsvChannel.S);
            float val = mutator.GetChannel(HsvChannel.V);

            // Hue ring
            int ringId = GUIUtility.GetControlID(s_HueRingHash, FocusType.Passive, area);
            Event e = Event.current;
            switch (e.GetTypeForControl(ringId))
            {
                case EventType.MouseDown:
                {
                    float dist = Vector2.Distance(e.mousePosition, center);
                    if (e.button == 0 && dist <= outerRadius && dist >= innerRadius - 2f * S)
                    {
                        GUIUtility.hotControl = ringId;
                        GUIUtility.keyboardControl = 0;
                        SetHueFromMouse(center, e.mousePosition);
                        e.Use();
                    }
                    break;
                }
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == ringId)
                    {
                        SetHueFromMouse(center, e.mousePosition);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == ringId)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                {
                    GUI.DrawTexture(area, HueRingTexture(hdr), ScaleMode.StretchToFill, true);
                    float angle = hue * Mathf.PI * 2f;
                    float mid = (outerRadius + innerRadius) / 2f;
                    Vector2 thumbCenter =
                        center + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * mid;
                    float thumbSize = ringThickness + 4f * S;
                    Rect thumb = new Rect(0, 0, thumbSize, thumbSize) { center = thumbCenter };
                    Color hueColor = RuntimeColorGUI.ToDisplay(Color.HSVToRGB(hue, 1f, 1f), hdr);
                    DrawCircle(thumb, hueColor, filled: true);
                    DrawCircle(thumb, Color.white, filled: false);
                    break;
                }
            }

            // Saturation (x) / value (y) box inscribed in the ring
            int side = Mathf.FloorToInt(Mathf.Sqrt(2f) * (innerRadius - 6f * S));
            Rect box = new Rect(0, 0, side, side) { center = center };
            int boxId = GUIUtility.GetControlID(s_ColorBoxHash, FocusType.Passive, box);
            switch (e.GetTypeForControl(boxId))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && box.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = boxId;
                        GUIUtility.keyboardControl = 0;
                        SetSatValFromMouse(box, e.mousePosition);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == boxId)
                    {
                        SetSatValFromMouse(box, e.mousePosition);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == boxId)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                {
                    UpdateColorBox(hue);
                    // Half-texel inset so the corners show the exact extreme colors
                    float texel = 1f / ColorBoxResolution;
                    GUI.DrawTextureWithTexCoords(
                        box,
                        colorBox,
                        new Rect(texel * 0.5f, texel * 0.5f, 1f - texel, 1f - texel)
                    );
                    RuntimeColorGUI.DrawBorder(box, RuntimeColorGUI.ControlBorder);

                    float thumbSize = 12f * S;
                    Rect thumb = new Rect(0, 0, thumbSize, thumbSize)
                    {
                        center = new Vector2(
                            box.x + sat * box.width,
                            box.y + (1f - val) * box.height
                        ),
                    };
                    Color current = RuntimeColorGUI.ToDisplay(mutator.BaseColor, hdr);
                    DrawCircle(thumb, RuntimeColorGUI.ContrastColor(current), filled: false);
                    break;
                }
            }
        }

        private void SetHueFromMouse(Vector2 center, Vector2 mouse)
        {
            Vector2 d = mouse - center;
            float hue = Mathf.Repeat(Mathf.Atan2(-d.y, d.x) / (Mathf.PI * 2f), 1f);
            mutator.SetChannel(HsvChannel.H, hue);
            OnColorChanged();
        }

        private void SetSatValFromMouse(Rect box, Vector2 mouse)
        {
            mutator.SetChannel(HsvChannel.S, Mathf.Clamp01((mouse.x - box.x) / box.width));
            mutator.SetChannel(HsvChannel.V, 1f - Mathf.Clamp01((mouse.y - box.y) / box.height));
            OnColorChanged();
        }

        private void UpdateColorBox(float hue)
        {
            if (colorBox != null && Mathf.Approximately(colorBoxHue, hue))
                return;
            if (colorBox == null)
                colorBox = RuntimeColorGUI.CreateTexture(ColorBoxResolution, ColorBoxResolution);
            colorBoxHue = hue;

            var pixels = new Color[ColorBoxResolution * ColorBoxResolution];
            float step = 1f / (ColorBoxResolution - 1);
            // Texture row 0 is the bottom: value rises upwards, saturation to the right.
            for (int y = 0; y < ColorBoxResolution; y++)
            for (int x = 0; x < ColorBoxResolution; x++)
                pixels[y * ColorBoxResolution + x] = RuntimeColorGUI.ToDisplay(
                    Color.HSVToRGB(hue, x * step, y * step),
                    hdr
                );
            colorBox.SetPixels(pixels);
            colorBox.Apply();
        }

        // ---- Sliders ----

        private void DrawSliderModes(Rect rect)
        {
            string floatLabel =
                hdr && mutator.ExposureAdjustedColor.maxColorComponent > 1f
                    ? "RGB 0-Inf"
                    : "RGB 0-1.0";
            sliderMode = (SliderMode)
                RuntimeColorGUI.Segmented(
                    rect,
                    (int)sliderMode,
                    new[] { "RGB 0-255", floatLabel, "HSV" }
                );
        }

        private float DrawChannelSliders(float x, float y, float w)
        {
            bool anyFloatActive = false;
            if (sliderMode == SliderMode.Hsv)
            {
                float h = mutator.GetChannel(HsvChannel.H);
                float s = mutator.GetChannel(HsvChannel.S);
                float v = mutator.GetChannel(HsvChannel.V);

                UpdateSliderTexture(0, new Vector4(1f, 1f, 0f, 0f), t => Color.HSVToRGB(t, 1f, 1f));
                UpdateSliderTexture(
                    1,
                    new Vector4(h, Mathf.Max(v, 0.2f), 1f, 0f),
                    t => Color.HSVToRGB(h, t, Mathf.Max(v, 0.2f))
                );
                UpdateSliderTexture(2, new Vector4(h, s, 2f, 0f), t => Color.HSVToRGB(h, s, t));

                float hv = h * 360f;
                if (
                    ChannelRow(
                        new Rect(x, y, w, RowHeight),
                        "H",
                        ref hv,
                        0f,
                        360f,
                        360f,
                        "0",
                        sliderTextures[0],
                        out _
                    )
                )
                {
                    mutator.SetChannel(HsvChannel.H, Mathf.Clamp(hv, 0f, 360f) / 360f);
                    OnColorChanged();
                }
                y += RowHeight + RowGap;
                float sv = s * 100f;
                if (
                    ChannelRow(
                        new Rect(x, y, w, RowHeight),
                        "S",
                        ref sv,
                        0f,
                        100f,
                        100f,
                        "0",
                        sliderTextures[1],
                        out _
                    )
                )
                {
                    mutator.SetChannel(HsvChannel.S, Mathf.Clamp(sv, 0f, 100f) / 100f);
                    OnColorChanged();
                }
                y += RowHeight + RowGap;
                float vv = v * 100f;
                if (
                    ChannelRow(
                        new Rect(x, y, w, RowHeight),
                        "V",
                        ref vv,
                        0f,
                        100f,
                        100f,
                        "0",
                        sliderTextures[2],
                        out _
                    )
                )
                {
                    mutator.SetChannel(HsvChannel.V, Mathf.Clamp(vv, 0f, 100f) / 100f);
                    OnColorChanged();
                }
                y += RowHeight + RowGap;
            }
            else
            {
                float r = mutator.GetChannelNormalized(RgbaChannel.R);
                float g = mutator.GetChannelNormalized(RgbaChannel.G);
                float b = mutator.GetChannelNormalized(RgbaChannel.B);
                UpdateSliderTexture(0, new Vector4(g, b, 3f, 0f), t => new Color(t, g, b));
                UpdateSliderTexture(1, new Vector4(r, b, 4f, 0f), t => new Color(r, t, b));
                UpdateSliderTexture(2, new Vector4(r, g, 5f, 0f), t => new Color(r, g, t));

                // RGB 0-1 / 0-Inf: slider range is the channel range at the current
                // intensity; the text field accepts anything >= 0 for HDR.
                Color hdrColor = mutator.ExposureAdjustedColor;
                float baseMax = ((Color)mutator.BaseColor).maxColorComponent;
                float liveMax =
                    hdr && hdrColor.maxColorComponent > 1f && baseMax > 0f
                        ? hdrColor.maxColorComponent / baseMax
                        : 1f;
                float floatMax = floatSliderActive ? frozenFloatSliderMax : liveMax;

                string[] labels = { "R", "G", "B" };
                for (int i = 0; i < 3; i++)
                {
                    var channel = (RgbaChannel)i;
                    Rect row = new Rect(x, y, w, RowHeight);
                    if (sliderMode == SliderMode.Rgb)
                    {
                        float value = mutator.GetChannel(channel);
                        if (
                            ChannelRow(
                                row,
                                labels[i],
                                ref value,
                                0f,
                                255f,
                                255f,
                                "0",
                                sliderTextures[i],
                                out _
                            )
                        )
                        {
                            mutator.SetChannelNormalized(channel, value / 255f);
                            OnColorChanged();
                        }
                    }
                    else
                    {
                        float value = mutator.GetChannelHdr(channel);
                        float fieldMax = hdr ? float.MaxValue : 1f;
                        if (
                            ChannelRow(
                                row,
                                labels[i],
                                ref value,
                                0f,
                                floatMax,
                                fieldMax,
                                "0.###",
                                sliderTextures[i],
                                out bool active
                            )
                        )
                        {
                            mutator.SetChannelHdr(channel, Mathf.Clamp(value, 0f, fieldMax));
                            OnColorChanged();
                        }
                        if (active)
                        {
                            if (!floatSliderActive)
                                frozenFloatSliderMax = liveMax;
                            anyFloatActive = true;
                        }
                    }
                    y += RowHeight + RowGap;
                }
            }
            floatSliderActive = anyFloatActive;

            if (showAlpha)
            {
                Color32 c = mutator.BaseColor;
                Color baseColor = c;
                UpdateSliderTexture(
                    3,
                    new Vector4(baseColor.r, baseColor.g, baseColor.b, 6f),
                    t => new Color(baseColor.r, baseColor.g, baseColor.b, t)
                );

                float scale =
                    sliderMode == SliderMode.Hsv ? 100f
                    : sliderMode == SliderMode.Rgb ? 255f
                    : 1f;
                string format = sliderMode == SliderMode.RgbFloat ? "0.###" : "0";
                float value = mutator.GetChannelNormalized(RgbaChannel.A) * scale;
                if (
                    ChannelRow(
                        new Rect(x, y, w, RowHeight),
                        "A",
                        ref value,
                        0f,
                        scale,
                        scale,
                        format,
                        sliderTextures[3],
                        out _,
                        checker: true
                    )
                )
                {
                    mutator.SetChannelNormalized(
                        RgbaChannel.A,
                        Mathf.Clamp(value, 0f, scale) / scale
                    );
                    OnColorChanged();
                }
                y += RowHeight + RowGap;
            }
            return y - RowGap;
        }

        /// <summary>Label + slider + number field. Returns true when the value was changed.</summary>
        private bool ChannelRow(
            Rect row,
            string label,
            ref float value,
            float sliderMin,
            float sliderMax,
            float fieldMax,
            string format,
            Texture2D background,
            out bool active,
            bool checker = false
        )
        {
            float labelWidth = 14f * S;
            float fieldWidth = 48f * S;
            float gap = 6f * S;
            GUI.Label(new Rect(row.x, row.y, labelWidth, row.height), label, RuntimeColorGUI.Label);

            float sliderHeight = 14f * S;
            Rect slider = new Rect(
                row.x + labelWidth,
                row.y + (row.height - sliderHeight) / 2f,
                row.width - labelWidth - gap - fieldWidth,
                sliderHeight
            );
            bool changed = RuntimeColorGUI.Slider(
                slider,
                ref value,
                sliderMin,
                sliderMax,
                background,
                out active,
                checker
            );

            float fieldValue = value;
            if (
                RuntimeColorGUI.FloatField(
                    new Rect(row.xMax - fieldWidth, row.y, fieldWidth, row.height),
                    "RuntimeColorPicker." + label,
                    ref fieldValue,
                    format
                )
            )
            {
                value = Mathf.Clamp(fieldValue, 0f, fieldMax);
                changed = true;
            }
            return changed;
        }

        private void UpdateSliderTexture(int index, Vector4 key, Func<float, Color> colorAt)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            if (sliderTextures[index] != null && sliderTextureKeys[index] == key)
                return;
            if (sliderTextures[index] == null)
                sliderTextures[index] = RuntimeColorGUI.CreateTexture(SliderResolution, 1);
            sliderTextureKeys[index] = key;

            var pixels = new Color[SliderResolution];
            for (int i = 0; i < SliderResolution; i++)
            {
                Color c = colorAt(i / (float)(SliderResolution - 1));
                Color display = RuntimeColorGUI.ToDisplay(c, hdr);
                display.a = c.a;
                pixels[i] = display;
            }
            sliderTextures[index].SetPixels(pixels);
            sliderTextures[index].Apply();
        }

        // ---- HDR intensity / LDR hex ----

        private void DrawIntensity(Rect row)
        {
            float labelWidth = 56f * S;
            float fieldWidth = 48f * S;
            float gap = 6f * S;
            GUI.Label(
                new Rect(row.x, row.y, labelWidth, row.height),
                "Intensity",
                RuntimeColorGUI.Label
            );

            float sliderHeight = 14f * S;
            Rect slider = new Rect(
                row.x + labelWidth,
                row.y + (row.height - sliderHeight) / 2f,
                row.width - labelWidth - gap - fieldWidth,
                sliderHeight
            );
            float exposure = mutator.ExposureValue;
            bool changed = RuntimeColorGUI.Slider(
                slider,
                ref exposure,
                -exposureSliderMax,
                exposureSliderMax,
                null,
                out _
            );
            if (Event.current.type == EventType.Repaint)
            {
                // Centre tick at 0 stops
                RuntimeColorGUI.DrawRect(
                    new Rect(slider.center.x, slider.y, 1f, slider.height),
                    new Color(1f, 1f, 1f, 0.25f)
                );
            }
            changed |= RuntimeColorGUI.FloatField(
                new Rect(row.xMax - fieldWidth, row.y, fieldWidth, row.height),
                "RuntimeColorPicker.Intensity",
                ref exposure,
                "0.##"
            );
            if (changed)
            {
                mutator.ExposureValue = exposure;
                OnColorChanged();
            }
        }

        private void DrawExposureSwatches(Rect rect)
        {
            const int count = 5;
            float swatchWidth = 32f * S;
            float x = rect.x + (rect.width - count * swatchWidth) / 2f;
            for (int i = 0; i < count; i++)
            {
                int stops = i - count / 2;
                Rect r = new Rect(x + i * swatchWidth, rect.y, swatchWidth, rect.height);
                Color display = RuntimeColorGUI.ToDisplay(
                    mutator.ExposureAdjustedColor * Mathf.Pow(2f, stops),
                    true
                );
                display.a = 1f;

                RuntimeColorGUI.DrawRect(r, display);
                RuntimeColorGUI.DrawBorder(
                    r,
                    stops == 0
                        ? RuntimeColorGUI.ContrastColor(display)
                        : RuntimeColorGUI.ControlBorder,
                    stops == 0 ? 2f : 1f
                );
                if (stops != 0)
                {
                    Color previous = GUI.contentColor;
                    GUI.contentColor = RuntimeColorGUI.ContrastColor(display);
                    GUI.Label(
                        r,
                        stops > 0 ? $"+{stops}" : stops.ToString(),
                        RuntimeColorGUI.MiniCenteredLabel
                    );
                    GUI.contentColor = previous;
                }
                if (stops != 0 && GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    mutator.ExposureValue = Mathf.Clamp(
                        mutator.ExposureValue + stops,
                        -exposureSliderMax,
                        exposureSliderMax
                    );
                    OnColorChanged();
                }
            }
        }

        private void DrawHex(Rect row)
        {
            float fieldWidth = 85f * S;
            GUI.Label(
                new Rect(row.x, row.y, row.width - fieldWidth, row.height),
                "Hexadecimal",
                RuntimeColorGUI.Label
            );
            Color32 c = mutator.BaseColor;
            if (
                RuntimeColorGUI.HexField(
                    new Rect(row.xMax - fieldWidth, row.y, fieldWidth, row.height),
                    "RuntimeColorPicker.Hex",
                    ref c,
                    withAlpha: false
                )
            )
            {
                mutator.SetChannel(RgbaChannel.R, c.r);
                mutator.SetChannel(RgbaChannel.G, c.g);
                mutator.SetChannel(RgbaChannel.B, c.b);
                OnColorChanged();
            }
        }

        private void OnColorChanged()
        {
            exposureSliderMax = Mathf.Max(exposureSliderMax, mutator.ExposureValue);
            onColorChanged?.Invoke(mutator.ExposureAdjustedColor);
        }

        // ---- Generated textures ----

        private static Texture2D HueRingTexture(bool hdr)
        {
            ref Texture2D tex = ref (hdr ? ref s_HueRingHdr : ref s_HueRing);
            if (tex != null)
                return tex;

            const int size = 256;
            tex = RuntimeColorGUI.CreateTexture(size, size);
            // Ring thickness relative to the radius; matches the drawn 16px ring at 250px width.
            float outer = size / 2f;
            float inner = outer * (1f - 16f / ((250f - 20f) / 2f));
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - outer;
                float dy = y + 0.5f - outer; // row 0 is the bottom, so dy grows upwards
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(outer - dist) * Mathf.Clamp01(dist - inner);
                float hue = Mathf.Repeat(Mathf.Atan2(dy, dx) / (Mathf.PI * 2f), 1f);
                Color c = RuntimeColorGUI.ToDisplay(Color.HSVToRGB(hue, 1f, 1f), hdr);
                c.a = alpha;
                pixels[y * size + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static void DrawCircle(Rect rect, Color color, bool filled)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            ref Texture2D tex = ref (filled ? ref s_Circle : ref s_CircleOutline);
            if (tex == null)
            {
                const int size = 64;
                tex = RuntimeColorGUI.CreateTexture(size, size);
                float r = size / 2f;
                float thickness = size * 0.12f;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(r - dist);
                    if (!filled)
                        alpha *= Mathf.Clamp01(dist - (r - thickness));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
                tex.SetPixels(pixels);
                tex.Apply();
            }
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }
    }
}
