using System;
using UnityEngine;

namespace RuntimeCurveEditor
{
    public class RuntimeCurveEditorWindow : MonoBehaviour
    {
        private static RuntimeCurveEditorWindow s_Instance;

        private RuntimeCurveEditorCore curveEditor;
        private RuntimeCurvePresets presets;
        private RuntimeCurveEditorSettings settings;

        private AnimationCurve targetCurve;
        private Action<AnimationCurve> onCurveChanged;

        private bool isVisible;
        private Rect windowRect;

        private const float MIN_WIDTH = 420f;
        private const float MIN_HEIGHT = 340f;
        private const float PRESET_BAR_HEIGHT = 50f;
        private const float TITLE_BAR_HEIGHT = 20f;

        // Manual drag state (replaces GUI.DragWindow)
        private bool isDraggingWindow;
        private Vector2 dragOffset;

        private static GUIStyle s_TitleStyle;
        private static GUIStyle s_CloseButtonStyle;

        public static RuntimeCurveEditorWindow Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    GameObject go = new GameObject("[RuntimeCurveEditor]");
                    DontDestroyOnLoad(go);
                    s_Instance = go.AddComponent<RuntimeCurveEditorWindow>();
                }
                return s_Instance;
            }
        }

        public static bool IsVisible => s_Instance != null && s_Instance.isVisible;

        public static void Show(AnimationCurve curve, Action<AnimationCurve> onChanged, RuntimeCurveEditorSettings settings = null, Rect? anchorRect = null)
        {
            var instance = Instance;
            instance.settings = settings ?? RuntimeCurveEditorSettings.DefaultUnbounded();
            instance.targetCurve = curve;
            instance.onCurveChanged = onChanged;

            instance.curveEditor = new RuntimeCurveEditorCore(curve, new Color(0f, 0.8f, 0f, 1f), instance.settings);
            instance.curveEditor.onCurveChanged = () =>
            {
                instance.onCurveChanged?.Invoke(instance.targetCurve);
            };

            instance.presets = new RuntimeCurvePresets(instance.settings);

            // Position window
            if (anchorRect.HasValue)
            {
                Rect anchor = anchorRect.Value;
                instance.windowRect = new Rect(
                    anchor.x,
                    anchor.yMax + 2f,
                    Mathf.Max(MIN_WIDTH, anchor.width),
                    MIN_HEIGHT);
            }
            else
            {
                instance.windowRect = new Rect(
                    (Screen.width - MIN_WIDTH) / 2f,
                    (Screen.height - MIN_HEIGHT) / 2f,
                    MIN_WIDTH,
                    MIN_HEIGHT);
            }

            // Clamp to screen
            instance.windowRect.x = Mathf.Clamp(instance.windowRect.x, 0f, Screen.width - instance.windowRect.width);
            instance.windowRect.y = Mathf.Clamp(instance.windowRect.y, 0f, Screen.height - instance.windowRect.height);

            instance.isDraggingWindow = false;
            instance.isVisible = true;
        }

        public static void Hide()
        {
            if (s_Instance != null)
            {
                s_Instance.isVisible = false;
                s_Instance.isDraggingWindow = false;
                s_Instance.curveEditor = null;
                s_Instance.presets = null;
            }
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        private void OnGUI()
        {
            if (!isVisible || curveEditor == null) return;

            EnsureStyles();

            // Ensure minimum size
            windowRect.width = Mathf.Max(windowRect.width, MIN_WIDTH);
            windowRect.height = Mathf.Max(windowRect.height, MIN_HEIGHT);

            // Draw semi-transparent overlay
            GUI.color = new Color(0f, 0f, 0f, 0.3f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Handle title bar dragging (before click-outside-to-close)
            Rect titleRect = new Rect(windowRect.x, windowRect.y, windowRect.width, TITLE_BAR_HEIGHT);
            HandleWindowDrag(titleRect);

            // Click outside to close (only if not dragging, no context menu, and no preset dialog)
            if (!isDraggingWindow &&
                Event.current.type == EventType.MouseDown &&
                !windowRect.Contains(Event.current.mousePosition) &&
                !curveEditor.inputHandler.showContextMenu &&
                !curveEditor.inputHandler.menuManager.IsOpen &&
                (presets == null || !presets.IsDialogOpen))
            {
                Hide();
                Event.current.Use();
                return;
            }

            // --- Draw window background ---
            GUI.color = new Color(0.16f, 0.16f, 0.16f, 1f);
            GUI.DrawTexture(windowRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Draw border
            DrawWindowBorder();

            // --- Title bar ---
            GUI.color = new Color(0.18f, 0.18f, 0.18f, 1f);
            GUI.DrawTexture(titleRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(windowRect.x + 8f, windowRect.y + 1f, windowRect.width - 30f, TITLE_BAR_HEIGHT), "Curve", s_TitleStyle);

            // Close button
            Rect closeRect = new Rect(windowRect.xMax - 22f, windowRect.y + 2f, 18f, 16f);
            if (GUI.Button(closeRect, "x", s_CloseButtonStyle))
            {
                Hide();
                return;
            }

            // --- Curve editor area (screen coordinates) ---
            float curveAreaTop = windowRect.y + TITLE_BAR_HEIGHT;
            float curveAreaHeight = windowRect.height - TITLE_BAR_HEIGHT - PRESET_BAR_HEIGHT;
            Rect curveArea = new Rect(windowRect.x, curveAreaTop, windowRect.width, curveAreaHeight);

            // Skip curve editor when a preset dialog is open (dialog overlaps curve area
            // and would consume input events meant for the dialog buttons)
            if (presets == null || !presets.IsDialogOpen)
                curveEditor.OnGUI(curveArea);

            // --- Preset bar (screen coordinates) ---
            Rect presetBarRect = new Rect(
                windowRect.x,
                windowRect.yMax - PRESET_BAR_HEIGHT,
                windowRect.width,
                PRESET_BAR_HEIGHT);

            presets.OnGUI(presetBarRect, OnPresetSelected, targetCurve);

            // --- Context menu (drawn last, on top of everything) ---
            if (curveEditor.inputHandler.showContextMenu)
            {
                curveEditor.inputHandler.menuManager.ShowContextMenu(curveEditor.inputHandler.contextMenuPosition);
                curveEditor.inputHandler.showContextMenu = false;
            }

            if (curveEditor.inputHandler.menuManager.IsOpen)
            {
                curveEditor.inputHandler.menuManager.OnGUI();
            }
        }

        private void HandleWindowDrag(Rect titleRect)
        {
            Event e = Event.current;

            Rect closeRect = new Rect(windowRect.xMax - 22f, windowRect.y + 2f, 18f, 16f);
            if (e.type == EventType.MouseDown && e.button == 0 && titleRect.Contains(e.mousePosition) && !closeRect.Contains(e.mousePosition))
            {
                isDraggingWindow = true;
                dragOffset = e.mousePosition - new Vector2(windowRect.x, windowRect.y);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isDraggingWindow)
            {
                windowRect.x = e.mousePosition.x - dragOffset.x;
                windowRect.y = e.mousePosition.y - dragOffset.y;

                // Clamp to screen
                windowRect.x = Mathf.Clamp(windowRect.x, 0f, Screen.width - windowRect.width);
                windowRect.y = Mathf.Clamp(windowRect.y, 0f, Screen.height - windowRect.height);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && isDraggingWindow)
            {
                isDraggingWindow = false;
            }
        }

        private void DrawWindowBorder()
        {
            Color borderColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
            RuntimeCurveRenderer.DrawLine(new Vector2(windowRect.x, windowRect.y), new Vector2(windowRect.xMax, windowRect.y), borderColor);
            RuntimeCurveRenderer.DrawLine(new Vector2(windowRect.xMax, windowRect.y), new Vector2(windowRect.xMax, windowRect.yMax), borderColor);
            RuntimeCurveRenderer.DrawLine(new Vector2(windowRect.xMax, windowRect.yMax), new Vector2(windowRect.x, windowRect.yMax), borderColor);
            RuntimeCurveRenderer.DrawLine(new Vector2(windowRect.x, windowRect.yMax), new Vector2(windowRect.x, windowRect.y), borderColor);
        }

        private void OnPresetSelected(AnimationCurve presetCurve)
        {
            if (targetCurve == null) return;

            // Copy keys from preset to target
            while (targetCurve.length > 0)
                targetCurve.RemoveKey(0);

            for (int i = 0; i < presetCurve.length; i++)
                targetCurve.AddKey(presetCurve[i]);

            targetCurve.preWrapMode = presetCurve.preWrapMode;
            targetCurve.postWrapMode = presetCurve.postWrapMode;

            // Refresh editor
            curveEditor.SetCurve(targetCurve, curveEditor.curveWrapper.color);
            onCurveChanged?.Invoke(targetCurve);
        }

        private static void EnsureStyles()
        {
            if (s_TitleStyle != null) return;

            s_TitleStyle = new GUIStyle(GUI.skin.label);
            s_TitleStyle.fontSize = 12;
            s_TitleStyle.fontStyle = FontStyle.Bold;
            s_TitleStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            s_TitleStyle.alignment = TextAnchor.MiddleLeft;

            s_CloseButtonStyle = new GUIStyle(GUI.skin.button);
            s_CloseButtonStyle.fontSize = 11;
            s_CloseButtonStyle.fontStyle = FontStyle.Bold;
            s_CloseButtonStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            s_CloseButtonStyle.alignment = TextAnchor.MiddleCenter;
            s_CloseButtonStyle.padding = new RectOffset(0, 0, 0, 2);
        }
    }
}
