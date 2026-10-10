using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives the particle layer its own post-processing. The Particles Camera (layer KinectOverlay)
/// renders into a screen-sized transparent RenderTexture with its own Volume Mask, and a
/// full-screen RawImage on a Screen Space Overlay canvas draws that texture over the Main
/// Camera's already post-processed feed. Screen Space Overlay UI is drawn after the camera's
/// post-processing, so neither look leaks into the other.
///
/// Requires URP's Post Processing "Alpha Processing" so the particle pass keeps its alpha. The
/// RawImage uses a premultiplied-alpha material: opaque, alpha-blended and additive particles
/// all land in the texture premultiplied over transparent black, and bloom / lens flare glow
/// (alpha 0) still adds light on top of the feed.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ParticleLayerCompositor : MonoBehaviour
{
    [Tooltip("Full-screen RawImage (premultiplied material) that shows the particle layer.")]
    public RawImage compositeImage;

    [Tooltip(
        "Depth bits of the particle texture. The body occluder and metaball bounds depth-test "
            + "inside this pass, so it needs a depth buffer."
    )]
    public int depthBits = 24;

    [Tooltip(
        "With Sync Particles To Feed on, seconds without a new camera feed frame before the "
            + "particle layer goes back to redrawing every frame (feed stalled or sensor lost)."
    )]
    public float feedStallTimeout = 0.2f;

    private Camera particleCamera;
    private RenderTexture target;
    private BodySourceManager feedSource;
    private int lastFeedFrame = -1;
    private float lastFeedFrameTime = float.NegativeInfinity;

    private void OnEnable()
    {
        particleCamera = GetComponent<Camera>();
        EnsureTarget();
    }

    private void OnDisable()
    {
        if (particleCamera != null)
        {
            particleCamera.targetTexture = null;
            particleCamera.enabled = true;
        }
        if (compositeImage != null)
            compositeImage.texture = null;
        ReleaseTarget();
    }

    private void LateUpdate()
    {
        // Output resolution changes (window resize, different display) recreate the texture.
        bool targetRecreated = EnsureTarget();

        // A camera that skips a frame leaves its texture untouched, so the canvas keeps showing
        // the last particle image. A new texture is empty and has to be drawn right away.
        particleCamera.enabled = targetRecreated || ShouldRenderThisFrame();
    }

    /// <summary>
    /// False on frames the particle layer holds its last image: Sync Particles To Feed is on and
    /// no camera feed frame arrived this frame. Feed frames are delivered during Update, so the
    /// count is current by LateUpdate and the particles change on the same frames as the feed.
    /// Both VFX graphs are set to Always Simulate, so they keep stepping while the camera is off.
    /// </summary>
    private bool ShouldRenderThisFrame()
    {
        var controller = SceneController.Instance;
        if (controller == null || !controller.HasFeature(SceneController.SceneFeature.CameraFeed))
            return true;

        var settings = controller.GetRuntimeSettings();
        if (!settings.syncParticlesToFeed || !settings.showCameraFeed)
            return true;

        if (feedSource == null && !controller.TryGetComponent(out feedSource))
            return true;

        if (feedSource.ColorFrameCount != lastFeedFrame)
        {
            lastFeedFrame = feedSource.ColorFrameCount;
            lastFeedFrameTime = Time.unscaledTime;
            return true;
        }

        // No feed frames for a while: don't freeze the particles along with the feed.
        return Time.unscaledTime - lastFeedFrameTime > feedStallTimeout;
    }

    private bool EnsureTarget()
    {
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        if (target != null && target.width == width && target.height == height)
            return false;

        ReleaseTarget();
        target = new RenderTexture(width, height, depthBits, RenderTextureFormat.ARGB32)
        {
            name = "Particle Layer",
        };
        target.Create();

        particleCamera.targetTexture = target;
        if (compositeImage != null)
            compositeImage.texture = target;
        return true;
    }

    private void ReleaseTarget()
    {
        if (target == null)
            return;
        target.Release();
        Destroy(target);
        target = null;
    }
}
