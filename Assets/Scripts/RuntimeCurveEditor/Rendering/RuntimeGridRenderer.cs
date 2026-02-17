using UnityEngine;

namespace RuntimeCurveEditor
{
    public static class RuntimeGridRenderer
    {
        private static GUIStyle s_TickLabelStyle;

        private static GUIStyle TickLabelStyle
        {
            get
            {
                if (s_TickLabelStyle == null)
                {
                    s_TickLabelStyle = new GUIStyle(GUI.skin.label);
                    s_TickLabelStyle.fontSize = 10;
                    s_TickLabelStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                    s_TickLabelStyle.alignment = TextAnchor.UpperLeft;
                }
                return s_TickLabelStyle;
            }
        }

        public static void DrawGrid(Rect curveArea, Vector2 shownAreaMin, Vector2 shownAreaMax, bool showLabels)
        {
            if (RuntimeCurveRenderer.LineMaterial == null) return;

            float rangeX = shownAreaMax.x - shownAreaMin.x;
            float rangeY = shownAreaMax.y - shownAreaMin.y;
            if (rangeX <= 0f || rangeY <= 0f) return;

            // Calculate nice tick spacings
            float hTick = CalculateNiceTickSpacing(rangeX, curveArea.width, 80f);
            float vTick = CalculateNiceTickSpacing(rangeY, curveArea.height, 50f);

            Color majorColor = new Color(0.35f, 0.35f, 0.35f, 0.5f);
            Color minorColor = new Color(0.25f, 0.25f, 0.25f, 0.3f);
            Color axisColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);

            RuntimeCurveRenderer.LineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();

            // Minor grid lines (subdivide major ticks by 5)
            float hMinor = hTick / 5f;
            float vMinor = vTick / 5f;

            DrawGridLines(curveArea, shownAreaMin, shownAreaMax, hMinor, true, minorColor);
            DrawGridLines(curveArea, shownAreaMin, shownAreaMax, vMinor, false, minorColor);

            // Major grid lines
            DrawGridLines(curveArea, shownAreaMin, shownAreaMax, hTick, true, majorColor);
            DrawGridLines(curveArea, shownAreaMin, shownAreaMax, vTick, false, majorColor);

            // Axis lines (time=0, value=0)
            if (shownAreaMin.x <= 0f && shownAreaMax.x >= 0f)
            {
                Vector2 top = RuntimeCurveRenderer.DrawingToView(new Vector2(0f, shownAreaMax.y), curveArea, shownAreaMin, shownAreaMax);
                Vector2 bot = RuntimeCurveRenderer.DrawingToView(new Vector2(0f, shownAreaMin.y), curveArea, shownAreaMin, shownAreaMax);
                GL.Begin(GL.LINES);
                GL.Color(axisColor);
                GL.Vertex3(top.x, top.y, 0f);
                GL.Vertex3(bot.x, bot.y, 0f);
                GL.End();
            }
            if (shownAreaMin.y <= 0f && shownAreaMax.y >= 0f)
            {
                Vector2 left = RuntimeCurveRenderer.DrawingToView(new Vector2(shownAreaMin.x, 0f), curveArea, shownAreaMin, shownAreaMax);
                Vector2 right = RuntimeCurveRenderer.DrawingToView(new Vector2(shownAreaMax.x, 0f), curveArea, shownAreaMin, shownAreaMax);
                GL.Begin(GL.LINES);
                GL.Color(axisColor);
                GL.Vertex3(left.x, left.y, 0f);
                GL.Vertex3(right.x, right.y, 0f);
                GL.End();
            }

            GL.PopMatrix();

            // Tick labels
            if (showLabels)
            {
                DrawTickLabels(curveArea, shownAreaMin, shownAreaMax, hTick, true);
                DrawTickLabels(curveArea, shownAreaMin, shownAreaMax, vTick, false);
            }
        }

        private static void DrawGridLines(Rect curveArea, Vector2 shownAreaMin, Vector2 shownAreaMax, float tickSpacing, bool vertical, Color color)
        {
            if (tickSpacing <= 0f) return;

            GL.Begin(GL.LINES);
            GL.Color(color);

            if (vertical)
            {
                float startTick = Mathf.Floor(shownAreaMin.x / tickSpacing) * tickSpacing;
                for (float t = startTick; t <= shownAreaMax.x; t += tickSpacing)
                {
                    Vector2 top = RuntimeCurveRenderer.DrawingToView(new Vector2(t, shownAreaMax.y), curveArea, shownAreaMin, shownAreaMax);
                    if (top.x < curveArea.x || top.x > curveArea.xMax) continue;
                    Vector2 bot = RuntimeCurveRenderer.DrawingToView(new Vector2(t, shownAreaMin.y), curveArea, shownAreaMin, shownAreaMax);
                    GL.Vertex3(top.x, top.y, 0f);
                    GL.Vertex3(bot.x, bot.y, 0f);
                }
            }
            else
            {
                float startTick = Mathf.Floor(shownAreaMin.y / tickSpacing) * tickSpacing;
                for (float v = startTick; v <= shownAreaMax.y; v += tickSpacing)
                {
                    Vector2 left = RuntimeCurveRenderer.DrawingToView(new Vector2(shownAreaMin.x, v), curveArea, shownAreaMin, shownAreaMax);
                    if (left.y < curveArea.y || left.y > curveArea.yMax) continue;
                    Vector2 right = RuntimeCurveRenderer.DrawingToView(new Vector2(shownAreaMax.x, v), curveArea, shownAreaMin, shownAreaMax);
                    GL.Vertex3(left.x, left.y, 0f);
                    GL.Vertex3(right.x, right.y, 0f);
                }
            }

            GL.End();
        }

        private static void DrawTickLabels(Rect curveArea, Vector2 shownAreaMin, Vector2 shownAreaMax, float tickSpacing, bool horizontal)
        {
            if (tickSpacing <= 0f) return;

            string format = GetLabelFormat(tickSpacing);

            if (horizontal)
            {
                float startTick = Mathf.Floor(shownAreaMin.x / tickSpacing) * tickSpacing;
                for (float t = startTick; t <= shownAreaMax.x; t += tickSpacing)
                {
                    Vector2 pos = RuntimeCurveRenderer.DrawingToView(new Vector2(t, shownAreaMin.y), curveArea, shownAreaMin, shownAreaMax);
                    if (pos.x >= curveArea.x && pos.x <= curveArea.xMax - 30f)
                    {
                        Rect labelRect = new Rect(pos.x + 2f, curveArea.yMax - 14f, 60f, 14f);
                        GUI.Label(labelRect, t.ToString(format), TickLabelStyle);
                    }
                }
            }
            else
            {
                float startTick = Mathf.Floor(shownAreaMin.y / tickSpacing) * tickSpacing;
                for (float v = startTick; v <= shownAreaMax.y; v += tickSpacing)
                {
                    Vector2 pos = RuntimeCurveRenderer.DrawingToView(new Vector2(shownAreaMin.x, v), curveArea, shownAreaMin, shownAreaMax);
                    if (pos.y >= curveArea.y + 5f && pos.y <= curveArea.yMax - 14f)
                    {
                        Rect labelRect = new Rect(curveArea.x + 2f, pos.y - 7f, 60f, 14f);
                        GUI.Label(labelRect, v.ToString(format), TickLabelStyle);
                    }
                }
            }
        }

        public static float CalculateNiceTickSpacing(float range, float pixelSize, float minPixelsPerTick)
        {
            if (range <= 0f || pixelSize <= 0f) return 1f;

            float rawSpacing = range * minPixelsPerTick / pixelSize;
            return NiceNumber(rawSpacing, true);
        }

        private static float NiceNumber(float value, bool round)
        {
            float exponent = Mathf.Floor(Mathf.Log10(value));
            float fraction = value / Mathf.Pow(10f, exponent);

            float niceFraction;
            if (round)
            {
                if (fraction < 1.5f) niceFraction = 1f;
                else if (fraction < 3f) niceFraction = 2f;
                else if (fraction < 7f) niceFraction = 5f;
                else niceFraction = 10f;
            }
            else
            {
                if (fraction <= 1f) niceFraction = 1f;
                else if (fraction <= 2f) niceFraction = 2f;
                else if (fraction <= 5f) niceFraction = 5f;
                else niceFraction = 10f;
            }

            return niceFraction * Mathf.Pow(10f, exponent);
        }

        private static string GetLabelFormat(float tickSpacing)
        {
            if (tickSpacing >= 1f) return "F0";
            if (tickSpacing >= 0.1f) return "F1";
            if (tickSpacing >= 0.01f) return "F2";
            return "F3";
        }
    }
}
