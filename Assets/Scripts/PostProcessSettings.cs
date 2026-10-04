using System;
using UnityEngine;

/// <summary>
/// One post-processing look (the values VolumeController pushes into a Volume Profile).
/// <see cref="RuntimeSceneSettings"/> holds two: one for the particle layer and one for the
/// camera feed. PP profile files are serialized straight from this class; the field names match
/// the old flat RuntimeSceneSettings fields, so pre-split PP profiles (and working sets) load
/// unchanged through JsonUtility.
/// </summary>
[Serializable]
public class PostProcessSettings
{
    [Header("Bloom")]
    public float bloomThreshold = 1.0f;
    public float bloomIntensity = 0.5f;
    public float bloomScatter = 0.7f;

    [Header("Screen Space Lens Flare")]
    public float lensFlareIntensity = 1.0f;
    public float lensFlareRegularMultiplier = 1.0f;
    public float lensFlareReversedMultiplier = 1.0f;
    public float lensFlareStreaksMultiplier = 1.0f;
    public float lensFlareStreaksLength = 0.04f;
    public float lensFlareStreaksOrientation = 0.0f;
    public float lensFlareStreaksThreshold = 0.05f;
    public float lensFlareChromaticIntensity = 1.0f;

    [Header("Lens Distortion")]
    public float lensDistortionIntensity = 0.0f;
    public float lensDistortionXMultiplier = 1.0f;
    public float lensDistortionYMultiplier = 1.0f;
    public float lensDistortionScale = 1.0f;
    public float lensDistortionCenterX = 0.5f;
    public float lensDistortionCenterY = 0.5f;

    [Header("Color Adjustments")]
    public float colorAdjustmentsPostExposure = 0.0f;
    public float colorAdjustmentsContrast = 0.0f;
    public float colorAdjustmentsHueShift = 0.0f;
    public float colorAdjustmentsSaturation = 0.0f;

    [Header("White Balance")]
    public float whiteBalanceTemperature = 0.0f;
    public float whiteBalanceTint = 0.0f;

    // All fields are plain floats, so a JSON round trip is an exact deep copy. Only runs on
    // settings changes, never per frame.
    public PostProcessSettings DeepCopy() =>
        JsonUtility.FromJson<PostProcessSettings>(JsonUtility.ToJson(this));

    public string ToCanonicalJson() => JsonUtility.ToJson(this);
}
