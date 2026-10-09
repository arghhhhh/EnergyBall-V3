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
    // Stored as "_individualColors" in profile JSON. A change is picked up by
    // SceneController.RebuildEffectiveSettings (it recolors live players) - this class must not
    // raise events, since scratch copies of it are built on every settings change.
    [SerializeField]
    private bool _individualColors = false;
    public bool individualColors
    {
        get => _individualColors;
        set => _individualColors = value;
    }
    public bool showCameraFeed = true;
    public bool drawSkeleton = false;
    public bool useTrackingStateColors = true;

    // Colors. Files written before these existed have no "hasStyleColors" key, so JsonUtility
    // leaves it false: loads then keep the current colors instead of the defaults below.
    // Colors don't body-scale.
    [Tooltip("True once the colors below hold real values (false in older profiles).")]
    public bool hasStyleColors = false;

    public Color skeletonColor = Color.magenta;

    [Tooltip(
        "Skeleton color for each palette slot (same index as particleColors). Wraps around when "
            + "shorter than particleColors; empty falls back to the single skeleton color."
    )]
    public Color[] skeletonColors = new Color[]
    {
        Color.blue,
        Color.cyan,
        Color.green,
        Color.magenta,
        Color.red,
        new(1, 0.5f, 0),
        Color.yellow,
    };

    [GradientUsage(true)]
    public Gradient particleColor = new();

    [GradientUsage(true)]
    [Tooltip(
        "The individual-colors palette: one particle gradient per slot. Its length is the number "
            + "of slots; each new player takes a free slot (random among them)."
    )]
    public Gradient[] particleColors = new Gradient[]
    {
        new(),
        new(),
        new(),
        new(),
        new(),
        new(),
        new(),
    };

    // Post-processing: the particle layer (KinectOverlay, Particles Camera) and the camera feed
    // (Main Camera) each get their own look. PP profiles load into either target.
    public PostProcessSettings particlePostProcessing = new();
    public PostProcessSettings feedPostProcessing = new();

    [Header("Debugging")]
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
        copy.particlePostProcessing =
            particlePostProcessing != null
                ? particlePostProcessing.DeepCopy()
                : new PostProcessSettings();
        copy.feedPostProcessing =
            feedPostProcessing != null ? feedPostProcessing.DeepCopy() : new PostProcessSettings();
        copy.showCameraFeed = showCameraFeed;
        copy.drawSkeleton = drawSkeleton;
        copy._individualColors = _individualColors;
        copy.useTrackingStateColors = useTrackingStateColors;
        copy.hasStyleColors = hasStyleColors;
        copy.skeletonColor = skeletonColor;
        copy.skeletonColors = ColorSettingsUtility.Clone(skeletonColors);
        copy.particleColor = ColorSettingsUtility.Clone(particleColor);
        copy.particleColors = ColorSettingsUtility.Clone(particleColors);
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

    /// <summary>
    /// Copies the colors from <paramref name="source"/>. Does nothing when the source has none
    /// (an older file), so the current colors stay.
    /// </summary>
    public void CopyStyleColorsFrom(RuntimeSceneSettings source)
    {
        if (source == null || !source.hasStyleColors)
            return;
        hasStyleColors = true;
        skeletonColor = source.skeletonColor;
        skeletonColors = ColorSettingsUtility.Clone(source.skeletonColors);
        particleColor = ColorSettingsUtility.Clone(source.particleColor);
        particleColors = ColorSettingsUtility.Clone(source.particleColors);
    }

    public bool StyleColorsEqual(RuntimeSceneSettings other) =>
        other != null
        && skeletonColor == other.skeletonColor
        && ColorSettingsUtility.Same(skeletonColors, other.skeletonColors)
        && ColorSettingsUtility.Same(particleColor, other.particleColor)
        && ColorSettingsUtility.Same(particleColors, other.particleColors);
}
