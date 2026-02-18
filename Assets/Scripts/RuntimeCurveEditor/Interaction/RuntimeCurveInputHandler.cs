using System.Collections.Generic;
using UnityEngine;

namespace RuntimeCurveEditor
{
    public class RuntimeCurveInputHandler
    {
        private enum DragMode
        {
            None,
            DraggingKeys,
            DraggingInTangent,
            DraggingOutTangent,
            Panning,
            MarqueeSelect,
        }

        private RuntimeCurveEditorCore editor;
        private DragMode dragMode = DragMode.None;
        private Vector2 dragStartMouse;
        private Vector2 dragStartShownMin;
        private Vector2 dragStartShownMax;
        private Dictionary<int, Keyframe> dragStartKeyframes = new Dictionary<int, Keyframe>();
        private int draggingTangentKeyIndex = -1;
        private float lastClickTime;
        private Vector2 lastClickPos;
        private const float DOUBLE_CLICK_TIME = 0.3f;
        private const float DOUBLE_CLICK_DIST = 5f;

        public bool isMarqueeSelecting => dragMode == DragMode.MarqueeSelect;
        public Rect marqueeRect;

        // Context menu state
        public bool showContextMenu;
        public Vector2 contextMenuPosition;
        public RuntimeCurveMenuManager menuManager;

        public RuntimeCurveInputHandler(RuntimeCurveEditorCore editor)
        {
            this.editor = editor;
            menuManager = new RuntimeCurveMenuManager(editor);
        }

        public void HandleInput(Rect curveArea)
        {
            Event e = Event.current;
            if (e == null)
                return;

            // Block all normal input when menu or edit key dialog is open
            if (menuManager.IsOpen || menuManager.IsEditKeyDialogOpen)
            {
                return;
            }

            if (!curveArea.Contains(e.mousePosition) && dragMode == DragMode.None)
                return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    HandleMouseDown(e, curveArea);
                    break;
                case EventType.MouseDrag:
                    HandleMouseDrag(e, curveArea);
                    break;
                case EventType.MouseUp:
                    HandleMouseUp(e, curveArea);
                    break;
                case EventType.ScrollWheel:
                    HandleScrollWheel(e, curveArea);
                    break;
                case EventType.KeyDown:
                    HandleKeyDown(e, curveArea);
                    break;
            }
        }

        private void HandleMouseDown(Event e, Rect curveArea)
        {
            // Right click - context menu
            if (e.button == 1)
            {
                contextMenuPosition = e.mousePosition;
                showContextMenu = true;
                e.Use();
                return;
            }

            // Middle click - pan
            if (e.button == 2 || (e.button == 0 && e.alt))
            {
                dragMode = DragMode.Panning;
                dragStartMouse = e.mousePosition;
                dragStartShownMin = editor.shownAreaMin;
                dragStartShownMax = editor.shownAreaMax;
                e.Use();
                return;
            }

            // Left click
            if (e.button == 0)
            {
                // Check for double click
                float timeSinceLastClick = Time.realtimeSinceStartup - lastClickTime;
                float distFromLastClick = Vector2.Distance(e.mousePosition, lastClickPos);
                bool isDoubleClick =
                    timeSinceLastClick < DOUBLE_CLICK_TIME && distFromLastClick < DOUBLE_CLICK_DIST;
                lastClickTime = Time.realtimeSinceStartup;
                lastClickPos = e.mousePosition;

                if (isDoubleClick)
                {
                    HandleDoubleClick(e, curveArea);
                    return;
                }

                // Check if clicking on a tangent handle
                int tangentKey;
                bool isInTangent;
                if (
                    TryHitTangentHandle(e.mousePosition, curveArea, out tangentKey, out isInTangent)
                )
                {
                    dragMode = isInTangent
                        ? DragMode.DraggingInTangent
                        : DragMode.DraggingOutTangent;
                    draggingTangentKeyIndex = tangentKey;
                    e.Use();
                    return;
                }

                // Check if clicking on a keyframe
                int hitKey = HitTestKeyframe(e.mousePosition, curveArea);
                if (hitKey >= 0)
                {
                    if (!editor.selection.IsKeySelected(editor.curveWrapper.id, hitKey))
                    {
                        editor.selection.SelectKey(editor.curveWrapper.id, hitKey, e.shift);
                    }
                    else if (e.shift)
                    {
                        editor.selection.DeselectKey(editor.curveWrapper.id, hitKey);
                        e.Use();
                        return;
                    }

                    // Start dragging keys
                    dragMode = DragMode.DraggingKeys;
                    dragStartMouse = e.mousePosition;
                    SaveDragStartKeyframes();
                    e.Use();
                    return;
                }

                // Hit nothing - start marquee or deselect
                if (!e.shift)
                    editor.selection.SelectNone();

                dragMode = DragMode.MarqueeSelect;
                dragStartMouse = e.mousePosition;
                marqueeRect = new Rect(e.mousePosition.x, e.mousePosition.y, 0, 0);
                e.Use();
            }
        }

        private void HandleMouseDrag(Event e, Rect curveArea)
        {
            switch (dragMode)
            {
                case DragMode.Panning:
                    HandlePanDrag(e, curveArea);
                    break;
                case DragMode.DraggingKeys:
                    HandleKeyDrag(e, curveArea);
                    break;
                case DragMode.DraggingInTangent:
                case DragMode.DraggingOutTangent:
                    HandleTangentDrag(e, curveArea);
                    break;
                case DragMode.MarqueeSelect:
                    HandleMarqueeDrag(e, curveArea);
                    break;
            }
        }

        private void HandleMouseUp(Event e, Rect curveArea)
        {
            if (dragMode == DragMode.MarqueeSelect)
            {
                FinishMarqueeSelect(curveArea);
            }

            dragMode = DragMode.None;
            draggingTangentKeyIndex = -1;
            dragStartKeyframes.Clear();
        }

        private void HandleScrollWheel(Event e, Rect curveArea)
        {
            float zoomDelta = e.delta.y * 0.05f;

            Vector2 mouseDrawing = RuntimeCurveRenderer.ViewToDrawing(
                e.mousePosition,
                curveArea,
                editor.shownAreaMin,
                editor.shownAreaMax
            );
            Vector2 range = editor.shownAreaMax - editor.shownAreaMin;

            float hZoom = e.shift ? 0f : zoomDelta;
            float vZoom = (e.control || e.command) ? 0f : zoomDelta;

            if (!editor.settings.hRangeLocked)
            {
                float newRangeX = range.x * (1f + hZoom);
                float pivotX = (mouseDrawing.x - editor.shownAreaMin.x) / range.x;
                editor.shownAreaMin.x = mouseDrawing.x - pivotX * newRangeX;
                editor.shownAreaMax.x = editor.shownAreaMin.x + newRangeX;
            }

            if (!editor.settings.vRangeLocked)
            {
                float newRangeY = range.y * (1f + vZoom);
                float pivotY = (mouseDrawing.y - editor.shownAreaMin.y) / range.y;
                editor.shownAreaMin.y = mouseDrawing.y - pivotY * newRangeY;
                editor.shownAreaMax.y = editor.shownAreaMin.y + newRangeY;
            }

            e.Use();
        }

        private void HandleKeyDown(Event e, Rect curveArea)
        {
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                DeleteSelectedKeys();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.A)
            {
                editor.FrameToFit();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.F)
            {
                editor.FrameSelected();
                e.Use();
                return;
            }
        }

        private void HandleDoubleClick(Event e, Rect curveArea)
        {
            Vector2 drawingPos = RuntimeCurveRenderer.ViewToDrawing(
                e.mousePosition,
                curveArea,
                editor.shownAreaMin,
                editor.shownAreaMax
            );

            AnimationCurve curve = editor.curveWrapper.curve;

            // Create keyframe with value from the curve at this time
            float value = curve.length > 0 ? curve.Evaluate(drawingPos.x) : drawingPos.y;
            Keyframe kf = new Keyframe(drawingPos.x, value);

            int newIndex = editor.curveWrapper.AddKey(kf);
            if (newIndex >= 0)
            {
                // Set tangent mode from surrounding context
                KeyframeTangentUtility.SetKeyModeFromContext(curve, newIndex);
                KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(curve, newIndex);

                editor.selection.UpdateKeyIndicesAfterInsertion(editor.curveWrapper.id, newIndex);
                editor.selection.SelectKey(editor.curveWrapper.id, newIndex, false);
            }

            e.Use();
        }

        private void HandlePanDrag(Event e, Rect curveArea)
        {
            Vector2 delta = e.mousePosition - dragStartMouse;
            Vector2 range = dragStartShownMax - dragStartShownMin;

            float dx = -delta.x / curveArea.width * range.x;
            float dy = delta.y / curveArea.height * range.y;

            if (!editor.settings.hRangeLocked)
            {
                editor.shownAreaMin.x = dragStartShownMin.x + dx;
                editor.shownAreaMax.x = dragStartShownMax.x + dx;
            }

            if (!editor.settings.vRangeLocked)
            {
                editor.shownAreaMin.y = dragStartShownMin.y + dy;
                editor.shownAreaMax.y = dragStartShownMax.y + dy;
            }

            e.Use();
        }

        private void HandleKeyDrag(Event e, Rect curveArea)
        {
            Vector2 mouseDelta = e.mousePosition - dragStartMouse;
            Vector2 range = editor.shownAreaMax - editor.shownAreaMin;
            float dtTime = mouseDelta.x / curveArea.width * range.x;
            float dtValue = -mouseDelta.y / curveArea.height * range.y;

            AnimationCurve curve = editor.curveWrapper.curve;
            var selectedIndices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);

            // Sort in reverse so index shifts don't corrupt earlier moves
            selectedIndices.Sort((a, b) => b.CompareTo(a));

            foreach (int idx in selectedIndices)
            {
                if (idx < 0 || idx >= curve.length)
                    continue;
                if (!dragStartKeyframes.ContainsKey(idx))
                    continue;

                Keyframe original = dragStartKeyframes[idx];
                Keyframe moved = original;
                moved.time = original.time + dtTime;
                moved.value = original.value + dtValue;

                // Clamp to value range if set
                if (editor.curveWrapper.vRangeMin != float.NegativeInfinity)
                    moved.value = Mathf.Max(moved.value, editor.curveWrapper.vRangeMin);
                if (editor.curveWrapper.vRangeMax != float.PositiveInfinity)
                    moved.value = Mathf.Min(moved.value, editor.curveWrapper.vRangeMax);

                curve.MoveKey(idx, moved);
                KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(curve, idx);
            }

            editor.curveWrapper.changed = true;
            e.Use();
        }

        private void HandleTangentDrag(Event e, Rect curveArea)
        {
            if (draggingTangentKeyIndex < 0)
                return;

            AnimationCurve curve = editor.curveWrapper.curve;
            if (draggingTangentKeyIndex >= curve.length)
                return;

            Keyframe kf = curve[draggingTangentKeyIndex];
            Vector2 keyDrawingPos = new Vector2(kf.time, kf.value);
            Vector2 mouseDrawingPos = RuntimeCurveRenderer.ViewToDrawing(
                e.mousePosition,
                curveArea,
                editor.shownAreaMin,
                editor.shownAreaMax
            );
            Vector2 delta = mouseDrawingPos - keyDrawingPos;

            bool isInTangent = dragMode == DragMode.DraggingInTangent;
            bool isBroken = KeyframeTangentUtility.GetKeyBroken(kf);

            if (isInTangent)
            {
                if (delta.x < -0.0001f)
                    kf.inTangent = delta.y / delta.x;
                else if (delta.y > 0)
                    kf.inTangent = float.PositiveInfinity;
                else
                    kf.inTangent = float.NegativeInfinity;

                KeyframeTangentUtility.SetKeyLeftTangentMode(
                    ref kf,
                    KeyframeTangentUtility.TangentMode.Free
                );

                // Handle weighted in-tangent
                if ((kf.weightedMode & WeightedMode.In) != 0 && draggingTangentKeyIndex > 0)
                {
                    float dt = kf.time - curve[draggingTangentKeyIndex - 1].time;
                    if (dt > 0.0001f)
                        kf.inWeight = Mathf.Clamp(Mathf.Abs(delta.x / dt), 0f, 1f);
                }

                if (!isBroken)
                {
                    kf.outTangent = kf.inTangent;
                    KeyframeTangentUtility.SetKeyRightTangentMode(
                        ref kf,
                        KeyframeTangentUtility.TangentMode.Free
                    );
                }
            }
            else
            {
                if (delta.x > 0.0001f)
                    kf.outTangent = delta.y / delta.x;
                else if (delta.y > 0)
                    kf.outTangent = float.PositiveInfinity;
                else
                    kf.outTangent = float.NegativeInfinity;

                KeyframeTangentUtility.SetKeyRightTangentMode(
                    ref kf,
                    KeyframeTangentUtility.TangentMode.Free
                );

                // Handle weighted out-tangent
                if (
                    (kf.weightedMode & WeightedMode.Out) != 0
                    && draggingTangentKeyIndex < curve.length - 1
                )
                {
                    float dt = curve[draggingTangentKeyIndex + 1].time - kf.time;
                    if (dt > 0.0001f)
                        kf.outWeight = Mathf.Clamp(Mathf.Abs(delta.x / dt), 0f, 1f);
                }

                if (!isBroken)
                {
                    kf.inTangent = kf.outTangent;
                    KeyframeTangentUtility.SetKeyLeftTangentMode(
                        ref kf,
                        KeyframeTangentUtility.TangentMode.Free
                    );
                }
            }

            curve.MoveKey(draggingTangentKeyIndex, kf);
            KeyframeTangentUtility.UpdateTangentsFromModeSurrounding(
                curve,
                draggingTangentKeyIndex
            );
            editor.curveWrapper.changed = true;
            e.Use();
        }

        private void HandleMarqueeDrag(Event e, Rect curveArea)
        {
            float x = Mathf.Min(dragStartMouse.x, e.mousePosition.x);
            float y = Mathf.Min(dragStartMouse.y, e.mousePosition.y);
            float w = Mathf.Abs(e.mousePosition.x - dragStartMouse.x);
            float h = Mathf.Abs(e.mousePosition.y - dragStartMouse.y);

            marqueeRect = new Rect(x, y, w, h);
            e.Use();
        }

        private void FinishMarqueeSelect(Rect curveArea)
        {
            if (marqueeRect.width < 2f && marqueeRect.height < 2f)
                return;

            Rect globalMarquee = marqueeRect;

            AnimationCurve curve = editor.curveWrapper.curve;
            if (curve == null)
                return;

            bool additive = Event.current != null && Event.current.shift;
            if (!additive)
                editor.selection.SelectNone();

            for (int i = 0; i < curve.length; i++)
            {
                Keyframe kf = curve[i];
                Vector2 viewPos = RuntimeCurveRenderer.DrawingToView(
                    new Vector2(kf.time, kf.value),
                    curveArea,
                    editor.shownAreaMin,
                    editor.shownAreaMax
                );

                if (globalMarquee.Contains(viewPos))
                {
                    editor.selection.SelectKey(editor.curveWrapper.id, i, true);
                }
            }
        }

        private int HitTestKeyframe(Vector2 mousePos, Rect curveArea)
        {
            AnimationCurve curve = editor.curveWrapper.curve;
            if (curve == null)
                return -1;

            float closestDist = float.MaxValue;
            int closestKey = -1;

            for (int i = 0; i < curve.length; i++)
            {
                Keyframe kf = curve[i];
                Vector2 viewPos = RuntimeCurveRenderer.DrawingToView(
                    new Vector2(kf.time, kf.value),
                    curveArea,
                    editor.shownAreaMin,
                    editor.shownAreaMax
                );

                Rect hitRect = RuntimeKeyframeRenderer.GetKeyframeRect(viewPos);
                if (hitRect.Contains(mousePos))
                {
                    float dist = Vector2.Distance(mousePos, viewPos);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestKey = i;
                    }
                }
            }

            return closestKey;
        }

        private bool TryHitTangentHandle(
            Vector2 mousePos,
            Rect curveArea,
            out int keyIndex,
            out bool isInTangent
        )
        {
            keyIndex = -1;
            isInTangent = false;

            AnimationCurve curve = editor.curveWrapper.curve;
            if (curve == null)
                return false;

            for (int i = 0; i < curve.length; i++)
            {
                if (!editor.selection.IsKeySelected(editor.curveWrapper.id, i))
                    continue;

                // Check in tangent
                if (i > 0)
                {
                    Vector2 inPos = editor.GetTangentHandlePosition(i, true, curveArea);
                    Rect hitRect = RuntimeKeyframeRenderer.GetTangentDotRect(inPos);
                    if (hitRect.Contains(mousePos))
                    {
                        keyIndex = i;
                        isInTangent = true;
                        return true;
                    }
                }

                // Check out tangent
                if (i < curve.length - 1)
                {
                    Vector2 outPos = editor.GetTangentHandlePosition(i, false, curveArea);
                    Rect hitRect = RuntimeKeyframeRenderer.GetTangentDotRect(outPos);
                    if (hitRect.Contains(mousePos))
                    {
                        keyIndex = i;
                        isInTangent = false;
                        return true;
                    }
                }
            }

            return false;
        }

        private void DeleteSelectedKeys()
        {
            AnimationCurve curve = editor.curveWrapper.curve;
            if (curve == null)
                return;

            var indices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);
            if (indices.Count == 0)
                return;

            // Don't delete last key unless allowed
            if (!editor.settings.allowDeleteLastKeyInCurve && curve.length - indices.Count < 1)
                return;

            // Remove from highest index to lowest to avoid index shifting
            indices.Sort((a, b) => b.CompareTo(a));
            foreach (int idx in indices)
            {
                if (idx >= 0 && idx < curve.length)
                {
                    curve.RemoveKey(idx);
                    editor.selection.UpdateKeyIndicesAfterRemoval(editor.curveWrapper.id, idx);
                }
            }

            editor.selection.SelectNone();
            editor.curveWrapper.changed = true;
        }

        private void SaveDragStartKeyframes()
        {
            dragStartKeyframes.Clear();
            var indices = editor.selection.GetSelectedKeyIndices(editor.curveWrapper.id);
            AnimationCurve curve = editor.curveWrapper.curve;

            foreach (int idx in indices)
            {
                if (idx >= 0 && idx < curve.length)
                    dragStartKeyframes[idx] = curve[idx];
            }
        }
    }
}
