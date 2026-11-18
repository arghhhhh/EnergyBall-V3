using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Makes the particle layer's post-processing grade the particles fully.
///
/// URP Alpha Processing (needed so the particle layer composites over the feed) applies color
/// grading in proportion to each pixel's alpha. Additive particles write almost no alpha, so
/// saturation, exposure, white balance etc. barely reached them. On cameras with a
/// <see cref="ParticleLayerCompositor"/> this feature saves the frame before post-processing,
/// makes the whole layer opaque so the grade applies everywhere (particles and the bloom / lens
/// flare glow around them; empty pixels are black and stay black), then restores the saved alpha
/// after post-processing, so the layer still composites additively as before. Bloom also sees
/// the particles at full strength now.
///
/// Cost: two full-screen copies of the particle layer and one temporary texture, only on that
/// camera and only on frames it renders.
/// </summary>
public class ParticleAlphaGradingFeature : ScriptableRendererFeature
{
    [Tooltip("Hidden/EnergyBall/ParticleAlphaGrading (found by name when empty).")]
    [SerializeField]
    private Shader shader;

    private Material material;
    private SavePass savePass;
    private RestorePass restorePass;

    private static readonly int SavedColorId = Shader.PropertyToID("_SavedColor");

    /// <summary>The pre-post-processing frame, handed from the save pass to the restore pass.</summary>
    private class SavedFrame : ContextItem
    {
        public TextureHandle color;

        public override void Reset()
        {
            color = TextureHandle.nullHandle;
        }
    }

    public override void Create()
    {
        if (shader == null)
            shader = Shader.Find("Hidden/EnergyBall/ParticleAlphaGrading");
        if (shader == null)
            return;

        CoreUtils.Destroy(material);
        material = CoreUtils.CreateEngineMaterial(shader);

        savePass = new SavePass(material)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing,
            requiresIntermediateTexture = true,
        };
        restorePass = new RestorePass(material)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
            requiresIntermediateTexture = true,
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null)
            return;

        ref CameraData cameraData = ref renderingData.cameraData;
        if (!cameraData.postProcessEnabled || cameraData.cameraType != CameraType.Game)
            return;
        if (!cameraData.camera.TryGetComponent(out ParticleLayerCompositor _))
            return;

        renderer.EnqueuePass(savePass);
        renderer.EnqueuePass(restorePass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        material = null;
    }

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle saved;
        public Material material;
    }

    /// <summary>Saves the frame, then rewrites it fully opaque.</summary>
    private class SavePass : ScriptableRenderPass
    {
        private readonly Material material;

        public SavePass(Material material)
        {
            this.material = material;
            profilingSampler = new ProfilingSampler("Particle Alpha: Save");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer)
                return;

            TextureHandle color = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(color);
            desc.name = "Particle Layer (pre post-processing)";
            desc.clearBuffer = false;
            TextureHandle saved = renderGraph.CreateTexture(desc);

            renderGraph.AddBlitPass(color, saved, Vector2.one, Vector2.zero, passName: "Particle Alpha: Copy");
            renderGraph.AddBlitPass(
                new RenderGraphUtils.BlitMaterialParameters(saved, color, material, 0),
                "Particle Alpha: Opaque"
            );

            frameData.Create<SavedFrame>().color = saved;
        }
    }

    /// <summary>Puts the saved alpha back under the post-processed color.</summary>
    private class RestorePass : ScriptableRenderPass
    {
        private readonly Material material;

        public RestorePass(Material material)
        {
            this.material = material;
            profilingSampler = new ProfilingSampler("Particle Alpha: Restore");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!frameData.Contains<SavedFrame>())
                return;
            TextureHandle saved = frameData.Get<SavedFrame>().color;
            if (!saved.IsValid())
                return;

            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer)
                return;

            TextureHandle color = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(color);
            desc.name = "Particle Layer (post-processed)";
            desc.clearBuffer = false;
            TextureHandle graded = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(color, graded, Vector2.one, Vector2.zero, passName: "Particle Alpha: Copy Graded");

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                "Particle Alpha: Restore",
                out var passData,
                profilingSampler
            );
            passData.source = graded;
            passData.saved = saved;
            passData.material = material;
            builder.UseTexture(graded);
            builder.UseTexture(saved);
            builder.SetRenderAttachment(color, 0);
            builder.SetRenderFunc(
                (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetTexture(SavedColorId, (RTHandle)data.saved);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 1);
                }
            );
        }
    }
}
