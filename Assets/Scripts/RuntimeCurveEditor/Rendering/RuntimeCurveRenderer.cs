using UnityEngine;

namespace RuntimeCurveEditor
{
    public class RuntimeCurveRenderer
    {
        private static Material s_LineMaterial;

        public static Material LineMaterial
        {
            get
            {
                if (s_LineMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null)
                    {
                        Debug.LogError(
                            "RuntimeCurveRenderer: Could not find 'Hidden/Internal-Colored' shader."
                        );
                        return null;
                    }
                    s_LineMaterial = new Material(shader);
                    s_LineMaterial.hideFlags = HideFlags.HideAndDontSave;
                    s_LineMaterial.SetInt(
                        "_SrcBlend",
                        (int)UnityEngine.Rendering.BlendMode.SrcAlpha
                    );
                    s_LineMaterial.SetInt(
                        "_DstBlend",
                        (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
                    );
                    s_LineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    s_LineMaterial.SetInt("_ZWrite", 0);
                }
                return s_LineMaterial;
            }
        }

        public static void DrawCurve(
            AnimationCurve curve,
            Rect curveArea,
            Vector2 shownAreaMin,
            Vector2 shownAreaMax,
            Color color,
            float lineWidth = 2f
        )
        {
            if (curve == null || curve.length == 0)
                return;
            if (LineMaterial == null)
                return;

            float visibleMinTime = shownAreaMin.x;
            float visibleMaxTime = shownAreaMax.x;

            int sampleCount = Mathf.Max((int)(curveArea.width / 2f), 64);
            float timeStep = (visibleMaxTime - visibleMinTime) / sampleCount;

            LineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();

            // Main curve
            GL.Begin(GL.LINE_STRIP);
            GL.Color(color);

            for (int i = 0; i <= sampleCount; i++)
            {
                float t = visibleMinTime + i * timeStep;
                float v = curve.Evaluate(t);
                Vector2 viewPos = DrawingToView(
                    new Vector2(t, v),
                    curveArea,
                    shownAreaMin,
                    shownAreaMax
                );
                GL.Vertex3(viewPos.x, viewPos.y, 0f);
            }

            GL.End();

            // Draw wrap mode extensions with faded color
            if (curve.length >= 2)
            {
                Color wrapColor = color;
                wrapColor.a *= 0.4f;

                float firstKeyTime = curve[0].time;
                float lastKeyTime = curve[curve.length - 1].time;

                // Pre-wrap
                if (visibleMinTime < firstKeyTime && curve.preWrapMode != WrapMode.Default)
                {
                    DrawWrapRegion(
                        curve,
                        curveArea,
                        shownAreaMin,
                        shownAreaMax,
                        wrapColor,
                        visibleMinTime,
                        firstKeyTime,
                        sampleCount / 4
                    );
                }

                // Post-wrap
                if (visibleMaxTime > lastKeyTime && curve.postWrapMode != WrapMode.Default)
                {
                    DrawWrapRegion(
                        curve,
                        curveArea,
                        shownAreaMin,
                        shownAreaMax,
                        wrapColor,
                        lastKeyTime,
                        visibleMaxTime,
                        sampleCount / 4
                    );
                }
            }

            GL.PopMatrix();
        }

        private static void DrawWrapRegion(
            AnimationCurve curve,
            Rect curveArea,
            Vector2 shownAreaMin,
            Vector2 shownAreaMax,
            Color color,
            float startTime,
            float endTime,
            int sampleCount
        )
        {
            if (sampleCount < 2)
                sampleCount = 2;
            float timeStep = (endTime - startTime) / sampleCount;

            GL.Begin(GL.LINE_STRIP);
            GL.Color(color);

            for (int i = 0; i <= sampleCount; i++)
            {
                float t = startTime + i * timeStep;
                float v = curve.Evaluate(t);
                Vector2 viewPos = DrawingToView(
                    new Vector2(t, v),
                    curveArea,
                    shownAreaMin,
                    shownAreaMax
                );
                GL.Vertex3(viewPos.x, viewPos.y, 0f);
            }

            GL.End();
        }

        public static void DrawMiniCurve(AnimationCurve curve, Rect rect, Color color)
        {
            if (curve == null || curve.length == 0)
                return;
            if (LineMaterial == null)
                return;

            // Compute bounds
            float minTime,
                maxTime,
                minVal,
                maxVal;
            GetCurveBounds(curve, out minTime, out maxTime, out minVal, out maxVal);

            float padding = 0.05f;
            float timeRange = maxTime - minTime;
            float valRange = maxVal - minVal;
            if (timeRange < 0.001f)
            {
                timeRange = 1f;
                minTime -= 0.5f;
            }
            if (valRange < 0.001f)
            {
                valRange = 1f;
                minVal -= 0.5f;
            }

            Vector2 areaMin = new Vector2(
                minTime - timeRange * padding,
                minVal - valRange * padding
            );
            Vector2 areaMax = new Vector2(
                maxTime + timeRange * padding,
                maxVal + valRange * padding
            );

            int sampleCount = Mathf.Max((int)(rect.width / 2f), 16);
            float timeStep = (areaMax.x - areaMin.x) / sampleCount;

            LineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();

            GL.Begin(GL.LINE_STRIP);
            GL.Color(color);

            for (int i = 0; i <= sampleCount; i++)
            {
                float t = areaMin.x + i * timeStep;
                float v = curve.Evaluate(t);
                Vector2 viewPos = DrawingToView(new Vector2(t, v), rect, areaMin, areaMax);
                GL.Vertex3(viewPos.x, viewPos.y, 0f);
            }

            GL.End();
            GL.PopMatrix();
        }

        public static void DrawLine(Vector2 a, Vector2 b, Color color)
        {
            if (LineMaterial == null)
                return;

            LineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();

            GL.Begin(GL.LINES);
            GL.Color(color);
            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(b.x, b.y, 0f);
            GL.End();

            GL.PopMatrix();
        }

        public static Vector2 DrawingToView(
            Vector2 drawingPoint,
            Rect curveArea,
            Vector2 shownAreaMin,
            Vector2 shownAreaMax
        )
        {
            float rangeX = shownAreaMax.x - shownAreaMin.x;
            float rangeY = shownAreaMax.y - shownAreaMin.y;
            if (rangeX == 0f)
                rangeX = 1f;
            if (rangeY == 0f)
                rangeY = 1f;

            float x = curveArea.x + (drawingPoint.x - shownAreaMin.x) / rangeX * curveArea.width;
            float y =
                curveArea.y
                + curveArea.height
                - (drawingPoint.y - shownAreaMin.y) / rangeY * curveArea.height;
            return new Vector2(x, y);
        }

        public static Vector2 ViewToDrawing(
            Vector2 viewPoint,
            Rect curveArea,
            Vector2 shownAreaMin,
            Vector2 shownAreaMax
        )
        {
            float rangeX = shownAreaMax.x - shownAreaMin.x;
            float rangeY = shownAreaMax.y - shownAreaMin.y;
            if (rangeX == 0f)
                rangeX = 1f;
            if (rangeY == 0f)
                rangeY = 1f;

            float x = shownAreaMin.x + (viewPoint.x - curveArea.x) / curveArea.width * rangeX;
            float y =
                shownAreaMin.y
                + (curveArea.y + curveArea.height - viewPoint.y) / curveArea.height * rangeY;
            return new Vector2(x, y);
        }

        public static void GetCurveBounds(
            AnimationCurve curve,
            out float minTime,
            out float maxTime,
            out float minVal,
            out float maxVal
        )
        {
            if (curve.length == 0)
            {
                minTime = 0f;
                maxTime = 1f;
                minVal = 0f;
                maxVal = 1f;
                return;
            }

            minTime = curve[0].time;
            maxTime = curve[curve.length - 1].time;
            minVal = float.MaxValue;
            maxVal = float.MinValue;

            // Sample the curve to find value bounds
            int samples = Mathf.Max(curve.length * 10, 50);
            float timeRange = maxTime - minTime;
            if (timeRange < 0.0001f)
                timeRange = 1f;

            for (int i = 0; i <= samples; i++)
            {
                float t = minTime + (float)i / samples * timeRange;
                float v = curve.Evaluate(t);
                if (v < minVal)
                    minVal = v;
                if (v > maxVal)
                    maxVal = v;
            }

            // Also check keyframe values directly
            for (int i = 0; i < curve.length; i++)
            {
                float v = curve[i].value;
                if (v < minVal)
                    minVal = v;
                if (v > maxVal)
                    maxVal = v;
            }
        }
    }
}
