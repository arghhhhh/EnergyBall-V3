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

    private Camera particleCamera;
    private RenderTexture target;

    private void OnEnable()
    {
        particleCamera = GetComponent<Camera>();
        EnsureTarget();
    }

    private void OnDisable()
    {
        if (particleCamera != null)
            particleCamera.targetTexture = null;
        if (compositeImage != null)
            compositeImage.texture = null;
        ReleaseTarget();
    }

    private void LateUpdate()
    {
        // Output resolution changes (window resize, different display) recreate the texture.
        EnsureTarget();
    }

    private void EnsureTarget()
    {
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        if (target != null && target.width == width && target.height == height)
            return;

        ReleaseTarget();
        target = new RenderTexture(width, height, depthBits, RenderTextureFormat.ARGB32)
        {
            name = "Particle Layer",
        };
        target.Create();

        particleCamera.targetTexture = target;
        if (compositeImage != null)
            compositeImage.texture = target;
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
