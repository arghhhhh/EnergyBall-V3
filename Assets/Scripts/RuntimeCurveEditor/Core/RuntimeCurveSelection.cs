using System;
using System.Collections.Generic;

namespace RuntimeCurveEditor
{
    [Serializable]
    public class RuntimeCurveSelection
        : IComparable<RuntimeCurveSelection>,
            IEquatable<RuntimeCurveSelection>
    {
        public enum SelectionType
        {
            Key,
            InTangent,
            OutTangent,
        }

        public int curveID;
        public int key;
        public SelectionType type;
        public bool semiSelected;

        public RuntimeCurveSelection(int curveID, int key, SelectionType type)
        {
            this.curveID = curveID;
            this.key = key;
            this.type = type;
            semiSelected = false;
        }

        public int CompareTo(RuntimeCurveSelection other)
        {
            int cmp = curveID.CompareTo(other.curveID);
            if (cmp != 0)
                return cmp;
            cmp = key.CompareTo(other.key);
            if (cmp != 0)
                return cmp;
            return type.CompareTo(other.type);
        }

        public bool Equals(RuntimeCurveSelection other)
        {
            if (other == null)
                return false;
            return curveID == other.curveID && key == other.key && type == other.type;
        }

        public override bool Equals(object obj)
        {
            return obj is RuntimeCurveSelection other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + curveID;
                hash = hash * 31 + key;
                hash = hash * 31 + (int)type;
                return hash;
            }
        }
    }

    public class SelectionManager
    {
        public List<RuntimeCurveSelection> selectedKeys = new List<RuntimeCurveSelection>();

        public bool IsKeySelected(int curveID, int keyIndex)
        {
            for (int i = 0; i < selectedKeys.Count; i++)
            {
                var s = selectedKeys[i];
                if (
                    s.curveID == curveID
                    && s.key == keyIndex
                    && s.type == RuntimeCurveSelection.SelectionType.Key
                )
                    return true;
            }
            return false;
        }

        public bool IsAnyPartSelected(int curveID, int keyIndex)
        {
            for (int i = 0; i < selectedKeys.Count; i++)
            {
                var s = selectedKeys[i];
                if (s.curveID == curveID && s.key == keyIndex)
                    return true;
            }
            return false;
        }

        public void SelectKey(int curveID, int keyIndex, bool additive)
        {
            if (!additive)
                selectedKeys.Clear();

            var sel = new RuntimeCurveSelection(
                curveID,
                keyIndex,
                RuntimeCurveSelection.SelectionType.Key
            );
            if (!selectedKeys.Contains(sel))
                selectedKeys.Add(sel);
        }

        public void SelectTangent(
            int curveID,
            int keyIndex,
            RuntimeCurveSelection.SelectionType tangentType,
            bool additive
        )
        {
            if (!additive)
                selectedKeys.Clear();

            var sel = new RuntimeCurveSelection(curveID, keyIndex, tangentType);
            if (!selectedKeys.Contains(sel))
                selectedKeys.Add(sel);
        }

        public void DeselectKey(int curveID, int keyIndex)
        {
            selectedKeys.RemoveAll(s => s.curveID == curveID && s.key == keyIndex);
        }

        public void SelectNone()
        {
            selectedKeys.Clear();
        }

        public void SelectAll(int curveID, int keyCount)
        {
            selectedKeys.Clear();
            for (int i = 0; i < keyCount; i++)
                selectedKeys.Add(
                    new RuntimeCurveSelection(curveID, i, RuntimeCurveSelection.SelectionType.Key)
                );
        }

        public List<int> GetSelectedKeyIndices(int curveID)
        {
            var result = new List<int>();
            for (int i = 0; i < selectedKeys.Count; i++)
            {
                var s = selectedKeys[i];
                if (
                    s.curveID == curveID
                    && s.type == RuntimeCurveSelection.SelectionType.Key
                    && !result.Contains(s.key)
                )
                    result.Add(s.key);
            }
            result.Sort();
            return result;
        }

        public void UpdateKeyIndicesAfterRemoval(int curveID, int removedIndex)
        {
            for (int i = selectedKeys.Count - 1; i >= 0; i--)
            {
                var s = selectedKeys[i];
                if (s.curveID != curveID)
                    continue;
                if (s.key == removedIndex)
                    selectedKeys.RemoveAt(i);
                else if (s.key > removedIndex)
                    s.key--;
            }
        }

        public void UpdateKeyIndicesAfterInsertion(int curveID, int insertedIndex)
        {
            for (int i = 0; i < selectedKeys.Count; i++)
            {
                var s = selectedKeys[i];
                if (s.curveID == curveID && s.key >= insertedIndex)
                    s.key++;
            }
        }
    }
}
