using System;
using UnityEngine;

namespace RuntimeCurveEditor
{
    [Serializable]
    public class RuntimeCurveWrapper
    {
        public enum SelectionMode
        {
            None,
            Selected,
            SemiSelected
        }

        public int id;
        public int groupId = -1;
        public Color color = Color.green;
        public bool readOnly;
        public bool hidden;
        public SelectionMode selected;

        public float vRangeMin = float.NegativeInfinity;
        public float vRangeMax = float.PositiveInfinity;

        private AnimationCurve m_Curve;
        private bool m_Changed;

        public AnimationCurve curve
        {
            get => m_Curve;
            set => m_Curve = value;
        }

        public bool changed
        {
            get => m_Changed;
            set => m_Changed = value;
        }

        public RuntimeCurveWrapper()
        {
            id = 0;
            groupId = -1;
            hidden = false;
            readOnly = false;
        }

        public RuntimeCurveWrapper(AnimationCurve curve, Color color)
        {
            m_Curve = curve;
            this.color = color;
            id = "Curve".GetHashCode();
            groupId = -1;
        }

        public int AddKey(Keyframe key)
        {
            int index = m_Curve.AddKey(key);
            m_Changed = true;
            return index;
        }

        public int MoveKey(int index, Keyframe key)
        {
            int newIndex = m_Curve.MoveKey(index, key);
            m_Changed = true;
            return newIndex;
        }

        public void RemoveKey(int index)
        {
            m_Curve.RemoveKey(index);
            m_Changed = true;
        }
    }
}
