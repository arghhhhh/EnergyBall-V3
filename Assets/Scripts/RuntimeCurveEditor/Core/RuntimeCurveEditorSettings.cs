using System;
using UnityEngine;

namespace RuntimeCurveEditor
{
    [Serializable]
    public class RuntimeCurveEditorSettings
    {
        public float hRangeMin = float.NegativeInfinity;
        public float hRangeMax = float.PositiveInfinity;
        public float vRangeMin = float.NegativeInfinity;
        public float vRangeMax = float.PositiveInfinity;

        public bool hRangeLocked;
        public bool vRangeLocked;

        public bool showAxisLabels = true;
        public bool allowDeleteLastKeyInCurve;
        public bool allowDraggingCurvesAndRegions = true;

        public string xAxisLabel = "time";
        public string yAxisLabel = "value";

        public float hTickLabelOffset = 10f;
        public Vector2 curveRegionDomain = new Vector2(0f, 1f);

        public bool hasUnboundedRanges =>
            hRangeMin == float.NegativeInfinity ||
            hRangeMax == float.PositiveInfinity ||
            vRangeMin == float.NegativeInfinity ||
            vRangeMax == float.PositiveInfinity;

        public static RuntimeCurveEditorSettings DefaultNormalized()
        {
            return new RuntimeCurveEditorSettings
            {
                hRangeMin = 0f,
                hRangeMax = 1f,
                vRangeMin = 0f,
                vRangeMax = 1f
            };
        }

        public static RuntimeCurveEditorSettings DefaultUnbounded()
        {
            return new RuntimeCurveEditorSettings();
        }
    }
}
