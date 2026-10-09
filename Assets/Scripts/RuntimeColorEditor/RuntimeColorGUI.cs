using System.Globalization;
using UnityEngine;

namespace RuntimeColorEditor
{
    /// <summary>
    /// Shared IMGUI drawing and controls for the runtime color picker and gradient editor.
    /// Everything is drawn in screen coordinates and scaled by <see cref="UIScale"/>, like the
    /// runtime curve editor.
    /// </summary>
    public static class RuntimeColorGUI
    {
        // UI dimensions scale with screen height (baseline 1080p), same as the curve editor
        public static float UIScale => Mathf.Max(1f, Screen.height / 1080f);

        public static readonly Color WindowBackground = new Color(0.16f, 0.16f, 0.16f, 1f);
        public static readonly Color TitleBackground = new Color(0.18f, 0.18f, 0.18f, 1f);
        public static readonly Color WindowBorder = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        public static readonly Color ControlBorder = new Color(0.08f, 0.08f, 0.08f, 1f);
        public static readonly Color SelectionColor = new Color32(57, 121, 187, 255);
        public static readonly Color TextColor = new Color(0.8f, 0.8f, 0.8f, 1f);

        // ---- Textures ----

        private static Texture2D s_Checker;

        /// <summary>2x2 grey checkerboard, repeated to show transparency behind colors.</summary>
        public static Texture2D Checker
        {
            get
            {
                if (s_Checker == null)
                {
                    s_Checker = CreateTexture(2, 2);
                    s_Checker.wrapMode = TextureWrapMode.Repeat;
                    s_Checker.filterMode = FilterMode.Point;
                    Color light = Color.white;
                    Color dark = new Color(0.7f, 0.7f, 0.7f, 1f);
                    s_Checker.SetPixels(new[] { light, dark, dark, light });
                    s_Checker.Apply();
                }
                return s_Checker;
            }
        }

        /// <summary>
        /// An sRGB texture: pixels hold display values, so a color drawn from it looks the same
        /// as the same color in the UI Toolkit menu.
        /// </summary>
        public static Texture2D CreateTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        public static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
                Object.Destroy(texture);
            texture = null;
        }

        /// <summary>
        /// The on-screen value of a stored color. HDR colors are linear (the editor's
        /// convention), so they're gamma-encoded; LDR colors already are display values.
        /// </summary>
        public static Color ToDisplay(Color color, bool hdr)
        {
            Color c = hdr ? color.gamma : color;
            return new Color(
                Mathf.Clamp01(c.r),
                Mathf.Clamp01(c.g),
                Mathf.Clamp01(c.b),
                Mathf.Clamp01(c.a)
            );
        }

        public static float PerceivedLuminance(Color c) =>
            0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        /// <summary>Black or white, whichever reads better on top of <paramref name="background"/>.</summary>
        public static Color ContrastColor(Color background) =>
            PerceivedLuminance(background) > 0.5f ? Color.black : Color.white;

        // ---- Drawing ----

        public static void DrawRect(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public static void DrawBorder(Rect rect, Color color, float thickness = 1f)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        public static void DrawChecker(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            float cell = 4f * UIScale;
            GUI.DrawTextureWithTexCoords(
                rect,
                Checker,
                new Rect(0f, 0f, rect.width / (2f * cell), rect.height / (2f * cell))
            );
        }

        /// <summary>
        /// A color swatch: the display color over a checkerboard when transparent, with an
        /// alpha bar along the bottom when <paramref name="showAlpha"/> (like the inspector).
        /// </summary>
        public static void DrawColorSwatch(Rect rect, Color color, bool hdr, bool showAlpha)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            Color display = ToDisplay(color, hdr);
            if (display.a < 1f)
                DrawChecker(rect);
            float fillAlpha = showAlpha ? display.a : 1f;
            DrawRect(rect, new Color(display.r, display.g, display.b, fillAlpha));

            // HDR with intensity: the outer thirds fade from the base color to the full color
            if (TryGetHdrSwatchBase(color, hdr, out Color baseDisplay))
            {
                float third = rect.width / 3f;
                Rect left = new Rect(rect.x, rect.y, third, rect.height);
                Rect right = new Rect(rect.xMax - third, rect.y, third, rect.height);
                Color baseFill = new Color(baseDisplay.r, baseDisplay.g, baseDisplay.b, fillAlpha);
                DrawRect(left, baseFill);
                DrawRect(right, baseFill);
                Color previous = GUI.color;
                GUI.color = new Color(display.r, display.g, display.b, fillAlpha);
                // Texel centres of the 2px ramp, so it runs exactly from 0 to 1
                GUI.DrawTextureWithTexCoords(left, AlphaRamp, new Rect(0.25f, 0f, 0.5f, 1f));
                GUI.DrawTextureWithTexCoords(right, AlphaRamp, new Rect(0.75f, 0f, -0.5f, 1f));
                GUI.color = previous;
            }

            if (showAlpha)
            {
                float barHeight = Mathf.Max(2f, Mathf.Round(rect.height * 0.15f));
                Rect bar = new Rect(rect.x, rect.yMax - barHeight, rect.width, barHeight);
                DrawRect(bar, Color.black);
                DrawRect(new Rect(bar.x, bar.y, bar.width * display.a, bar.height), Color.white);
            }

            if (hdr)
            {
                EnsureStyles();
                Color previous = GUI.contentColor;
                GUI.contentColor = ContrastColor(display) * new Color(1f, 1f, 1f, 0.7f);
                GUI.Label(rect, "HDR", s_MiniCenteredLabel);
                GUI.contentColor = previous;
            }
            DrawBorder(rect, ControlBorder);
        }

        /// <summary>
        /// The editor's HDR swatch shows a color with intensity as its base color (intensity
        /// removed, as the picker decomposes it) at both ends, fading to the full color over
        /// the outer thirds. Returns false when there's nothing to fade (LDR, or no intensity).
        /// </summary>
        public static bool TryGetHdrSwatchBase(Color color, bool hdr, out Color baseDisplay)
        {
            baseDisplay = default;
            if (!hdr)
                return false;
            RuntimeColorMutator.DecomposeHdrColor(color, out Color32 baseColor, out float exposure);
            if (Mathf.Approximately(exposure, 0f))
                return false;
            baseDisplay = ToDisplay(baseColor, true);
            return true;
        }

        /// <summary>Display color at <paramref name="u"/> (0-1) across an HDR swatch.</summary>
        public static Color SwatchColorAt(Color color, bool hdr, float u)
        {
            Color display = ToDisplay(color, hdr);
            if (!TryGetHdrSwatchBase(color, hdr, out Color baseDisplay))
                return display;
            float t = Mathf.Clamp01(Mathf.Min(u, 1f - u) * 3f);
            Color c = Color.Lerp(baseDisplay, display, t);
            c.a = display.a;
            return c;
        }

        private static Texture2D s_AlphaRamp;

        // 2x1 white texture, alpha 0 -> 1, bilinear.
        private static Texture2D AlphaRamp
        {
            get
            {
                if (s_AlphaRamp == null)
                {
                    s_AlphaRamp = CreateTexture(2, 1);
                    s_AlphaRamp.SetPixels(new[] { new Color(1f, 1f, 1f, 0f), Color.white });
                    s_AlphaRamp.Apply();
                }
                return s_AlphaRamp;
            }
        }

        // ---- Styles ----

        private static float s_StyleScale;
        private static GUIStyle s_Label;
        private static GUIStyle s_MiniLabel;
        private static GUIStyle s_MiniCenteredLabel;
        private static GUIStyle s_Title;
        private static GUIStyle s_Button;
        private static GUIStyle s_TextField;

        public static GUIStyle Label => s_Label;
        public static GUIStyle MiniLabel => s_MiniLabel;
        public static GUIStyle MiniCenteredLabel => s_MiniCenteredLabel;
        public static GUIStyle Title => s_Title;
        public static GUIStyle Button => s_Button;
        public static GUIStyle TextField => s_TextField;

        /// <summary>Builds (or rebuilds after a resolution change) the styles. Call inside OnGUI.</summary>
        public static void EnsureStyles()
        {
            float scale = UIScale;
            if (s_Label != null && Mathf.Approximately(s_StyleScale, scale))
                return;
            s_StyleScale = scale;

            s_Label = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(12 * scale),
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip,
            };
            s_Label.normal.textColor = TextColor;

            s_MiniLabel = new GUIStyle(s_Label)
            {
                fontSize = Mathf.RoundToInt(10 * scale),
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
            };
            s_MiniLabel.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1f);

            s_MiniCenteredLabel = new GUIStyle(s_MiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            s_MiniCenteredLabel.normal.textColor = Color.white;

            s_Title = new GUIStyle(s_Label)
            {
                fontSize = Mathf.RoundToInt(12 * scale),
                fontStyle = FontStyle.Bold,
            };

            s_Button = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(11 * scale),
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip,
            };
            s_Button.normal.textColor = TextColor;
            s_Button.hover.textColor = Color.white;

            s_TextField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = Mathf.RoundToInt(11 * scale),
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(
                    Mathf.RoundToInt(4 * scale),
                    Mathf.RoundToInt(2 * scale),
                    0,
                    0
                ),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip,
            };
        }

        // ---- Controls ----

        private static readonly int s_SliderHash = "RuntimeColorSlider".GetHashCode();

        /// <summary>
        /// A horizontal slider drawn over <paramref name="background"/> (a gradient showing what
        /// the channel does). Returns true when the user changed <paramref name="value"/>;
        /// <paramref name="active"/> is true while it's being dragged.
        /// </summary>
        public static bool Slider(
            Rect rect,
            ref float value,
            float min,
            float max,
            Texture2D background,
            out bool active,
            bool checker = false
        )
        {
            int id = GUIUtility.GetControlID(s_SliderHash, FocusType.Passive, rect);
            Event e = Event.current;
            bool changed = false;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        GUIUtility.keyboardControl = 0;
                        changed = SetFromMouse(rect, ref value, min, max, e.mousePosition.x);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        changed = SetFromMouse(rect, ref value, min, max, e.mousePosition.x);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    if (checker)
                        DrawChecker(rect);
                    if (background != null)
                        GUI.DrawTexture(rect, background, ScaleMode.StretchToFill, true);
                    DrawBorder(rect, ControlBorder);
                    float t = max > min ? Mathf.Clamp01((value - min) / (max - min)) : 0f;
                    float thumbWidth = Mathf.Max(3f, Mathf.Round(3f * UIScale));
                    float x = Mathf.Round(rect.x + t * rect.width - thumbWidth * 0.5f);
                    Rect thumb = new Rect(x, rect.y - 2f, thumbWidth, rect.height + 4f);
                    DrawRect(thumb, Color.white);
                    DrawBorder(thumb, Color.black);
                    break;
            }

            active = GUIUtility.hotControl == id;
            if (changed)
                GUI.changed = true;
            return changed;
        }

        private static bool SetFromMouse(
            Rect rect,
            ref float value,
            float min,
            float max,
            float mouseX
        )
        {
            float t = Mathf.Clamp01((mouseX - rect.x) / rect.width);
            float newValue = Mathf.Lerp(min, max, t);
            if (Mathf.Approximately(newValue, value))
                return false;
            value = newValue;
            return true;
        }

        // Text being typed into the focused field. IMGUI keeps no per-field text for us, so the
        // focused field shows this instead of the reformatted live value until focus leaves.
        private static string s_EditingControl;
        private static string s_EditingText;

        public static bool IsEditingText => s_EditingControl != null;

        /// <summary>
        /// A number field. Applies the typed value as soon as it parses (like the inspector) and
        /// shows the formatted live value while not focused.
        /// </summary>
        public static bool FloatField(Rect rect, string controlName, ref float value, string format)
        {
            string text = EditableText(
                rect,
                controlName,
                value.ToString(format, CultureInfo.InvariantCulture),
                out bool edited
            );
            if (
                edited
                && float.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float parsed
                )
                && !float.IsNaN(parsed)
                && !float.IsInfinity(parsed)
            )
            {
                value = parsed;
                GUI.changed = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// A hex color field (RRGGBB, or RRGGBBAA when <paramref name="withAlpha"/>). Returns
        /// true with the parsed color once the typed text is a full valid code.
        /// </summary>
        public static bool HexField(
            Rect rect,
            string controlName,
            ref Color32 color,
            bool withAlpha
        )
        {
            string current = withAlpha
                ? ColorUtility.ToHtmlStringRGBA(color)
                : ColorUtility.ToHtmlStringRGB(color);
            string text = EditableText(rect, controlName, current, out bool edited);
            if (!edited)
                return false;

            string hex = text.Trim().TrimStart('#');
            bool validLength = hex.Length == 6 || (withAlpha && hex.Length == 8);
            if (validLength && ColorUtility.TryParseHtmlString("#" + hex, out Color parsed))
            {
                Color32 parsed32 = parsed;
                if (!withAlpha)
                    parsed32.a = color.a;
                color = parsed32;
                GUI.changed = true;
                return true;
            }
            return false;
        }

        private static string EditableText(
            Rect rect,
            string controlName,
            string formatted,
            out bool edited
        )
        {
            edited = false;
            bool wasFocused =
                GUI.GetNameOfFocusedControl() == controlName && s_EditingControl == controlName;
            string shown = wasFocused ? s_EditingText : formatted;

            GUI.SetNextControlName(controlName);
            string text = GUI.TextField(rect, shown, s_TextField);

            if (GUI.GetNameOfFocusedControl() != controlName)
            {
                if (s_EditingControl == controlName)
                    s_EditingControl = null;
                return text;
            }

            if (s_EditingControl != controlName)
            {
                s_EditingControl = controlName;
                s_EditingText = shown;
            }
            if (text != s_EditingText)
            {
                s_EditingText = text;
                edited = true;
            }
            return text;
        }

        /// <summary>Drops keyboard focus from any field (commits the typed text).</summary>
        public static void EndTextEditing()
        {
            s_EditingControl = null;
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
        }

        /// <summary>A row of buttons where one is selected. Returns the (new) selected index.</summary>
        public static int Segmented(Rect rect, int selected, string[] labels)
        {
            float width = rect.width / labels.Length;
            Color previous = GUI.backgroundColor;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect r = new Rect(rect.x + i * width, rect.y, width, rect.height);
                GUI.backgroundColor = i == selected ? SelectionColor * 1.6f : previous;
                if (GUI.Button(r, labels[i], s_Button) && i != selected)
                {
                    selected = i;
                    GUI.changed = true;
                }
            }
            GUI.backgroundColor = previous;
            return selected;
        }
    }
}
