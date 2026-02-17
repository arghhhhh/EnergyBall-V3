using UnityEngine;

namespace RuntimeCurveEditor
{
    /// <summary>
    /// Runtime replacement for UnityEditor.AnimationUtility tangent mode methods.
    /// Uses the deprecated but still functional Keyframe.tangentMode property to read/write
    /// the internal tangent mode bitfield at runtime.
    ///
    /// Bitfield layout of tangentModeInternal (int):
    ///   Bit 0:    Broken flag (1 = broken, left/right tangents are independent)
    ///   Bits 1-5: Left tangent mode  (TangentMode value << 1)
    ///   Bits 6-10: Right tangent mode (TangentMode value << 6)
    /// </summary>
    public static class KeyframeTangentUtility
    {
        public enum TangentMode
        {
            Free = 0,
            Auto = 1,
            Linear = 2,
            Constant = 3,
            ClampedAuto = 4
        }

        private const int kBrokenMask = 1;         // bit 0
        private const int kLeftTangentMask = 0x3E;  // bits 1-5 (5 bits)
        private const int kLeftTangentShift = 1;
        private const int kRightTangentMask = 0x7C0; // bits 6-10 (5 bits)
        private const int kRightTangentShift = 6;

#pragma warning disable 0618 // Suppress tangentMode deprecation warning

        public static TangentMode GetKeyLeftTangentMode(Keyframe key)
        {
            return (TangentMode)((key.tangentMode & kLeftTangentMask) >> kLeftTangentShift);
        }

        public static TangentMode GetKeyRightTangentMode(Keyframe key)
        {
            return (TangentMode)((key.tangentMode & kRightTangentMask) >> kRightTangentShift);
        }

        public static bool GetKeyBroken(Keyframe key)
        {
            return (key.tangentMode & kBrokenMask) != 0;
        }

        public static void SetKeyLeftTangentMode(ref Keyframe key, TangentMode mode)
        {
            key.tangentMode = (key.tangentMode & ~kLeftTangentMask) | ((int)mode << kLeftTangentShift);
        }

        public static void SetKeyRightTangentMode(ref Keyframe key, TangentMode mode)
        {
            key.tangentMode = (key.tangentMode & ~kRightTangentMask) | ((int)mode << kRightTangentShift);
        }

        public static void SetKeyBroken(ref Keyframe key, bool broken)
        {
            if (broken)
                key.tangentMode |= kBrokenMask;
            else
                key.tangentMode &= ~kBrokenMask;
        }

#pragma warning restore 0618

        /// <summary>
        /// Runtime replacement for AnimationUtility.UpdateTangentsFromModeSurrounding.
        /// Recalculates tangent values for the key at the given index and its neighbors
        /// based on their tangent modes.
        /// </summary>
        public static void UpdateTangentsFromModeSurrounding(AnimationCurve curve, int index)
        {
            UpdateTangentsFromMode(curve, index);

            // Also update neighbors that might have auto tangent modes
            if (index > 0)
                UpdateTangentsFromMode(curve, index - 1);
            if (index < curve.length - 1)
                UpdateTangentsFromMode(curve, index + 1);
        }

        /// <summary>
        /// Recalculates tangent values for a single key based on its tangent mode.
        /// </summary>
        public static void UpdateTangentsFromMode(AnimationCurve curve, int index)
        {
            if (index < 0 || index >= curve.length) return;

            Keyframe key = curve[index];
            TangentMode leftMode = GetKeyLeftTangentMode(key);
            TangentMode rightMode = GetKeyRightTangentMode(key);

            bool changed = false;

            // Update left tangent
            if (leftMode == TangentMode.Linear)
            {
                if (index > 0)
                {
                    Keyframe prev = curve[index - 1];
                    float dt = key.time - prev.time;
                    if (dt > 0.0001f)
                    {
                        key.inTangent = (key.value - prev.value) / dt;
                        changed = true;
                    }
                }
            }
            else if (leftMode == TangentMode.Constant)
            {
                key.inTangent = float.PositiveInfinity;
                changed = true;
            }
            else if (leftMode == TangentMode.ClampedAuto || leftMode == TangentMode.Auto)
            {
                float tangent = CalculateAutoTangent(curve, index, leftMode == TangentMode.ClampedAuto);
                key.inTangent = tangent;
                if (!GetKeyBroken(key))
                {
                    key.outTangent = tangent;
                }
                changed = true;
            }

            // Update right tangent
            if (rightMode == TangentMode.Linear)
            {
                if (index < curve.length - 1)
                {
                    Keyframe next = curve[index + 1];
                    float dt = next.time - key.time;
                    if (dt > 0.0001f)
                    {
                        key.outTangent = (next.value - key.value) / dt;
                        changed = true;
                    }
                }
            }
            else if (rightMode == TangentMode.Constant)
            {
                key.outTangent = float.PositiveInfinity;
                changed = true;
            }
            else if (rightMode == TangentMode.ClampedAuto || rightMode == TangentMode.Auto)
            {
                float tangent = CalculateAutoTangent(curve, index, rightMode == TangentMode.ClampedAuto);
                key.outTangent = tangent;
                if (!GetKeyBroken(key))
                {
                    key.inTangent = tangent;
                }
                changed = true;
            }

            if (changed)
                curve.MoveKey(index, key);
        }

        /// <summary>
        /// Calculates an automatic smooth tangent for a keyframe.
        /// For ClampedAuto, prevents overshoot by clamping tangent to zero when it would cause the curve
        /// to go past the neighboring values.
        /// </summary>
        private static float CalculateAutoTangent(AnimationCurve curve, int index, bool clamped)
        {
            if (curve.length < 2) return 0f;

            Keyframe key = curve[index];

            // First or last key
            if (index == 0)
            {
                if (curve.length >= 2)
                {
                    Keyframe next = curve[1];
                    float dt = next.time - key.time;
                    if (dt > 0.0001f)
                    {
                        float slope = (next.value - key.value) / dt;
                        return clamped ? ClampTangent(slope, key.value, next.value) : slope;
                    }
                }
                return 0f;
            }

            if (index == curve.length - 1)
            {
                if (curve.length >= 2)
                {
                    Keyframe prev = curve[index - 1];
                    float dt = key.time - prev.time;
                    if (dt > 0.0001f)
                    {
                        float slope = (key.value - prev.value) / dt;
                        return clamped ? ClampTangent(slope, prev.value, key.value) : slope;
                    }
                }
                return 0f;
            }

            // Interior key - use average of slopes to neighbors
            Keyframe prevKey = curve[index - 1];
            Keyframe nextKey = curve[index + 1];

            float dtPrev = key.time - prevKey.time;
            float dtNext = nextKey.time - key.time;

            if (dtPrev < 0.0001f || dtNext < 0.0001f) return 0f;

            float slopePrev = (key.value - prevKey.value) / dtPrev;
            float slopeNext = (nextKey.value - key.value) / dtNext;

            // Weighted average based on time spans
            float totalDt = dtPrev + dtNext;
            float tangent = (slopePrev * dtNext + slopeNext * dtPrev) / totalDt;

            if (clamped)
            {
                // Clamp to prevent overshoot: if the key is a local extremum, force flat tangent
                if ((key.value >= prevKey.value && key.value >= nextKey.value) ||
                    (key.value <= prevKey.value && key.value <= nextKey.value))
                {
                    tangent = 0f;
                }
                else
                {
                    // Clamp tangent magnitude
                    float maxSlope = Mathf.Max(Mathf.Abs(slopePrev), Mathf.Abs(slopeNext));
                    if (Mathf.Abs(tangent) > maxSlope * 3f)
                        tangent = Mathf.Sign(tangent) * maxSlope * 3f;
                }
            }

            return tangent;
        }

        private static float ClampTangent(float tangent, float valA, float valB)
        {
            // If values are the same, tangent should be zero
            if (Mathf.Approximately(valA, valB))
                return 0f;
            return tangent;
        }

        /// <summary>
        /// Calculates smooth tangent as the average of in/out tangents.
        /// Used when switching to Free Smooth mode.
        /// </summary>
        public static float CalculateSmoothTangent(Keyframe key)
        {
            float inT = key.inTangent;
            float outT = key.outTangent;
            if (float.IsInfinity(inT)) inT = 0f;
            if (float.IsInfinity(outT)) outT = 0f;
            return (inT + outT) * 0.5f;
        }

        /// <summary>
        /// Sets up a newly added key's tangent modes based on its neighboring keys.
        /// Runtime replacement for CurveUtility.SetKeyModeFromContext.
        /// </summary>
        public static void SetKeyModeFromContext(AnimationCurve curve, int keyIndex)
        {
            Keyframe key = curve[keyIndex];
            bool hasBrokenNeighbor = false;
            bool hasAutoNeighbor = false;

            if (keyIndex > 0)
            {
                Keyframe prev = curve[keyIndex - 1];
                if (GetKeyBroken(prev))
                    hasBrokenNeighbor = true;
                var rightMode = GetKeyRightTangentMode(prev);
                if (rightMode == TangentMode.ClampedAuto || rightMode == TangentMode.Auto)
                    hasAutoNeighbor = true;
            }

            if (keyIndex < curve.length - 1)
            {
                Keyframe next = curve[keyIndex + 1];
                if (GetKeyBroken(next))
                    hasBrokenNeighbor = true;
                var leftMode = GetKeyLeftTangentMode(next);
                if (leftMode == TangentMode.ClampedAuto || leftMode == TangentMode.Auto)
                    hasAutoNeighbor = true;
            }

            SetKeyBroken(ref key, hasBrokenNeighbor);

            if (hasBrokenNeighbor && !hasAutoNeighbor)
            {
                if (keyIndex > 0)
                    SetKeyLeftTangentMode(ref key, GetKeyRightTangentMode(curve[keyIndex - 1]));
                if (keyIndex < curve.length - 1)
                    SetKeyRightTangentMode(ref key, GetKeyLeftTangentMode(curve[keyIndex + 1]));
                if (keyIndex == 0)
                    SetKeyLeftTangentMode(ref key, GetKeyRightTangentMode(key));
                if (keyIndex == curve.length - 1)
                    SetKeyRightTangentMode(ref key, GetKeyLeftTangentMode(key));
            }
            else
            {
                TangentMode mode = TangentMode.Free;
                bool allClampedAuto = true;
                bool allAuto = true;

                if (keyIndex > 0)
                {
                    var rm = GetKeyRightTangentMode(curve[keyIndex - 1]);
                    if (rm != TangentMode.ClampedAuto) allClampedAuto = false;
                    if (rm != TangentMode.Auto) allAuto = false;
                }
                if (keyIndex < curve.length - 1)
                {
                    var lm = GetKeyLeftTangentMode(curve[keyIndex + 1]);
                    if (lm != TangentMode.ClampedAuto) allClampedAuto = false;
                    if (lm != TangentMode.Auto) allAuto = false;
                }

                if (allClampedAuto) mode = TangentMode.ClampedAuto;
                else if (allAuto) mode = TangentMode.Auto;

                SetKeyLeftTangentMode(ref key, mode);
                SetKeyRightTangentMode(ref key, mode);
            }

            curve.MoveKey(keyIndex, key);
        }
    }
}
