using System;
using UnityEngine;

[System.Serializable]
public class RuntimeSceneSettings
{
    public event Action OnAnyDebuggingSettingChanged;

    [Header("Gravity Attraction")]
    public float g = 9.81f;
    public float maxTowardsForce = 10f;
    public float maxAwayFromForce = 10f;
    public float gravityForceDamper = 1f;
    public float stopGravityDistance = 0.1f;
    public float stopMovingDistance = 0.05f;
    public float stopVelocity = 0.1f;
    public float attractionRadiusMultiplier = 1f;

    [Header("Hands Attraction")]
    [System.NonSerialized] // Excluded from JSON serialization - controlled by CurveSettingsSO
    public AnimationCurve forceToMiddle = AnimationCurve.Linear(0, 0, 1, 1);
    public float singleHandOpenForceDamper = 1f;

    [Header("Boundary Drag")]
    [Tooltip(
        "Multiplier for max distance calculation. Max distance = this * (longest grid side / 2)."
    )]
    public float addedBoundaryDistance = 1.5f;

    [Tooltip(
        "Drag applied to stop the sphere when moving away from hands while past the boundary. Set to 0 to disable."
    )]
    public float boundaryOutwardDrag = 50f;

    [Tooltip(
        "Time in seconds the sphere must be out of bounds before it can be reset to hand midpoint when both hands open."
    )]
    public float outOfBoundsResetDelay = 3f;
    public float pushForce = 5f;
    public float minDrag = 0.1f;
    public float maxDrag = 5f;

    [System.NonSerialized] // Excluded from JSON serialization - controlled by CurveSettingsSO
    public AnimationCurve alignmentVectorStrength = AnimationCurve.Linear(0, 0, 1, 1);
    public float alignmentVectorStrengthScaler = 1f;
    public float handPushScaler = 1f;
    public bool prayToActivate = false;
    public float prayToActivateDistance = 0.65f;

    [Header("Intrinsic Pulsation")]
    [Range(0, 10f)]
    public float pulseAmount = 1f;
    public float pulseSpeed = 1f;
    public float graphLimit = 10f;
    public float[] pulseFreqs = new float[] { 1f, 2f, 3f };

    [Header("Movement-Based Pulsation")]
    public bool singleHandScaling = true;
    public float minimumUnscaledSize = 0.5f;

    [Range(0.0001f, 5f)]
    public float minHandDisplacementPerFrame = 0.01f;

    [System.NonSerialized] // Excluded from JSON serialization - controlled by CurveSettingsSO
    public AnimationCurve distanceDamper = AnimationCurve.Linear(0, 0, 1, 1);
    public float pulseScaleDamper = 1f;

    [Header("Miscellaneous")]
    public float mergeSizeScalerDamper = 1f;
    public float maxDistanceBetweenHands = 2f;
    public float baseZDepth = 5f;
    public float defaultUnscaledSize = 1f;
    public float bodyScale = 1f;
    public float maxDistanceFromCamera = 10f;

    [Header("Animation")]
    public float particleInitializationDelay = 1f;

    [Header("Style")]
    [SerializeField]
    private bool _customColors = false;
    public bool customColors
    {
        get => _customColors;
        set
        {
            if (_customColors != value)
            {
                _customColors = value;
                // Notify listeners with the new value so handlers don't depend on
                // timing of other settings updates.
                Actions.OnCustomColorsChanged?.Invoke(_customColors);
            }
        }
    }
    public bool drawSkeleton = false;
    public bool useTrackingStateColors = true;

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

    [Header("Debugging")]
    public bool dummyOnlyMode = false;
    public bool showSphereMeshOnHandCollision = false;
    public bool alwaysShowSphereMesh = false;
    public bool showMetaballMesh = false;

    [SerializeField]
    private bool _showAttractionRadius = false;
    public bool showAttractionRadius
    {
        get => _showAttractionRadius;
        set
        {
            if (_showAttractionRadius != value)
            {
                _showAttractionRadius = value;
                OnAnyDebuggingSettingChanged?.Invoke();
            }
        }
    }

    [SerializeField]
    private bool _showHandTrailDistorters = false;
    public bool showHandTrailDistorters
    {
        get => _showHandTrailDistorters;
        set
        {
            if (_showHandTrailDistorters != value)
            {
                _showHandTrailDistorters = value;
                OnAnyDebuggingSettingChanged?.Invoke();
            }
        }
    }

    [SerializeField]
    private bool _showSecondaryAttractor = false;
    public bool showSecondaryAttractor
    {
        get => _showSecondaryAttractor;
        set
        {
            if (_showSecondaryAttractor != value)
            {
                _showSecondaryAttractor = value;
                OnAnyDebuggingSettingChanged?.Invoke();
            }
        }
    }

    public void TriggerDebugSettingsUpdate()
    {
        OnAnyDebuggingSettingChanged?.Invoke();
    }

    [System.Obsolete(
        "CopyFromScriptableObject is deprecated. Use SceneController.CopyInspectorToRuntime instead."
    )]
    public void CopyFromScriptableObject(SceneSettingsSO so)
    {
        // This method is kept for backward compatibility but should not be used
        // Settings are now managed directly in SceneController inspector
        Debug.LogWarning(
            "CopyFromScriptableObject is deprecated. Settings are now managed in SceneController inspector."
        );
    }

    public RuntimeSceneSettings DeepCopy()
    {
        var copy = new RuntimeSceneSettings();
        copy.g = g;
        copy.maxTowardsForce = maxTowardsForce;
        copy.maxAwayFromForce = maxAwayFromForce;
        copy.gravityForceDamper = gravityForceDamper;
        copy.stopGravityDistance = stopGravityDistance;
        copy.stopMovingDistance = stopMovingDistance;
        copy.stopVelocity = stopVelocity;
        copy.attractionRadiusMultiplier = attractionRadiusMultiplier;
        copy.forceToMiddle = new AnimationCurve(forceToMiddle.keys);
        copy.singleHandOpenForceDamper = singleHandOpenForceDamper;
        copy.addedBoundaryDistance = addedBoundaryDistance;
        copy.boundaryOutwardDrag = boundaryOutwardDrag;
        copy.outOfBoundsResetDelay = outOfBoundsResetDelay;
        copy.pushForce = pushForce;
        copy.minDrag = minDrag;
        copy.maxDrag = maxDrag;
        copy.alignmentVectorStrength = new AnimationCurve(alignmentVectorStrength.keys);
        copy.alignmentVectorStrengthScaler = alignmentVectorStrengthScaler;
        copy.handPushScaler = handPushScaler;
        copy.prayToActivate = prayToActivate;
        copy.prayToActivateDistance = prayToActivateDistance;
        copy.pulseAmount = pulseAmount;
        copy.pulseSpeed = pulseSpeed;
        copy.graphLimit = graphLimit;
        copy.pulseFreqs = (float[])pulseFreqs.Clone();
        copy.singleHandScaling = singleHandScaling;
        copy.minimumUnscaledSize = minimumUnscaledSize;
        copy.minHandDisplacementPerFrame = minHandDisplacementPerFrame;
        copy.distanceDamper = new AnimationCurve(distanceDamper.keys);
        copy.pulseScaleDamper = pulseScaleDamper;
        copy.mergeSizeScalerDamper = mergeSizeScalerDamper;
        copy.maxDistanceBetweenHands = maxDistanceBetweenHands;
        copy.baseZDepth = baseZDepth;
        copy.defaultUnscaledSize = defaultUnscaledSize;
        copy.bodyScale = bodyScale;
        copy.maxDistanceFromCamera = maxDistanceFromCamera;
        copy.particleInitializationDelay = particleInitializationDelay;
        copy.bloomThreshold = bloomThreshold;
        copy.bloomIntensity = bloomIntensity;
        copy.bloomScatter = bloomScatter;
        copy.lensFlareIntensity = lensFlareIntensity;
        copy.lensFlareRegularMultiplier = lensFlareRegularMultiplier;
        copy.lensFlareReversedMultiplier = lensFlareReversedMultiplier;
        copy.lensFlareStreaksMultiplier = lensFlareStreaksMultiplier;
        copy.lensFlareStreaksLength = lensFlareStreaksLength;
        copy.lensFlareStreaksOrientation = lensFlareStreaksOrientation;
        copy.lensFlareStreaksThreshold = lensFlareStreaksThreshold;
        copy.lensFlareChromaticIntensity = lensFlareChromaticIntensity;
        copy.lensDistortionIntensity = lensDistortionIntensity;
        copy.lensDistortionXMultiplier = lensDistortionXMultiplier;
        copy.lensDistortionYMultiplier = lensDistortionYMultiplier;
        copy.lensDistortionScale = lensDistortionScale;
        copy.lensDistortionCenterX = lensDistortionCenterX;
        copy.lensDistortionCenterY = lensDistortionCenterY;
        copy.colorAdjustmentsPostExposure = colorAdjustmentsPostExposure;
        copy.colorAdjustmentsContrast = colorAdjustmentsContrast;
        copy.colorAdjustmentsHueShift = colorAdjustmentsHueShift;
        copy.colorAdjustmentsSaturation = colorAdjustmentsSaturation;
        copy.whiteBalanceTemperature = whiteBalanceTemperature;
        copy.whiteBalanceTint = whiteBalanceTint;
        copy.dummyOnlyMode = dummyOnlyMode;
        copy.drawSkeleton = drawSkeleton;
        copy._customColors = _customColors;
        copy.useTrackingStateColors = useTrackingStateColors;
        copy.showSphereMeshOnHandCollision = showSphereMeshOnHandCollision;
        copy.alwaysShowSphereMesh = alwaysShowSphereMesh;
        copy.showMetaballMesh = showMetaballMesh;
        copy._showAttractionRadius = _showAttractionRadius;
        copy._showHandTrailDistorters = _showHandTrailDistorters;
        copy._showSecondaryAttractor = _showSecondaryAttractor;
        return copy;
    }

    [System.Obsolete(
        "ApplyCurveSettings is deprecated. Curves are now managed directly in SceneController inspector."
    )]
    public void ApplyCurveSettings(CurveSettingsSO curveSettings)
    {
        // This method is kept for backward compatibility but should not be used
        // Curves are now managed directly in SceneController inspector
        Debug.LogWarning(
            "ApplyCurveSettings is deprecated. Curves are now managed in SceneController inspector."
        );
    }
}
