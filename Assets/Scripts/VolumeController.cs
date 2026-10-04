using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Pushes the two post-processing looks into their Volumes: <c>particlePostProcessing</c> into
/// <see cref="particleVolume"/> (seen only by the Particles Camera via its Volume Mask) and
/// <c>feedPostProcessing</c> into <see cref="feedVolume"/> (seen only by the Main Camera).
/// Scenes without a separate feed pass (the Dummy Scene) leave <see cref="feedVolume"/> empty and
/// only the particle look is applied.
/// </summary>
public class VolumeController : MonoBehaviour
{
    [Tooltip("Volume for the particle layer. Defaults to the Volume on this GameObject.")]
    public Volume particleVolume;

    [Tooltip("Volume for the camera feed. Leave empty in scenes without a separate feed pass.")]
    public Volume feedVolume;

    private readonly VolumeTarget particleTarget = new();
    private readonly VolumeTarget feedTarget = new();

    private void Start()
    {
        EnsureComponents();
    }

    /// <summary>
    /// Resolves the Volumes and their overrides. Safe to call in edit mode (the play-exit restore
    /// applies the working set to the profile assets before Start has run).
    /// </summary>
    private void EnsureComponents()
    {
        if (particleVolume == null)
            particleVolume = GetComponent<Volume>();
        particleTarget.Resolve(particleVolume);
        feedTarget.Resolve(feedVolume);
    }

    /// <summary>The Volume Profile assets this controller writes (for editor Undo).</summary>
    public VolumeProfile[] GetProfiles()
    {
        EnsureComponents();
        if (feedTarget.Profile != null && feedTarget.Profile != particleTarget.Profile)
            return new[] { particleTarget.Profile, feedTarget.Profile };
        return particleTarget.Profile != null ? new[] { particleTarget.Profile } : new VolumeProfile[0];
    }

    public void ApplyCurrentSettings(RuntimeSceneSettings settings)
    {
        EnsureComponents();
        particleTarget.Apply(settings.particlePostProcessing);
        feedTarget.Apply(settings.feedPostProcessing);
    }

    /// <summary>One Volume Profile and the overrides VolumeController drives on it.</summary>
    private class VolumeTarget
    {
        private Volume volume;
        private VolumeProfile resolvedProfile;
        private Bloom bloom;
        private ScreenSpaceLensFlare screenSpaceLensFlare;
        private ChromaticAberration chromaticAberration;
        private LensDistortion lensDistortion;
        private ColorAdjustments colorAdjustments;
        private WhiteBalance whiteBalance;

        public VolumeProfile Profile => resolvedProfile;

        public void Resolve(Volume target)
        {
            VolumeProfile profile = target != null ? target.profile : null;
            if (target == volume && profile == resolvedProfile)
                return;
            volume = target;
            resolvedProfile = profile;
            bloom = null;
            screenSpaceLensFlare = null;
            chromaticAberration = null;
            lensDistortion = null;
            colorAdjustments = null;
            whiteBalance = null;
            if (profile == null)
                return;
            profile.TryGet(out bloom);
            profile.TryGet(out screenSpaceLensFlare);
            profile.TryGet(out chromaticAberration);
            profile.TryGet(out lensDistortion);
            profile.TryGet(out colorAdjustments);
            profile.TryGet(out whiteBalance);
        }

        public void Apply(PostProcessSettings settings)
        {
            if (resolvedProfile == null || settings == null)
                return;

            if (bloom != null)
            {
                bloom.threshold.value = settings.bloomThreshold;
                bloom.intensity.value = settings.bloomIntensity;
                bloom.scatter.value = settings.bloomScatter;
            }

            if (screenSpaceLensFlare != null)
            {
                screenSpaceLensFlare.intensity.value = settings.lensFlareIntensity;
                screenSpaceLensFlare.firstFlareIntensity.value = settings.lensFlareRegularMultiplier;
                screenSpaceLensFlare.secondaryFlareIntensity.value =
                    settings.lensFlareReversedMultiplier;
                screenSpaceLensFlare.streaksIntensity.value = settings.lensFlareStreaksMultiplier;
                screenSpaceLensFlare.streaksLength.value = settings.lensFlareStreaksLength;
                screenSpaceLensFlare.streaksOrientation.value =
                    settings.lensFlareStreaksOrientation;
                screenSpaceLensFlare.streaksThreshold.value = settings.lensFlareStreaksThreshold;
            }

            if (chromaticAberration != null)
            {
                chromaticAberration.intensity.value = settings.lensFlareChromaticIntensity;
            }

            if (lensDistortion != null)
            {
                lensDistortion.intensity.value = settings.lensDistortionIntensity;
                lensDistortion.xMultiplier.value = settings.lensDistortionXMultiplier;
                lensDistortion.yMultiplier.value = settings.lensDistortionYMultiplier;
                lensDistortion.scale.value = settings.lensDistortionScale;
                lensDistortion.center.value = new Vector2(
                    settings.lensDistortionCenterX,
                    settings.lensDistortionCenterY
                );
            }

            if (colorAdjustments != null)
            {
                colorAdjustments.postExposure.value = settings.colorAdjustmentsPostExposure;
                colorAdjustments.contrast.value = settings.colorAdjustmentsContrast;
                colorAdjustments.hueShift.value = settings.colorAdjustmentsHueShift;
                colorAdjustments.saturation.value = settings.colorAdjustmentsSaturation;
            }

            if (whiteBalance != null)
            {
                whiteBalance.temperature.value = settings.whiteBalanceTemperature;
                whiteBalance.tint.value = settings.whiteBalanceTint;
            }

            // Mark the volume profile as dirty so changes persist in the inspector
#if UNITY_EDITOR
            EditorUtility.SetDirty(resolvedProfile);
#endif
        }
    }
}
