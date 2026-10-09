using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuntimeColorEditor
{
    /// <summary>
    /// Shared chrome for the runtime color popups: a draggable title bar with a close button,
    /// an optional dimmed backdrop, and modal input (a click outside closes the window, other
    /// mouse input outside it is swallowed so the UI behind can't react).
    ///
    /// Stacking uses <see cref="GUI.depth"/>: a lower depth draws on top and gets input first,
    /// so the color picker opened from the gradient editor sits above it and closes on its own
    /// before the gradient editor sees the click.
    /// </summary>
    public abstract class RuntimePopupWindow : MonoBehaviour
    {
        protected Rect windowRect;
        protected bool isVisible;

        // The window outlives scenes (DontDestroyOnLoad) but its callbacks point into the scene,
        // so a scene change (e.g. the right-click reload) closes it.
        protected virtual void OnEnable() =>
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

        protected virtual void OnDisable() =>
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (isVisible)
                Hide();
        }

        private bool isDraggingWindow;
        private Vector2 dragOffset;

        protected static float TitleBarHeight => 24f * RuntimeColorGUI.UIScale;
        protected static float Padding => 10f * RuntimeColorGUI.UIScale;

        protected abstract string Title { get; }

        /// <summary>IMGUI depth. Lower draws on top and receives input first.</summary>
        protected abstract int Depth { get; }

        protected virtual bool DimBackground => true;

        /// <summary>
        /// While true the window draws but ignores input (another popup on top of it is open).
        /// </summary>
        protected virtual bool InputBlocked => false;

        /// <summary>Draw the window body. <paramref name="contentRect"/> is below the title bar.</summary>
        protected abstract void DrawContents(Rect contentRect);

        /// <summary>The full window height for the current content (title bar included).</summary>
        protected abstract float CalcHeight();

        protected abstract void OnEscape();

        public virtual void Hide()
        {
            isVisible = false;
            isDraggingWindow = false;
            RuntimeColorGUI.EndTextEditing();
        }

        protected void PlaceWindow(float width, Rect? nextTo)
        {
            float height = CalcHeight();
            if (nextTo.HasValue)
            {
                // Beside the other window: right if there's room, otherwise left.
                Rect other = nextTo.Value;
                float gap = 6f * RuntimeColorGUI.UIScale;
                float x = other.xMax + gap;
                if (x + width > Screen.width)
                    x = other.x - gap - width;
                windowRect = new Rect(x, other.y, width, height);
            }
            else
            {
                windowRect = new Rect(
                    (Screen.width - width) / 2f,
                    (Screen.height - height) / 2f,
                    width,
                    height
                );
            }
            ClampToScreen();
            isDraggingWindow = false;
        }

        private void ClampToScreen()
        {
            windowRect.x = Mathf.Clamp(
                windowRect.x,
                0f,
                Mathf.Max(0f, Screen.width - windowRect.width)
            );
            windowRect.y = Mathf.Clamp(
                windowRect.y,
                0f,
                Mathf.Max(0f, Screen.height - windowRect.height)
            );
        }

        protected virtual void OnGUI()
        {
            if (!isVisible)
                return;

            GUI.depth = Depth;
            RuntimeColorGUI.EnsureStyles();
            windowRect.height = CalcHeight();

            Event e = Event.current;
            bool blocked = InputBlocked;

            if (DimBackground)
                RuntimeColorGUI.DrawRect(
                    new Rect(0, 0, Screen.width, Screen.height),
                    new Color(0f, 0f, 0f, 0.3f)
                );

            if (!blocked)
            {
                // A control being dragged (hotControl) keeps getting events outside the window,
                // so a slider released off-window still sees its MouseUp.
                if (
                    e.isMouse
                    && !windowRect.Contains(e.mousePosition)
                    && !isDraggingWindow
                    && GUIUtility.hotControl == 0
                )
                {
                    if (e.type == EventType.MouseDown)
                    {
                        Hide();
                        e.Use();
                        return;
                    }
                    // Swallow every other mouse event outside the window (drag, up, ...)
                    e.Use();
                }
                if (e.type == EventType.ScrollWheel && !windowRect.Contains(e.mousePosition))
                    e.Use();

                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Escape)
                    {
                        if (RuntimeColorGUI.IsEditingText)
                            RuntimeColorGUI.EndTextEditing();
                        else
                            OnEscape();
                        e.Use();
                        return;
                    }
                    if (
                        (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                        && RuntimeColorGUI.IsEditingText
                    )
                    {
                        RuntimeColorGUI.EndTextEditing();
                        e.Use();
                    }
                }
            }

            float scale = RuntimeColorGUI.UIScale;
            Rect titleRect = new Rect(windowRect.x, windowRect.y, windowRect.width, TitleBarHeight);
            float closeSize = 18f * scale;
            Rect closeRect = new Rect(
                windowRect.xMax - closeSize - 4f * scale,
                windowRect.y + (TitleBarHeight - closeSize) / 2f,
                closeSize,
                closeSize
            );

            if (!blocked)
                HandleWindowDrag(titleRect, closeRect);

            RuntimeColorGUI.DrawRect(windowRect, RuntimeColorGUI.WindowBackground);
            RuntimeColorGUI.DrawBorder(windowRect, RuntimeColorGUI.WindowBorder);
            RuntimeColorGUI.DrawRect(titleRect, RuntimeColorGUI.TitleBackground);
            GUI.Label(
                new Rect(
                    titleRect.x + 8f * scale,
                    titleRect.y,
                    titleRect.width - 40f * scale,
                    titleRect.height
                ),
                Title,
                RuntimeColorGUI.Title
            );

            // While blocked, swallow input aimed at this window so its controls don't react.
            if (blocked && (e.isMouse || e.isKey || e.type == EventType.ScrollWheel))
            {
                bool wasEnabled = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(closeRect, "x", RuntimeColorGUI.Button);
                DrawContents(
                    new Rect(
                        windowRect.x,
                        titleRect.yMax,
                        windowRect.width,
                        windowRect.height - TitleBarHeight
                    )
                );
                GUI.enabled = wasEnabled;
                return;
            }

            if (GUI.Button(closeRect, "x", RuntimeColorGUI.Button))
            {
                Hide();
                return;
            }

            DrawContents(
                new Rect(
                    windowRect.x,
                    titleRect.yMax,
                    windowRect.width,
                    windowRect.height - TitleBarHeight
                )
            );

            // Clicks inside the window that no control used must not reach the UI behind.
            if (
                !blocked
                && e.isMouse
                && windowRect.Contains(e.mousePosition)
                && e.type != EventType.Used
            )
            {
                if (e.type == EventType.MouseDown)
                    RuntimeColorGUI.EndTextEditing();
                e.Use();
            }
        }

        private void HandleWindowDrag(Rect titleRect, Rect closeRect)
        {
            Event e = Event.current;
            if (
                e.type == EventType.MouseDown
                && e.button == 0
                && titleRect.Contains(e.mousePosition)
                && !closeRect.Contains(e.mousePosition)
            )
            {
                isDraggingWindow = true;
                dragOffset = e.mousePosition - windowRect.position;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isDraggingWindow)
            {
                windowRect.position = e.mousePosition - dragOffset;
                ClampToScreen();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && isDraggingWindow)
            {
                isDraggingWindow = false;
                e.Use();
            }
        }

        public Rect WindowRect => windowRect;
    }
}
