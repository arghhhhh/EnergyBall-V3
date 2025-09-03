using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumeController : MonoBehaviour
{
    private Volume volume;
    private Bloom bloom;
    private Vignette vignette;
    private ScreenSpaceLensFlare screenSpaceLensFlare;
    private ChromaticAberration chromaticAberration;
    private LensDistortion lensDistortion;
    private ColorAdjustments colorAdjustments;
    private WhiteBalance whiteBalance;

    private void Start()
    {
        volume = GetComponent<Volume>();
        if (volume != null && volume.profile != null)
        {
            volume.profile.TryGet(out bloom);
            volume.profile.TryGet(out vignette);
            volume.profile.TryGet(out screenSpaceLensFlare);
            volume.profile.TryGet(out chromaticAberration);
            volume.profile.TryGet(out lensDistortion);
            volume.profile.TryGet(out colorAdjustments);
            volume.profile.TryGet(out whiteBalance);
        }
        else
        {
            Debug.LogError("VolumeController: No Volume component or profile found!");
        }
    }

    public void UpdateBloomSettings(RuntimeSceneSettings settings)
    {
        if (bloom != null)
        {
            bloom.threshold.value = settings.bloomThreshold;
            bloom.intensity.value = settings.bloomIntensity;
            bloom.scatter.value = settings.bloomScatter;
        }
    }

    public void UpdateLensFlareSettings(RuntimeSceneSettings settings)
    {
        if (screenSpaceLensFlare != null)
        {
            screenSpaceLensFlare.intensity.value = settings.lensFlareIntensity;
            screenSpaceLensFlare.firstFlareIntensity.value = settings.lensFlareRegularMultiplier;
            screenSpaceLensFlare.secondaryFlareIntensity.value = settings.lensFlareReversedMultiplier;
            screenSpaceLensFlare.streaksIntensity.value = settings.lensFlareStreaksMultiplier;
            screenSpaceLensFlare.streaksLength.value = settings.lensFlareStreaksLength;
            screenSpaceLensFlare.streaksOrientation.value = settings.lensFlareStreaksOrientation;
            screenSpaceLensFlare.streaksThreshold.value = settings.lensFlareStreaksThreshold;
        }
        
        if (chromaticAberration != null)
        {
            chromaticAberration.intensity.value = settings.lensFlareChromaticIntensity;
        }
    }

    public void UpdateLensDistortionSettings(RuntimeSceneSettings settings)
    {
        if (lensDistortion != null)
        {
            lensDistortion.intensity.value = settings.lensDistortionIntensity;
            lensDistortion.xMultiplier.value = settings.lensDistortionXMultiplier;
            lensDistortion.yMultiplier.value = settings.lensDistortionYMultiplier;
            lensDistortion.scale.value = settings.lensDistortionScale;
            lensDistortion.center.value = new Vector2(settings.lensDistortionCenterX, settings.lensDistortionCenterY);
        }
    }

    public void UpdateColorAdjustmentsSettings(RuntimeSceneSettings settings)
    {
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = settings.colorAdjustmentsPostExposure;
            colorAdjustments.contrast.value = settings.colorAdjustmentsContrast;
            colorAdjustments.hueShift.value = settings.colorAdjustmentsHueShift;
            colorAdjustments.saturation.value = settings.colorAdjustmentsSaturation;
        }
    }

    public void UpdateWhiteBalanceSettings(RuntimeSceneSettings settings)
    {
        if (whiteBalance != null)
        {
            whiteBalance.temperature.value = settings.whiteBalanceTemperature;
            whiteBalance.tint.value = settings.whiteBalanceTint;
        }
    }

    public void ApplyCurrentSettings(RuntimeSceneSettings settings)
    {
        UpdateBloomSettings(settings);
        UpdateLensFlareSettings(settings);
        UpdateLensDistortionSettings(settings);
        UpdateColorAdjustmentsSettings(settings);
        UpdateWhiteBalanceSettings(settings);
    }
}