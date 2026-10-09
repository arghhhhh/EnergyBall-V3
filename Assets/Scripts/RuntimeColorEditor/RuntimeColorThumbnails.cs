using UnityEngine;

namespace RuntimeColorEditor
{
    /// <summary>
    /// CPU-rendered previews for the settings menu (UI Toolkit shows them as background
    /// images): a color swatch with an alpha bar, and a gradient strip. Transparency is shown
    /// over a checkerboard, as in the inspector.
    /// </summary>
    public static class RuntimeColorThumbnails
    {
        private const int CheckerCell = 6;
        private static readonly Color CheckerLight = Color.white;
        private static readonly Color CheckerDark = new Color(0.7f, 0.7f, 0.7f, 1f);

        public static Texture2D CreateTexture(int width, int height)
        {
            var tex = RuntimeColorGUI.CreateTexture(width, height);
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        /// <param name="hdr">The color is linear HDR (gamma-encoded for display).</param>
        /// <param name="showAlpha">Draw the alpha bar along the bottom, like the inspector.</param>
        public static void RenderColorSwatch(Texture2D tex, Color color, bool hdr, bool showAlpha)
        {
            int width = tex.width;
            int height = tex.height;
            Color display = RuntimeColorGUI.ToDisplay(color, hdr);
            float alpha = showAlpha ? display.a : 1f;
            int barHeight = showAlpha ? Mathf.Max(2, Mathf.RoundToInt(height * 0.15f)) : 0;
            int barSplit = Mathf.RoundToInt(width * display.a);

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c;
                    if (y < barHeight) // row 0 is the bottom
                        c = x < barSplit ? Color.white : Color.black;
                    else
                        c = OverChecker(
                            RuntimeColorGUI.SwatchColorAt(color, hdr, (x + 0.5f) / width),
                            alpha,
                            x,
                            y
                        );
                    pixels[y * width + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
        }

        public static void RenderGradient(Texture2D tex, Gradient gradient)
        {
            int width = tex.width;
            int height = tex.height;
            const int rows = RuntimeGradientEditorWindow.PreviewRows;
            Color[] preview = RuntimeGradientEditorWindow.RenderGradientPixels(gradient, width);

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                // Stretch the preview rows over the height with bilinear filtering, as the
                // editor draws its preview texture (pixel centres, clamped at the edges).
                float row = Mathf.Clamp((y + 0.5f) / height * rows - 0.5f, 0f, rows - 1);
                int row0 = Mathf.FloorToInt(row);
                int row1 = Mathf.Min(row0 + 1, rows - 1);
                float t = row - row0;
                for (int x = 0; x < width; x++)
                {
                    Color c = Color.Lerp(preview[row0 * width + x], preview[row1 * width + x], t);
                    pixels[y * width + x] = OverChecker(c, c.a, x, y);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
        }

        private static Color OverChecker(Color color, float alpha, int x, int y)
        {
            if (alpha >= 1f)
                return new Color(color.r, color.g, color.b, 1f);
            Color checker =
                ((x / CheckerCell + y / CheckerCell) & 1) == 0 ? CheckerLight : CheckerDark;
            Color c = Color.Lerp(checker, color, alpha);
            c.a = 1f;
            return c;
        }
    }
}
