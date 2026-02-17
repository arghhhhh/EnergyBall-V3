using UnityEngine;

namespace RuntimeCurveEditor
{
    public static class RuntimeKeyframeRenderer
    {
        private static Texture2D s_DiamondUnselected;
        private static Texture2D s_DiamondSelected;
        private static Texture2D s_DiamondSemiSelected;
        private static Texture2D s_TangentDot;
        private static Texture2D s_WeightedDiamond;

        private const int DIAMOND_SIZE = 13;
        private const int DOT_SIZE = 9;

        public static void EnsureInitialized()
        {
            if (s_DiamondUnselected == null)
                GenerateTextures();
        }

        private static void GenerateTextures()
        {
            s_DiamondUnselected = CreateDiamondTexture(DIAMOND_SIZE, new Color(0.6f, 0.6f, 0.6f, 1f), new Color(0.15f, 0.15f, 0.15f, 1f));
            s_DiamondSelected = CreateDiamondTexture(DIAMOND_SIZE, new Color(1f, 1f, 1f, 1f), new Color(0.2f, 0.4f, 0.8f, 1f));
            s_DiamondSemiSelected = CreateDiamondTexture(DIAMOND_SIZE, new Color(0.8f, 0.8f, 1f, 0.8f), new Color(0.15f, 0.15f, 0.15f, 1f));
            s_WeightedDiamond = CreateDiamondTexture(DIAMOND_SIZE, new Color(1f, 0.8f, 0.2f, 1f), new Color(0.15f, 0.15f, 0.15f, 1f));
            s_TangentDot = CreateCircleTexture(DOT_SIZE, new Color(0.8f, 0.8f, 0.8f, 1f));
        }

        private static Texture2D CreateDiamondTexture(int size, Color outlineColor, Color fillColor)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.filterMode = FilterMode.Bilinear;

            Color clear = new Color(0, 0, 0, 0);
            float center = (size - 1) / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - center) / center;
                    float dy = Mathf.Abs(y - center) / center;
                    float dist = dx + dy;

                    if (dist <= 0.75f)
                        tex.SetPixel(x, y, fillColor);
                    else if (dist <= 1.0f)
                        tex.SetPixel(x, y, outlineColor);
                    else
                        tex.SetPixel(x, y, clear);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D CreateCircleTexture(int size, Color color)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.filterMode = FilterMode.Bilinear;

            Color clear = new Color(0, 0, 0, 0);
            float center = (size - 1) / 2f;
            float radius = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist <= radius - 1f)
                        tex.SetPixel(x, y, color);
                    else if (dist <= radius)
                        tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * (radius - dist)));
                    else
                        tex.SetPixel(x, y, clear);
                }
            }

            tex.Apply();
            return tex;
        }

        public static void DrawKeyframe(Vector2 viewPos, bool selected, bool semiSelected, bool weighted)
        {
            EnsureInitialized();

            Texture2D tex;
            if (weighted)
                tex = s_WeightedDiamond;
            else if (selected)
                tex = s_DiamondSelected;
            else if (semiSelected)
                tex = s_DiamondSemiSelected;
            else
                tex = s_DiamondUnselected;

            float half = DIAMOND_SIZE / 2f;
            Rect rect = new Rect(viewPos.x - half, viewPos.y - half, DIAMOND_SIZE, DIAMOND_SIZE);
            GUI.DrawTexture(rect, tex);
        }

        public static void DrawTangentDot(Vector2 viewPos)
        {
            EnsureInitialized();

            float half = DOT_SIZE / 2f;
            Rect rect = new Rect(viewPos.x - half, viewPos.y - half, DOT_SIZE, DOT_SIZE);
            GUI.DrawTexture(rect, s_TangentDot);
        }

        public static Rect GetKeyframeRect(Vector2 viewPos)
        {
            float half = DIAMOND_SIZE / 2f + 2f;
            return new Rect(viewPos.x - half, viewPos.y - half, half * 2f, half * 2f);
        }

        public static Rect GetTangentDotRect(Vector2 viewPos)
        {
            float half = DOT_SIZE / 2f + 2f;
            return new Rect(viewPos.x - half, viewPos.y - half, half * 2f, half * 2f);
        }
    }
}
