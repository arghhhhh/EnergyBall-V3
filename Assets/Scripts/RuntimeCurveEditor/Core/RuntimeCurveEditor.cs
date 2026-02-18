using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeCurveEditor
{
    public class RuntimeCurveEditorCore
    {
        public RuntimeCurveWrapper curveWrapper;
        public RuntimeCurveEditorSettings settings;
        public SelectionManager selection;
        public RuntimeCurveInputHandler inputHandler;

        public Rect curveArea;
        public Vector2 shownAreaMin;
        public Vector2 shownAreaMax;

        public Action onCurveChanged;
        public bool isDirty;

        public RuntimeCurveEditorCore(
            AnimationCurve curve,
            Color color,
            RuntimeCurveEditorSettings settings = null
        )
        {
            this.settings = settings ?? RuntimeCurveEditorSettings.DefaultUnbounded();
            curveWrapper = new RuntimeCurveWrapper(curve, color);
            selection = new SelectionManager();
            inputHandler = new RuntimeCurveInputHandler(this);

            FrameToFit();
        }

        public void SetCurve(AnimationCurve curve, Color color)
        {
            curveWrapper = new RuntimeCurveWrapper(curve, color);
            selection.SelectNone();
            FrameToFit();
        }

        public void FrameToFit()
        {
            AnimationCurve curve = curveWrapper.curve;
            if (curve == null || curve.length == 0)
            {
                shownAreaMin = new Vector2(-0.1f, -0.1f);
                shownAreaMax = new Vector2(1.1f, 1.1f);
                return;
            }

            float minTime,
                maxTime,
                minVal,
                maxVal;
            RuntimeCurveRenderer.GetCurveBounds(
                curve,
                out minTime,
                out maxTime,
                out minVal,
                out maxVal
            );

            // Apply settings bounds if set
            if (!settings.hasUnboundedRanges)
            {
                minTime = settings.hRangeMin;
                maxTime = settings.hRangeMax;
                minVal = settings.vRangeMin;
                maxVal = settings.vRangeMax;
            }

            float timeRange = maxTime - minTime;
            float valRange = maxVal - minVal;
            if (timeRange < 0.001f)
                timeRange = 1f;
            if (valRange < 0.001f)
                valRange = 1f;

            float marginFrac = 0.1f;
            shownAreaMin = new Vector2(
                minTime - timeRange * marginFrac,
                minVal - valRange * marginFrac
            );
            shownAreaMax = new Vector2(
                maxTime + timeRange * marginFrac,
                maxVal + valRange * marginFrac
            );
        }

        public void FrameSelected()
        {
            var indices = selection.GetSelectedKeyIndices(curveWrapper.id);
            if (indices.Count == 0)
            {
                FrameToFit();
                return;
            }

            AnimationCurve curve = curveWrapper.curve;
            float minT = float.MaxValue,
                maxT = float.MinValue;
            float minV = float.MaxValue,
                maxV = float.MinValue;

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= curve.length)
                    continue;
                Keyframe kf = curve[idx];
                if (kf.time < minT)
                    minT = kf.time;
                if (kf.time > maxT)
                    maxT = kf.time;
                if (kf.value < minV)
                    minV = kf.value;
                if (kf.value > maxV)
                    maxV = kf.value;
            }

            float timeRange = maxT - minT;
            float valRange = maxV - minV;
            if (timeRange < 0.1f)
                timeRange = 0.5f;
            if (valRange < 0.1f)
                valRange = 0.5f;

            float margin = 0.2f;
            shownAreaMin = new Vector2(minT - timeRange * margin, minV - valRange * margin);
            shownAreaMax = new Vector2(maxT + timeRange * margin, maxV + valRange * margin);
        }

        public void OnGUI(Rect area)
        {
            curveArea = area;

            // Draw background
            DrawBackground(curveArea);

            // Draw grid
            RuntimeGridRenderer.DrawGrid(
                curveArea,
                shownAreaMin,
                shownAreaMax,
                settings.showAxisLabels
            );

            // Draw curve line
            RuntimeCurveRenderer.DrawCurve(
                curveWrapper.curve,
                curveArea,
                shownAreaMin,
                shownAreaMax,
                curveWrapper.color,
                2f
            );

            // Draw tangent lines and handles for selected keyframes
            DrawTangentHandles(curveArea);

            // Draw keyframe points
            DrawKeyframes(curveArea);

            // Draw selection rect if active
            if (inputHandler.isMarqueeSelecting)
                DrawSelectionRect(inputHandler.marqueeRect);

            // Process input
            inputHandler.HandleInput(curveArea);

            // Check for changes
            if (curveWrapper.changed)
            {
                curveWrapper.changed = false;
                isDirty = true;
                onCurveChanged?.Invoke();
            }
        }

        private void DrawBackground(Rect localArea)
        {
            // Dark background matching Unity editor
            Color bgColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            Texture2D bgTex = Texture2D.whiteTexture;
            GUI.color = bgColor;
            GUI.DrawTexture(localArea, bgTex);
            GUI.color = Color.white;
        }

        private void DrawKeyframes(Rect area)
        {
            AnimationCurve curve = curveWrapper.curve;
            if (curve == null)
                return;

            for (int i = 0; i < curve.length; i++)
            {
                Keyframe kf = curve[i];
                Vector2 viewPos = RuntimeCurveRenderer.DrawingToView(
                    new Vector2(kf.time, kf.value),
                    area,
                    shownAreaMin,
                    shownAreaMax
                );

                bool isSelected = selection.IsKeySelected(curveWrapper.id, i);
                bool isSemiSelected = false;
                bool isWeighted = kf.weightedMode != WeightedMode.None;

                RuntimeKeyframeRenderer.DrawKeyframe(
                    viewPos,
                    isSelected,
                    isSemiSelected,
                    isWeighted
                );
            }
        }

        private void DrawTangentHandles(Rect area)
        {
            AnimationCurve curve = curveWrapper.curve;
            if (curve == null)
                return;

            Color tangentLineColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);

            for (int i = 0; i < curve.length; i++)
            {
                if (!selection.IsKeySelected(curveWrapper.id, i))
                    continue;

                Keyframe kf = curve[i];
                Vector2 keyViewPos = RuntimeCurveRenderer.DrawingToView(
                    new Vector2(kf.time, kf.value),
                    area,
                    shownAreaMin,
                    shownAreaMax
                );

                // In tangent (left side)
                if (i > 0)
                {
                    Vector2 inTangentViewPos = GetTangentHandlePosition(i, true, area);
                    RuntimeCurveRenderer.DrawLine(keyViewPos, inTangentViewPos, tangentLineColor);
                    RuntimeKeyframeRenderer.DrawTangentDot(inTangentViewPos);
                }

                // Out tangent (right side)
                if (i < curve.length - 1)
                {
                    Vector2 outTangentViewPos = GetTangentHandlePosition(i, false, area);
                    RuntimeCurveRenderer.DrawLine(keyViewPos, outTangentViewPos, tangentLineColor);
                    RuntimeKeyframeRenderer.DrawTangentDot(outTangentViewPos);
                }
            }
        }

        public Vector2 GetTangentHandlePosition(int keyIndex, bool inTangent, Rect area)
        {
            AnimationCurve curve = curveWrapper.curve;
            Keyframe kf = curve[keyIndex];
            Vector2 keyViewPos = RuntimeCurveRenderer.DrawingToView(
                new Vector2(kf.time, kf.value),
                area,
                shownAreaMin,
                shownAreaMax
            );

            float tangent = inTangent ? kf.inTangent : kf.outTangent;
            float sign = inTangent ? -1f : 1f;

            // Handle infinity tangents (constant mode)
            Vector2 direction;
            if (float.IsInfinity(tangent))
            {
                direction = new Vector2(0f, tangent > 0 ? -1f : 1f);
            }
            else
            {
                direction = new Vector2(sign, sign * tangent);
            }

            // Normalize in view space for consistent visual length
            Vector2 viewScale = GetViewScale(area);
            Vector2 dirInView = new Vector2(direction.x * viewScale.x, -direction.y * viewScale.y);

            float handleLength = 50f; // pixels

            // Check for weighted tangents
            bool isWeighted = false;
            if (inTangent && (kf.weightedMode & WeightedMode.In) != 0)
                isWeighted = true;
            else if (!inTangent && (kf.weightedMode & WeightedMode.Out) != 0)
                isWeighted = true;

            if (isWeighted && !float.IsInfinity(tangent))
            {
                float weight = inTangent ? kf.inWeight : kf.outWeight;
                float dt;
                if (inTangent && keyIndex > 0)
                    dt = kf.time - curve[keyIndex - 1].time;
                else if (!inTangent && keyIndex < curve.length - 1)
                    dt = curve[keyIndex + 1].time - kf.time;
                else
                    dt = 1f;

                Vector2 weightedDir = new Vector2(sign * dt * weight, sign * tangent * dt * weight);
                Vector2 weightedDirInView = new Vector2(
                    weightedDir.x * viewScale.x,
                    -weightedDir.y * viewScale.y
                );

                if (weightedDirInView.magnitude > 10f)
                {
                    return keyViewPos + weightedDirInView;
                }
            }

            if (dirInView.magnitude > 0.001f)
            {
                dirInView = dirInView.normalized * handleLength;
            }
            else
            {
                dirInView = new Vector2(sign * handleLength, 0f);
            }

            return keyViewPos + dirInView;
        }

        public Vector2 GetViewScale(Rect area)
        {
            float rangeX = shownAreaMax.x - shownAreaMin.x;
            float rangeY = shownAreaMax.y - shownAreaMin.y;
            if (rangeX == 0f)
                rangeX = 1f;
            if (rangeY == 0f)
                rangeY = 1f;

            return new Vector2(area.width / rangeX, area.height / rangeY);
        }

        private void DrawSelectionRect(Rect marqueeRect)
        {
            Color fillColor = new Color(0.3f, 0.5f, 0.8f, 0.15f);
            Color borderColor = new Color(0.4f, 0.6f, 0.9f, 0.6f);

            GUI.color = fillColor;
            GUI.DrawTexture(marqueeRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Border lines
            RuntimeCurveRenderer.DrawLine(
                new Vector2(marqueeRect.x, marqueeRect.y),
                new Vector2(marqueeRect.xMax, marqueeRect.y),
                borderColor
            );
            RuntimeCurveRenderer.DrawLine(
                new Vector2(marqueeRect.xMax, marqueeRect.y),
                new Vector2(marqueeRect.xMax, marqueeRect.yMax),
                borderColor
            );
            RuntimeCurveRenderer.DrawLine(
                new Vector2(marqueeRect.xMax, marqueeRect.yMax),
                new Vector2(marqueeRect.x, marqueeRect.yMax),
                borderColor
            );
            RuntimeCurveRenderer.DrawLine(
                new Vector2(marqueeRect.x, marqueeRect.yMax),
                new Vector2(marqueeRect.x, marqueeRect.y),
                borderColor
            );
        }

        public void SelectNone()
        {
            selection.SelectNone();
        }
    }
}
