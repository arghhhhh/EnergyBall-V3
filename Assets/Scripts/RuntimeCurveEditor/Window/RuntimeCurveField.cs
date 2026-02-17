using System;
using UnityEngine;

namespace RuntimeCurveEditor
{
    public class RuntimeCurveField : MonoBehaviour
    {
        [Header("Curve Data")]
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Appearance")]
        public Color curveColor = new Color(0f, 0.8f, 0f, 1f);
        public float thumbnailWidth = 80f;
        public float thumbnailHeight = 20f;
        public Vector2 screenPosition = new Vector2(20f, 20f);

        [Header("Settings")]
        public bool normalizedRange = true;
        public float hRangeMin = 0f;
        public float hRangeMax = 1f;
        public float vRangeMin = 0f;
        public float vRangeMax = 1f;

        [Header("Events")]
        public Action<AnimationCurve> onCurveChanged;

        private RuntimeCurveEditorSettings editorSettings;
        private static GUIStyle s_ThumbnailStyle;
        private static GUIStyle s_LabelStyle;

        private void Start()
        {
            UpdateSettings();
        }

        private void UpdateSettings()
        {
            if (normalizedRange)
            {
                editorSettings = new RuntimeCurveEditorSettings
                {
                    hRangeMin = hRangeMin,
                    hRangeMax = hRangeMax,
                    vRangeMin = vRangeMin,
                    vRangeMax = vRangeMax
                };
            }
            else
            {
                editorSettings = RuntimeCurveEditorSettings.DefaultUnbounded();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            Rect thumbnailRect = new Rect(screenPosition.x, screenPosition.y, thumbnailWidth, thumbnailHeight);

            // Background
            bool hovered = thumbnailRect.Contains(Event.current.mousePosition);
            Color bgColor = hovered
                ? new Color(0.28f, 0.28f, 0.28f, 1f)
                : new Color(0.22f, 0.22f, 0.22f, 1f);

            GUI.color = bgColor;
            GUI.DrawTexture(thumbnailRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Border
            Color borderColor = hovered
                ? new Color(0.5f, 0.5f, 0.5f, 0.8f)
                : new Color(0.35f, 0.35f, 0.35f, 0.6f);

            DrawRectBorder(thumbnailRect, borderColor);

            // Mini curve preview
            if (curve != null && curve.length > 0)
            {
                Rect curveRect = new Rect(
                    thumbnailRect.x + 2f,
                    thumbnailRect.y + 2f,
                    thumbnailRect.width - 4f,
                    thumbnailRect.height - 4f);

                RuntimeCurveRenderer.DrawMiniCurve(curve, curveRect, curveColor);
            }

            // Click to open editor
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                thumbnailRect.Contains(Event.current.mousePosition) &&
                !RuntimeCurveEditorWindow.IsVisible)
            {
                UpdateSettings();
                RuntimeCurveEditorWindow.Show(curve, OnEditorCurveChanged, editorSettings, thumbnailRect);
                Event.current.Use();
            }
        }

        private void OnEditorCurveChanged(AnimationCurve changedCurve)
        {
            onCurveChanged?.Invoke(changedCurve);
        }

        private static void DrawRectBorder(Rect rect, Color color)
        {
            RuntimeCurveRenderer.DrawLine(new Vector2(rect.x, rect.y), new Vector2(rect.xMax, rect.y), color);
            RuntimeCurveRenderer.DrawLine(new Vector2(rect.xMax, rect.y), new Vector2(rect.xMax, rect.yMax), color);
            RuntimeCurveRenderer.DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.x, rect.yMax), color);
            RuntimeCurveRenderer.DrawLine(new Vector2(rect.x, rect.yMax), new Vector2(rect.x, rect.y), color);
        }

        private static void EnsureStyles()
        {
            if (s_ThumbnailStyle != null) return;

            s_ThumbnailStyle = new GUIStyle();
            Texture2D bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.22f, 0.22f, 0.22f, 1f));
            bgTex.Apply();
            bgTex.hideFlags = HideFlags.HideAndDontSave;
            s_ThumbnailStyle.normal.background = bgTex;

            s_LabelStyle = new GUIStyle(GUI.skin.label);
            s_LabelStyle.fontSize = 9;
            s_LabelStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        }
    }
}
