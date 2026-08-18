using UnityEngine;

/// <summary>
/// Drives the Dummy Scene's orthographic camera from a size authored at
/// bodyScale = 1, multiplied by the effective bodyScale at runtime — the camera
/// counterpart of <see cref="DummyTransformer"/>. The main scene's perspective
/// camera keeps alignment for free because the world scales about the origin;
/// an orthographic camera's view height must follow explicitly or the whole
/// scene appears to shrink/grow when bodyScale changes. OnValidate previews the
/// unscaled size in the editor. Allocation-free check every frame.
/// </summary>
[RequireComponent(typeof(Camera))]
public class DummyCameraScaler : MonoBehaviour
{
    [Tooltip("Orthographic half-height at bodyScale 1 (multiplied by bodyScale at runtime).")]
    public float orthographicSize = 0.95f;

    private Camera cam;

    // Last effective bodyScale the camera was sized for (0 forces the first apply).
    private float appliedBodyScale = 0f;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Update()
    {
        if (SceneController.Instance == null)
            return;
        float bodyScale = SceneController.Instance.GetRuntimeSettings().bodyScale;
        if (bodyScale <= 0f)
            bodyScale = 1f;
        if (Mathf.Approximately(bodyScale, appliedBodyScale))
            return;

        cam.orthographicSize = orthographicSize * bodyScale;
        appliedBodyScale = bodyScale;
    }

    void OnValidate()
    {
        if (Application.isPlaying)
            return;
        if (TryGetComponent<Camera>(out var editorCam) && editorCam.orthographic)
            editorCam.orthographicSize = orthographicSize;
    }
}
