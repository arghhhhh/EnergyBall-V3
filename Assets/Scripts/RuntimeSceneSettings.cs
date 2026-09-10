using System;
using UnityEngine;

/// <summary>
/// The scene settings the game reads. Profiles, the inspector and the in-game menu
/// hold BASE values at bodyScale = 1; every dimensioned field carries a
/// <see cref="BodyScaledAttribute"/> and <see cref="BodyScaling.CreateEffective"/>
/// derives the effective object (base x bodyScale^exp) that consumers read via
/// <c>SceneController.CurrentSettings</c> / <c>GetRuntimeSettings()</c>.
/// </summary>
[System.Serializable]
public class RuntimeSceneSettings
{
    public event Action OnAnyDebuggingSettingChanged;

    /// <summary>
    /// 0 = legacy file: effective values tuned at the file's own bodyScale (auto-converted on
    /// load). 1 = base values at bodyScale = 1. Must default to 0 so a legacy JSON without the
    /// key reads as legacy; the save path stamps 1.
    /// </summary>
    public int settingsVersion = 0;
    public const int CurrentSettingsVersion = 1;

    [Header("Gravity Attraction")]
    [BodyScaled(2)]
    public float g = 0.48f;

    [BodyScaled(2)]
    public float maxTowardsForce = 0.4f;

    [BodyScaled(2)]
    public float maxAwayFromForce = 1f;
    public float gravityForceDamper = 1f;

    [BodyScaled(1)]
    public float stopGravityDistance = 0.024f;

    [BodyScaled(1)]
    public float stopMovingDistance = 0.01f;

    [BodyScaled(1)]
    public float stopVelocity = 0.1f;
    public float attractionRadiusMultiplier = 1f;

    [Header("Hands Attraction")]
    public AnimationCurve forceToMiddle = AnimationCurve.Linear(0, 0, 1, 1);
    public float singleHandOpenForceDamper = 1f;

    [Header("Boundary Drag")]
    [BodyScaled(1)]
    [Tooltip("Distance added to the grid extents to get the boundary (world units at 1x).")]
    public float addedBoundaryDistance = 0.26f;

    [BodyScaled(1)]
    [Tooltip(
        "Drag applied to stop the sphere when moving away from hands while past the boundary. Set to 0 to disable. "
            + "AddForce(-v * k) with mass and v both proportional to s, so k scales with s."
    )]
    public float boundaryOutwardDrag = 4f;

    [Tooltip(
        "Time in seconds the sphere must be out of bounds before it can be reset to hand midpoint when both hands open."
    )]
    public float outOfBoundsResetDelay = 3f;

    [BodyScaled(2)]
    [Tooltip("Rigidbody push force toward the hands (force -> exp 2, mass is proportional to s).")]
    public float pushForce = 2.8f;

    [BodyScaled(1)]
    public float torsoMaxForwardOffset = 0.2f;

    [BodyScaled(1)]
    public float torsoOffsetFalloffDistance = 0.4f;
    public float minDrag = 0.1f;
    public float maxDrag = 5f;

    public AnimationCurve alignmentVectorStrength = AnimationCurve.Linear(0, 0, 1, 1);

    [BodyScaled(1)]
    public float alignmentVectorStrengthScaler = 0.07f;
    public float handPushScaler = 1f;
    public bool prayToActivate = false;

    [BodyScaled(1)]
    public float prayToActivateDistance = 0.14f;

    [Header("Intrinsic Pulsation")]
    [Range(0, 10f)]
    public float pulseAmount = 1f;
    public float pulseSpeed = 1f;
    public float graphLimit = 10f;
    public float[] pulseFreqs = new float[] { 1f, 2f, 3f };

    [Header("Movement-Based Pulsation")]
    public bool singleHandScaling = true;

    [BodyScaled(1)]
    public float minimumUnscaledSize = 0.3f;

    [BodyScaled(1)]
    public float maximumUnscaledSize = 0.6f;

    [BodyScaled(1)]
    [Range(0.0001f, 5f)]
    public float minHandDisplacementPerFrame = 0.01f;

    [BodyScaled(1)]
    [Tooltip(
        "Hand-velocity sanity gate for movement-based scaling: frames where a hand moves faster "
            + "than this (world units/s at 1x) are ignored as tracking glitches."
    )]
    public float maxHandVelocity = 3.0f;

    public AnimationCurve distanceDamper = AnimationCurve.Linear(0, 0, 1, 1);
    public float pulseScaleDamper = 1f;

    [Header("Miscellaneous")]
    public float mergeSizeScalerDamper = 1f;

    [BodyScaled(1)]
    public float maxDistanceBetweenHands = 1.6f;

    [BodyScaled(1)]
    public float baseZDepth = 2f;

    [BodyScaled(1)]
    public float gridScale = 0.06f;

    [BodyScaled(1)]
    public float defaultUnscaledSize = 0.5f;

    [Tooltip(
        "World scale of the Kinect space. Every [BodyScaled] setting is stored at 1x and multiplied "
            + "by bodyScale^exp at runtime, so changing this alone keeps gameplay/look identical relative to the body."
    )]
    public float bodyScale = 1f;

    [BodyScaled(1)]
    public float maxDistanceFromCamera = 2.6f;

    [BodyScaled(1)]
    [Tooltip(
        "Random +/- jitter (world units at 1x) added when the sphere is reset to the hand midpoint."
    )]
    public float sphereResetJitter = 0.1f;

    [Header("Hand VFX")]
    [Tooltip(
        "Per-hand HandEffects.vfx values (base at 1x). Copied as one object at every plumbing site."
    )]
    public HandVfxSettings handVfx = new HandVfxSettings();

    [Header("Animation")]
    public float particleInitializationDelay = 1f;
    public float initializationResetDelay = 3f;

    [Tooltip(
        "Minimum time in single-hand-open state before the final push uses that hand's position. "
            + "Accounts for slight timing discrepancies with real Kinect users."
    )]
    public float singleHandOpenThreshold = 0.1f;

    [Tooltip(
        "Duration in seconds to lerp the force damper from single-hand to both-hands strength "
            + "when transitioning from single-hand-open to both-hands-open."
    )]
    public float singleHandForceLerpDuration = 0.35f;

    [Range(0f, 1f)]
    [Tooltip(
        "Speed of the hand opening animation during initialization. Lower values = slower animation."
    )]
    public float initializationSpeed = 0.05f;

    [Tooltip(
        "Duration in seconds for the metaball radius to animate from minimum to full size during initialization."
    )]
    public float metaballRadiusAnimationDuration = 2f;

    [BodyScaled(1)]
    [Tooltip("The starting radius for the metaball animation during initialization.")]
    public float metaballRadiusAnimationStartSize = 0.02f;

    [BodyScaled(1)]
    [Tooltip("Particle size of the BodyEffects.vfx spawn flash on VFX_Body (world units at 1x).")]
    public float bodySpawnSize = 0.2f;

    [Tooltip(
        "Animation curve for the metaball radius transition (0-1 input maps to animation progress)."
    )]
    public AnimationCurve metaballRadiusAnimationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

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
    private bool _showPointCloud = false;
    public bool showPointCloud
    {
        get => _showPointCloud;
        set
        {
            if (_showPointCloud != value)
            {
                _showPointCloud = value;
                OnAnyDebuggingSettingChanged?.Invoke();
            }
        }
    }

    [SerializeField]
    private bool _showMetaballBounds = false;
    public bool showMetaballBounds
    {
        get => _showMetaballBounds;
        set
        {
            if (_showMetaballBounds != value)
            {
                _showMetaballBounds = value;
                OnAnyDebuggingSettingChanged?.Invoke();
            }
        }
    }

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

    public RuntimeSceneSettings DeepCopy()
    {
        var copy = new RuntimeSceneSettings();
        copy.settingsVersion = settingsVersion;
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
        copy.torsoMaxForwardOffset = torsoMaxForwardOffset;
        copy.torsoOffsetFalloffDistance = torsoOffsetFalloffDistance;
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
        copy.maximumUnscaledSize = maximumUnscaledSize;
        copy.minHandDisplacementPerFrame = minHandDisplacementPerFrame;
        copy.maxHandVelocity = maxHandVelocity;
        copy.distanceDamper = new AnimationCurve(distanceDamper.keys);
        copy.pulseScaleDamper = pulseScaleDamper;
        copy.mergeSizeScalerDamper = mergeSizeScalerDamper;
        copy.maxDistanceBetweenHands = maxDistanceBetweenHands;
        copy.baseZDepth = baseZDepth;
        copy.gridScale = gridScale;
        copy.defaultUnscaledSize = defaultUnscaledSize;
        copy.bodyScale = bodyScale;
        copy.maxDistanceFromCamera = maxDistanceFromCamera;
        copy.sphereResetJitter = sphereResetJitter;
        copy.handVfx = handVfx != null ? handVfx.DeepCopy() : new HandVfxSettings();
        copy.particleInitializationDelay = particleInitializationDelay;
        copy.initializationResetDelay = initializationResetDelay;
        copy.singleHandOpenThreshold = singleHandOpenThreshold;
        copy.singleHandForceLerpDuration = singleHandForceLerpDuration;
        copy.initializationSpeed = initializationSpeed;
        copy.metaballRadiusAnimationDuration = metaballRadiusAnimationDuration;
        copy.metaballRadiusAnimationStartSize = metaballRadiusAnimationStartSize;
        copy.bodySpawnSize = bodySpawnSize;
        copy.metaballRadiusAnimationCurve = new AnimationCurve(metaballRadiusAnimationCurve.keys);
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
        copy._showPointCloud = _showPointCloud;
        copy._showMetaballBounds = _showMetaballBounds;
        copy._showAttractionRadius = _showAttractionRadius;
        copy._showHandTrailDistorters = _showHandTrailDistorters;
        copy._showSecondaryAttractor = _showSecondaryAttractor;
        return copy;
    }
}
