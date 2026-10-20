using System.Collections.Generic;
using MarchingCubes;
using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Windows.Kinect;
using Joint = Windows.Kinect.Joint;

[DefaultExecutionOrder(-200)]
public class SceneController : MonoBehaviour
{
    public static SceneController Instance { get; private set; } // singleton pattern

    [Header("Runtime Settings")]
    [Tooltip("Skip the Kinect entirely and drive the scene with dummy players only.")]
    public bool dummyOnlyMode = false;
    public InGameSettingsMenu settingsMenu;
    public VolumeController volumeController;

    /// <summary>
    /// Hardware / scene capabilities a setting can depend on. Settings that only make sense
    /// with a live Kinect, or with a camera feed in the scene, are shown in the in-game menu and
    /// the inspector only when the feature is present (see <see cref="HasFeature"/>).
    /// </summary>
    [System.Flags]
    public enum SceneFeature
    {
        None = 0,

        /// <summary>A Kinect drives the players (not dummy-only mode).</summary>
        Kinect = 1 << 0,

        /// <summary>The Kinect color feed is shown in this scene (a feed quad is assigned).</summary>
        CameraFeed = 1 << 1,
    }

    public SceneFeature Features
    {
        get
        {
            var features = SceneFeature.None;
            if (!dummyOnlyMode)
            {
                features |= SceneFeature.Kinect;
                if (cameraFeedQuad != null)
                    features |= SceneFeature.CameraFeed;
            }
            return features;
        }
    }

    public bool HasFeature(SceneFeature feature) => (Features & feature) == feature;

    // NaughtyAttributes visibility conditions (one ShowIf per field, so combined rules get a
    // named property). They mirror what the in-game menu shows.
    private bool HasKinect => HasFeature(SceneFeature.Kinect);
    private bool HasCameraFeed => HasFeature(SceneFeature.CameraFeed);
    private bool ShowSkeletonSettings => HasKinect && drawSkeleton;
    private bool ShowOcclusionSettings => HasKinect && bodyOcclusion;

    // Reacquiring only happens while a lost player is still held.
    private bool ShowReacquireDistanceField => HasKinect && bodyLossGracePeriod > 0f;

    // Syncing the particles to the feed only applies while the feed is shown.
    private bool ShowFeedSyncField => HasCameraFeed && showCameraFeed;

    // The single skeleton color is only used when bones aren't tracking-state colored and players
    // don't get individual palette colors.
    private bool ShowSkeletonColorField =>
        ShowSkeletonSettings && !useTrackingStateColors && !individualColors;

    // Per-slot skeleton colors: only used for bones colored by the player's palette slot.
    private bool ShowSkeletonPaletteField =>
        ShowSkeletonSettings && !useTrackingStateColors && individualColors;

    #region Inspector Settings
    // Grouped by [SettingGroup] into the same sections / groups / labels as the in-game menu
    // (drawn by SceneControllerEditor). The handVfx fields are flattened into Particles.

    // ---- Space ----
    [SettingGroup(SettingSections.Space, "World")]
    [Tooltip(
        "World scale of the Kinect space. Every size-, speed- or force-related setting is stored "
            + "at 1x and derived x bodyScale^exp at runtime (see BodyScaling), so changing it alone "
            + "keeps gameplay and look identical relative to the body."
    )]
    public float bodyScale = 1f;

    [SettingGroup(SettingSections.Space, "World")]
    [Tooltip(
        "World-space depth of the play volume: the metaball grid, boundary and Kinect joints are "
            + "all placed at this Z. Also sent to the hand VFX. Base at 1x."
    )]
    public float baseZDepth = 2f;

    [SettingGroup(SettingSections.Space, "World")]
    [Tooltip(
        "World size of one marching-cubes voxel at 1x; the metaball volume spans 64x32x64 voxels. "
            + "Scales with bodyScale automatically."
    )]
    public float gridScale = 0.06f;

    [SettingGroup(SettingSections.Space, "Play Boundary")]
    [Label("Margin")]
    [Tooltip(
        "Margin added around the metaball grid to define the play boundary (world units at 1x). "
            + "Beyond it Outward Drag engages and the ball becomes eligible for reset."
    )]
    public float addedBoundaryDistance = 0.26f;

    [SettingGroup(SettingSections.Space, "Play Boundary")]
    [Label("Outward Drag")]
    [Tooltip(
        "Drag that opposes the ball while it is past the boundary and moving away from the hands. "
            + "0 disables. Base at 1x (scaled x s)."
    )]
    public float boundaryOutwardDrag = 4f;

    [SettingGroup(SettingSections.Space, "Play Boundary")]
    [Label("Reset Delay")]
    [Tooltip(
        "Seconds the ball must stay out of bounds before opening both hands snaps it back to the "
            + "hand midpoint (plus Reset Jitter)."
    )]
    public float outOfBoundsResetDelay = 3f;

    [SettingGroup(SettingSections.Space, "Play Boundary")]
    [Label("Reset Jitter")]
    [Tooltip(
        "Random +/- offset (world units at 1x) added when the ball is reset to the hand midpoint, "
            + "so overlapping balls don't reset to exactly the same spot."
    )]
    public float sphereResetJitter = 0.1f;

    // ---- Kinect ----
    [SettingGroup(SettingSections.Kinect, "Tracking")]
    [Label("Max Player Distance")]
    [ShowIf("HasKinect")]
    [Tooltip(
        "If a player's hands are tracked farther from the camera than this, they are treated as "
            + "closed and their colliders/skeleton lines are disabled (filters people far in the "
            + "background). Base at 1x."
    )]
    public float maxDistanceFromCamera = 2.6f;

    [SettingGroup(SettingSections.Kinect, "Tracking")]
    [Label("Body Loss Grace")]
    [ShowIf("HasKinect")]
    [Min(0f)]
    [Tooltip(BodyLossGraceTooltip)]
    public float bodyLossGracePeriod = 1f;

    public const string BodyLossGraceTooltip =
        "Seconds a player is kept, frozen at its last pose, after the Kinect loses their body. "
        + "If the body comes back in that time the player carries on with their ball; after it "
        + "the player is removed. 0 removes players as soon as the body is lost.";

    [SettingGroup(SettingSections.Kinect, "Tracking")]
    [Label("Reacquire Distance")]
    [ShowIf("ShowReacquireDistanceField")]
    [Min(0f)]
    [Tooltip(BodyReacquireDistanceTooltip)]
    public float bodyReacquireDistance = 0.5f;

    public const string BodyReacquireDistanceTooltip =
        "The Kinect gives a body it finds again a new id. A new body this close to where a "
        + "player was lost (measured at the base of the neck) takes that player back over "
        + "instead of starting a new one.";

    [SettingGroup(SettingSections.Kinect, "Tracking")]
    [Label("Inferred Joint Smoothing")]
    [ShowIf("HasKinect")]
    [Min(0f)]
    [Tooltip(InferredJointSmoothingTooltip)]
    public float inferredJointSmoothing = 0.1f;

    public const string InferredJointSmoothingTooltip =
        "Seconds of smoothing for joints the Kinect only infers (e.g. a hand hidden behind the "
        + "body), whose guessed positions jump around. Tracked joints are never smoothed. "
        + "Joints the Kinect doesn't track at all keep their last position. 0 uses inferred "
        + "positions as they come.";

    [SettingGroup(SettingSections.Kinect, "Hand State")]
    [Label("Debounce Frames")]
    [ShowIf("HasKinect")]
    [Min(1)]
    [Tooltip(HandStateDebounceTooltip)]
    public int handStateDebounceFrames = 2;

    public const string HandStateDebounceTooltip =
        "Consecutive Kinect frames (30 per second) a hand must read open, or closed, before it "
        + "counts as opened or closed. Filters single misread frames. 1 reacts to every frame.";

    [SettingGroup(SettingSections.Kinect, "Hand State")]
    [Label("Hold Time")]
    [ShowIf("HasKinect")]
    [Min(0f)]
    [Tooltip(HandStateHoldTimeTooltip)]
    public float handStateHoldTime = 1f;

    public const string HandStateHoldTimeTooltip =
        "While the Kinect can't tell whether a hand is open or closed (unknown, not tracked or "
        + "lasso), the hand keeps its last state. After this many seconds without a reading, an "
        + "open hand counts as closed. 0 holds the last state indefinitely.";

    [SettingGroup(SettingSections.Kinect, "Hand State")]
    [Label("High Confidence Only")]
    [ShowIf("HasKinect")]
    [Tooltip(HandStateHighConfidenceOnlyTooltip)]
    public bool handStateHighConfidenceOnly = false;

    public const string HandStateHighConfidenceOnlyTooltip =
        "Ignore open / closed readings the Kinect reports with low confidence (the hand keeps "
        + "its state, as for an unknown reading). Steadier, but slower to react at a distance, "
        + "where low confidence is common.";

    // Gated on Kinect, not CameraFeed: assigning this is what turns the CameraFeed feature on.
    [SettingGroup(SettingSections.Kinect, "Camera Feed")]
    [ShowIf("HasKinect")]
    [Tooltip("The quad displaying the Kinect color feed (child of Main Camera).")]
    public Transform cameraFeedQuad;

    [SettingGroup(SettingSections.Kinect, "Camera Feed")]
    [ShowIf("HasCameraFeed")]
    [Tooltip("Show the Kinect color feed behind the players. Off leaves a black background.")]
    public bool showCameraFeed = true;

    [SettingGroup(SettingSections.Kinect, "Camera Feed")]
    [ShowIf("ShowFeedSyncField")]
    [Tooltip(SyncParticlesToFeedTooltip)]
    public bool syncParticlesToFeed = false;

    public const string SyncParticlesToFeedTooltip =
        "Redraw the particle layer only when a new camera feed frame arrives (30 fps, 15 in low "
        + "light), so particles and video change on the same frames. Off draws particles every "
        + "frame.";

    [SettingGroup(SettingSections.Kinect, "Camera Feed")]
    [ShowIf("HasCameraFeed")]
    [Tooltip(
        "Project joints through the Kinect color-camera intrinsics onto the camera feed quad "
            + "so skeletons/hands align with the video at every depth. Falls back to the legacy "
            + "linear mapping when off or when the mapper is unavailable."
    )]
    public bool projectiveAlignment = true;

    [SettingGroup(SettingSections.Kinect, "Camera Feed")]
    [ShowIf("HasCameraFeed")]
    [Tooltip(
        "Transform of the camera the players are rendered through (Main Camera — the Particles "
            + "Camera must share its position for alignment to hold)."
    )]
    public Transform renderCameraTransform;

    [SettingGroup(SettingSections.Kinect, "Occlusion")]
    [ShowIf("HasKinect")]
    [Tooltip(BodyOcclusionTooltip)]
    public bool bodyOcclusion = true;

    public const string BodyOcclusionTooltip =
        "Hide particles behind people (the Body Occluder draws the Kinect depth image into the "
        + "particle layer's depth). Off draws every particle on top of the camera feed.";

    [SettingGroup(SettingSections.Kinect, "Occlusion")]
    [Label("Depth Bias")]
    [ShowIf("ShowOcclusionSettings")]
    [Tooltip(OccluderDepthBiasTooltip)]
    public float occluderDepthBias = 0f;

    public const string OccluderDepthBiasTooltip =
        "Pushes the occluder away from the camera, so particles at body depth stay in front of "
        + "the body instead of flickering against it.";

    [SettingGroup(SettingSections.Kinect, "Occlusion")]
    [Label("Min Depth")]
    [ShowIf("ShowOcclusionSettings")]
    [Min(0f)]
    [Tooltip(OccluderMinDepthTooltip)]
    public float occluderMinDepth = 0.4f;

    public const string OccluderMinDepthTooltip =
        "Depth samples closer to the sensor than this many meters are ignored by the occluder.";

    [SettingGroup(SettingSections.Kinect, "Occlusion")]
    [Label("Bodies Only")]
    [ShowIf("ShowOcclusionSettings")]
    [Tooltip(OccluderBodiesOnlyTooltip)]
    public bool occluderBodiesOnly = false;

    public const string OccluderBodiesOnlyTooltip =
        "On: only Kinect-tracked human bodies occlude particles. Off: anything the depth camera "
        + "sees (chairs, walls, floor) occludes.";

    [SettingGroup(SettingSections.Kinect, "Skeleton")]
    [ShowIf("HasKinect")]
    [Tooltip("Draw line-renderer bones between tracked Kinect joints for each player.")]
    public bool drawSkeleton = false;

    [SettingGroup(SettingSections.Kinect, "Skeleton")]
    [Label("Line Material")]
    [ShowIf("ShowSkeletonSettings")]
    [Tooltip(
        "Material applied to skeleton LineRenderers at player creation. Uses ZTest Always so "
            + "the debug skeleton is never hidden by the body depth occluder."
    )]
    public Material skeletonLineMaterial;

    [SettingGroup(SettingSections.Kinect, "Skeleton")]
    [Label("Line Width")]
    [ShowIf("ShowSkeletonSettings")]
    [Min(0f)]
    [Tooltip("Thickness of the skeleton bones. Each bone tapers slightly toward its far joint.")]
    public float skeletonLineWidth = 0.02f;

    // Applies with individual colors too: tracking-state colors override the player's color.
    [SettingGroup(SettingSections.Kinect, "Skeleton")]
    [Label("Tracking State Colors")]
    [ShowIf("ShowSkeletonSettings")]
    [Tooltip(TrackingStateColorsTooltip)]
    public bool useTrackingStateColors = true;

    public const string TrackingStateColorsTooltip =
        "Color each end of a bone by its joint's Kinect tracking state instead of the player's "
        + "color: green tracked, yellow inferred, red not tracked (held at its last position). "
        + "A lost body held for Body Loss Grace turns magenta.";

    [SettingGroup(SettingSections.Kinect, "Skeleton")]
    [ShowIf("ShowSkeletonColorField")]
    [Tooltip("Bone color for every player while Color Per Player is off.")]
    public Color skeletonColor = Color.magenta;

    [SettingGroup(SettingSections.Kinect, "Skeleton")]
    [Label("Skeleton Palette")]
    [ShowIf("ShowSkeletonPaletteField")]
    [Tooltip(
        "Skeleton color for each Color Per Player slot (same index as Particle Palette). Wraps "
            + "around when shorter than the Particle Palette; empty falls back to the single "
            + "Skeleton Color."
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

    // ---- Ball ----
    [SettingGroup(SettingSections.Ball, "Size")]
    [Label("Start Size")]
    [Tooltip(
        "Starting diameter of a new player's ball before any pulsation, growing or shrinking is "
            + "applied (base at 1x)."
    )]
    public float defaultUnscaledSize = 0.5f;

    [SettingGroup(SettingSections.Ball, "Size")]
    [Label("Min Size")]
    [Tooltip("The minimum size the ball can shrink to (base at 1x).")]
    public float minimumUnscaledSize = 0.3f;

    [SettingGroup(SettingSections.Ball, "Size")]
    [Label("Max Size")]
    [Tooltip("The maximum size the ball can grow to (base at 1x).")]
    public float maximumUnscaledSize = 0.6f;

    [SettingGroup(SettingSections.Ball, "Size")]
    [Label("Merge Size Damper")]
    [Tooltip(
        "A damper for the scaling that occurs when multiple balls merge together. 0 disables merging."
    )]
    public float mergeSizeScalerDamper = 1f;

    [SettingGroup(SettingSections.Ball, "Breathing")]
    [Label("Amount")]
    [Range(0, 10f)]
    [Tooltip(
        "Amplitude of the idle 'breathing' size wobble, as a fraction of the ball's size "
            + "(value/10). 0 disables it."
    )]
    public float pulseAmount = 1f;

    [SettingGroup(SettingSections.Ball, "Breathing")]
    [Label("Speed")]
    [Tooltip("Time multiplier for the breathing wobble. Higher = faster oscillation.")]
    public float pulseSpeed = 1f;

    [SettingGroup(SettingSections.Ball, "Breathing")]
    [Tooltip(
        "Expected peak of the summed sine waves, used to normalize the wobble into 0..Amount. "
            + "Roughly the number of Frequencies entries; lower values clip, higher values flatten "
            + "the pulse."
    )]
    public float graphLimit = 10f;

    [SettingGroup(SettingSections.Ball, "Breathing")]
    [Label("Frequencies")]
    [Tooltip(
        "Frequencies of the sine waves summed to make the breathing wobble "
            + "(y = sin(f1*t) + sin(f2*t) + ...). Mixed, non-integer values give a less regular pulse."
    )]
    public float[] pulseFreqs = new float[] { 1f, 2f, 3f };

    [SettingGroup(SettingSections.Ball, "Spawn")]
    [Label("Grow-In Duration")]
    [Tooltip(
        "Seconds for the metaball radius to grow from Grow-In Start Radius to full size when a "
            + "player initializes."
    )]
    public float metaballRadiusAnimationDuration = 2f;

    [SettingGroup(SettingSections.Ball, "Spawn")]
    [Label("Grow-In Start Radius")]
    [Tooltip("Metaball radius the grow-in starts from (base at 1x).")]
    public float metaballRadiusAnimationStartSize = 0.02f;

    [SettingGroup(SettingSections.Ball, "Spawn")]
    [Label("Grow-In Curve")]
    [Tooltip(
        "Easing curve for the metaball grow-in (X: 0-1 normalized time, Y: 0-1 progress from "
            + "start radius to full size)."
    )]
    public AnimationCurve metaballRadiusAnimationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [SettingGroup(SettingSections.Ball, "Spawn")]
    [Label("Spawn Flash Size")]
    [Tooltip("Particle size of the BodyEffects.vfx spawn flash on VFX_Body (world units at 1x).")]
    public float bodySpawnSize = 0.2f;

    [SettingGroup(SettingSections.Ball, "Gravity")]
    [Tooltip(
        "Gravitational constant of the pairwise attraction between balls. Base at 1x (scaled x s^2)."
    )]
    public float g = 0.48f;

    [SettingGroup(SettingSections.Ball, "Gravity")]
    [Tooltip(
        "Cap on the pairwise gravity force (G*m1*m2/r^2) while two balls are moving toward each "
            + "other. Keeps close balls from slamming together. Base at 1x (scaled x s^2)."
    )]
    public float maxTowardsForce = 0.4f;

    [SettingGroup(SettingSections.Ball, "Gravity")]
    [Label("Max Away Force")]
    [Tooltip(
        "Cap on the pairwise gravity force while two balls are moving apart. Above Max Towards "
            + "Force, gravity resists separation more than it accelerates approach. Base at 1x "
            + "(scaled x s^2)."
    )]
    public float maxAwayFromForce = 1f;

    [SettingGroup(SettingSections.Ball, "Gravity")]
    [Label("Damper")]
    [Tooltip(
        "Multiplier applied to Max Towards Force when that cap kicks in. Below 1 softens the final "
            + "approach; 1 = no extra damping."
    )]
    public float gravityForceDamper = 1f;

    [SettingGroup(SettingSections.Ball, "Gravity")]
    [Tooltip(
        "Center-to-center distance below which gravity stops being applied. Inside this range the "
            + "balls coast (or get stopped, see Stop Moving Distance)."
    )]
    public float stopGravityDistance = 0.024f;

    [SettingGroup(SettingSections.Ball, "Gravity")]
    [Tooltip(
        "Within this center-to-center distance, if the balls' relative speed is below Stop "
            + "Velocity, a counter-force cancels their motion so they settle side by side."
    )]
    public float stopMovingDistance = 0.01f;

    [SettingGroup(SettingSections.Ball, "Gravity")]
    [Tooltip(
        "Relative speed threshold for the settle-in-place behavior. Balls closer than Stop Moving "
            + "Distance and slower than this are brought to rest."
    )]
    public float stopVelocity = 0.1f;

    [SettingGroup(SettingSections.Ball, "Gravity")]
    [Tooltip(
        "Scales each ball's attraction radius (relative to its current diameter). Gravity only "
            + "starts once another ball's body enters this radius. Also sizes the debug radius sprite."
    )]
    public float attractionRadiusMultiplier = 1f;

    // ---- Hands ----
    [SettingGroup(SettingSections.Hands, "Activation")]
    [Tooltip(
        "When enabled, players must bring their hands together to initialize. When disabled, "
            + "players start initialized."
    )]
    public bool prayToActivate = false;

    [SettingGroup(SettingSections.Hands, "Activation")]
    [Label("Pray Distance")]
    [ShowIf("prayToActivate")]
    [Tooltip("How close the hands must come to activate the player (base at 1x).")]
    public float prayToActivateDistance = 0.14f;

    [SettingGroup(SettingSections.Hands, "Activation")]
    [Label("Hand Open Speed")]
    [Range(0f, 1f)]
    [Tooltip("Playback speed of the hand-open animation. Lower values = slower animation.")]
    public float initializationSpeed = 0.05f;

    [SettingGroup(SettingSections.Hands, "Activation")]
    [Label("Re-arm Delay")]
    [Tooltip(ReArmDelayTooltip)]
    public float initializationResetDelay = 3f;

    public const string ReArmDelayTooltip =
        "Seconds a hand must stay closed before opening it replays the full hand-open animation. "
        + "Shorter closes just cross-fade back open, so flickering Kinect hand states don't "
        + "retrigger it. When both hands were closed this long, opening also replays the ball's "
        + "grow-in.";

    [SettingGroup(SettingSections.Hands, "Push")]
    [Tooltip(
        "Base rigidbody force driving the ball toward the hand target (midpoint of both hands, or "
            + "the single open hand). Everything else in Push multiplies it. Base at 1x (scaled x s^2)."
    )]
    public float pushForce = 2.8f;

    [SettingGroup(SettingSections.Hands, "Push")]
    [Tooltip(
        "Curve of push-force strength vs. how close the ball is to its target. X: 0 = ball is Max "
            + "Hand Spread away, 1 = ball is at the target. Y multiplies Push Force."
    )]
    public AnimationCurve forceToMiddle = AnimationCurve.Linear(0, 0, 1, 1);

    [SettingGroup(SettingSections.Hands, "Push")]
    [Tooltip(
        "Rigidbody linear damping when the ball is far from the hand target (at Max Hand Spread). "
            + "Lower = ball keeps its momentum longer."
    )]
    public float minDrag = 0.1f;

    [SettingGroup(SettingSections.Hands, "Push")]
    [Tooltip(
        "Rigidbody linear damping when the ball is right at the hand target. Higher = ball "
            + "settles quickly instead of overshooting."
    )]
    public float maxDrag = 5f;

    [SettingGroup(SettingSections.Hands, "Push")]
    [Label("Final Push Scaler")]
    [Tooltip(
        "Extra multiplier on the push force applied while both hands are closed (the final flick "
            + "that sends the ball off). Drag is set to 0 during this push."
    )]
    public float handPushScaler = 1f;

    [SettingGroup(SettingSections.Hands, "Push")]
    [Label("Max Hand Spread")]
    [Tooltip(
        "Reference hand separation used to normalize several curves (Force To Middle, Alignment "
            + "Strength, Distance Damper) and the drag remap. Distances beyond it are clamped. "
            + "Base at 1x."
    )]
    public float maxDistanceBetweenHands = 1.6f;

    [SettingGroup(SettingSections.Hands, "Aim")]
    [Label("Alignment Strength")]
    [Tooltip(
        "Curve of how far the target is offset along the direction the hands point (wrist to "
            + "fingertip). X: 0 = hands together, 1 = hands at Max Hand Spread. Y multiplies "
            + "Alignment Scaler."
    )]
    public AnimationCurve alignmentVectorStrength = AnimationCurve.Linear(0, 0, 1, 1);

    [SettingGroup(SettingSections.Hands, "Aim")]
    [Label("Alignment Scaler")]
    [Tooltip(
        "Max distance the hand target is pushed along the hands' pointing direction. Lets players "
            + "aim the ball by tilting their hands rather than only by moving them. Base at 1x."
    )]
    public float alignmentVectorStrengthScaler = 0.07f;

    [SettingGroup(SettingSections.Hands, "Aim")]
    [Label("Torso Forward Offset")]
    [Tooltip(
        "How far the ball's push target is pulled toward the camera when the hands sit at torso "
            + "depth, so the ball isn't occluded by the player's own body. 0 disables."
    )]
    public float torsoMaxForwardOffset = 0.2f;

    [SettingGroup(SettingSections.Hands, "Aim")]
    [Label("Torso Offset Falloff")]
    [Tooltip(
        "How far (in z) the hands must be from the torso plane, forward or backward, for the "
            + "torso forward offset to fade to zero."
    )]
    public float torsoOffsetFalloffDistance = 0.4f;

    [SettingGroup(SettingSections.Hands, "One Hand")]
    [Label("Open Force Damper")]
    [Tooltip(
        "Multiplier on Push Force while only one hand is open (0-1). Lets one-handed steering be "
            + "gentler than two-handed."
    )]
    public float singleHandOpenForceDamper = 1f;

    [SettingGroup(SettingSections.Hands, "One Hand")]
    [Label("Open Threshold")]
    [Tooltip(
        "Minimum time in single-hand-open state before the final push uses that hand's position. "
            + "Accounts for slight timing discrepancies with real Kinect users."
    )]
    public float singleHandOpenThreshold = 0.1f;

    [SettingGroup(SettingSections.Hands, "One Hand")]
    [Label("Force Blend Time")]
    [Tooltip(
        "Seconds to blend the push force from Open Force Damper back to full strength after the "
            + "second hand opens."
    )]
    public float singleHandForceLerpDuration = 0.35f;

    [SettingGroup(SettingSections.Hands, "Grow & Shrink")]
    [Tooltip("Allow scaling to occur with only one hand's velocity.")]
    public bool singleHandScaling = true;

    [SettingGroup(SettingSections.Hands, "Grow & Shrink")]
    [Tooltip(
        "Curve scaling the grow/shrink effect by hand separation. X: 0 = hands at Max Hand Spread, "
            + "1 = hands together. Y multiplies the scale change, so hands close to the ball have "
            + "more effect."
    )]
    public AnimationCurve distanceDamper = AnimationCurve.Linear(0, 0, 1, 1);

    [SettingGroup(SettingSections.Hands, "Grow & Shrink")]
    [Label("Strength")]
    [Tooltip("An overall multiplier on how much hand movement grows or shrinks the ball.")]
    public float pulseScaleDamper = 1f;

    [SettingGroup(SettingSections.Hands, "Grow & Shrink")]
    [Label("Min Hand Displacement")]
    [Range(0.0001f, 5f)]
    [Tooltip(
        "Per-frame hand movement below this is ignored, masking false velocity readings from "
            + "sensor position jitter."
    )]
    public float minHandDisplacementPerFrame = 0.01f;

    [SettingGroup(SettingSections.Hands, "Grow & Shrink")]
    [Tooltip(
        "Hand-velocity sanity gate: frames where a hand moves faster than this (world units/s at "
            + "1x) are ignored as tracking glitches."
    )]
    public float maxHandVelocity = 3.0f;

    // ---- Particles ----
    [SettingGroup(SettingSections.Particles, "Color")]
    [Label("Color Per Player")]
    [Tooltip(
        "Give each new player its own slot in the Particle Palette (and the Skeleton Palette) "
            + "instead of the shared Particle Gradient."
    )]
    public bool individualColors = false;

    [SettingGroup(SettingSections.Particles, "Color")]
    [Label("Particle Gradient")]
    [HideIf("individualColors")]
    [GradientUsage(true)]
    [Tooltip("Hand particle gradient for every player while Color Per Player is off.")]
    public Gradient particleColor = new();
    private int lastColorIndex;

    // The palette: its length is the number of slots, and each player keeps its slot index.
    [SettingGroup(SettingSections.Particles, "Color")]
    [Label("Particle Palette")]
    [ShowIf("individualColors")]
    [GradientUsage(true)]
    [Tooltip(
        "The Color Per Player palette: one particle gradient per slot. Its length is the number "
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

    // Flattened into the Particles groups by SceneControllerEditor (see HandVfxSettings).
    [Tooltip("Per-hand HandEffects.vfx values (base at 1x). See HandVfxSettings.")]
    public HandVfxSettings handVfx = new();

    // ---- Debug ----
    [SettingGroup(SettingSections.Debug, "Visualizers")]
    [Tooltip(
        "Temporarily show the physics sphere mesh whenever a hand's scaling ray hits the ball, to "
            + "visualize the grow/shrink hit test."
    )]
    public bool showSphereMeshOnHandCollision = false;

    [SettingGroup(SettingSections.Debug, "Visualizers")]
    [Tooltip("When enabled, the sphere mesh is always visible regardless of hand collision state.")]
    public bool alwaysShowSphereMesh = false;

    [SettingGroup(SettingSections.Debug, "Visualizers")]
    [Tooltip("When enabled, the metaball mesh renderer is visible for debugging.")]
    public bool showMetaballMesh = false;

    [SettingGroup(SettingSections.Debug, "Visualizers")]
    [Tooltip(
        "When enabled, the Kinect depth point cloud (the body occlusion geometry) is rendered "
            + "visibly, colored by depth — body pixels warm, environment cool."
    )]
    public bool showPointCloud = false;

    [SettingGroup(SettingSections.Debug, "Visualizers")]
    [Tooltip(
        "When enabled, the metaball volume's bounding box is drawn as a wireframe for checking "
            + "grid placement while tuning baseZDepth."
    )]
    public bool showMetaballBounds = false;

    [SettingGroup(SettingSections.Debug, "Visualizers")]
    [Tooltip("Show a sprite around each ball indicating its gravity attraction radius.")]
    public bool showAttractionRadius = false;

    [SettingGroup(SettingSections.Debug, "Visualizers")]
    [Tooltip(
        "Render the TD1/TD2 trail distorter debug spheres that orbit each hand and shape the hand "
            + "particles."
    )]
    public bool showHandTrailDistorters = false;

    [SettingGroup(SettingSections.Debug, "Visualizers")]
    [Tooltip(
        "Render the secondary attractor debug sphere on each hand that the hand particles conform to."
    )]
    public bool showSecondaryAttractor = false;
    #endregion

    // BASE settings (what the menu / inspector / profiles hold, at bodyScale = 1).
    private RuntimeSceneSettings runtimeSettings;

    // EFFECTIVE settings (base x bodyScale^exp) - the only object consumers read.
    // Rebuilt by RebuildEffectiveSettings() on every settings change; never per frame.
    private RuntimeSceneSettings cachedCurrentSettings;
    GravityForce gravityForceController;
    HandForce handForceController;
    HandEffects handEffectsController;
    BoundaryForce boundaryForceController;
    PlayerScaler playerScaleController;
    PlayerScaleApplier playerScaleApplier;

    [SerializeField]
    [Tooltip(
        "The MetaballsToSDF component (lives on the Metaballs GameObject; auto-found if empty)."
    )]
    MetaballsToSDF metaballsToSDF = null;
    BodySourceManager bodySourceManager = null;
    Body[] bodyData;
    List<ulong> trackedIds = new();
    List<ulong> knownIds = new();

    // Kinect players whose body the Kinect lost, by tracking id -> Time.time it was lost. They're
    // held at their last pose for bodyLossGracePeriod (see UpdateLostPlayers).
    private readonly Dictionary<ulong, float> lostPlayers = new();
    private readonly List<ulong> expiredPlayerIds = new();
    private int lastBodyFrameCount = -1;
    public GameObject playerPrefab;
    public TextMeshProUGUI debugText;
    private readonly Dictionary<ulong, GameObject> players = new();
    public Dictionary<ulong, GameObject> Players
    {
        get { return players; }
    }

    private readonly Dictionary<ulong, GameObject> dummies = new();

    private readonly Dictionary<JointType, JointType> boneMap = new()
    {
        // { JointType.FootLeft, JointType.AnkleLeft },
        // { JointType.AnkleLeft, JointType.KneeLeft },
        // { JointType.KneeLeft, JointType.HipLeft },
        // { JointType.HipLeft, JointType.SpineBase },

        // { JointType.FootRight, JointType.AnkleRight },
        // { JointType.AnkleRight, JointType.KneeRight },
        // { JointType.KneeRight, JointType.HipRight },
        // { JointType.HipRight, JointType.SpineBase },

        // { JointType.HandTipLeft, JointType.HandLeft },
        // { JointType.ThumbLeft, JointType.HandLeft },
        { JointType.HandLeft, JointType.WristLeft },
        { JointType.WristLeft, JointType.ElbowLeft },
        { JointType.ElbowLeft, JointType.ShoulderLeft },
        { JointType.ShoulderLeft, JointType.SpineShoulder },
        // { JointType.HandTipRight, JointType.HandRight },
        // { JointType.ThumbRight, JointType.HandRight },
        { JointType.HandRight, JointType.WristRight },
        { JointType.WristRight, JointType.ElbowRight },
        { JointType.ElbowRight, JointType.ShoulderRight },
        { JointType.ShoulderRight, JointType.SpineShoulder },

        // { JointType.SpineBase, JointType.SpineMid },
        // { JointType.SpineMid, JointType.SpineShoulder },
        // { JointType.SpineShoulder, JointType.Neck },
        // { JointType.Neck, JointType.Head },
    };

    private void OnEnable()
    {
#if UNITY_EDITOR
        // Edit mode (scene open / domain reload): show the working set in the inspector.
        if (!Application.isPlaying && !editModeValidateQueued)
        {
            editModeValidateQueued = true;
            UnityEditor.EditorApplication.delayCall += EditModeValidate;
        }
#endif
        // Actions.OnPlayerAdded += AddPlayer;
        // Actions.OnPlayerRemoved += RemovePlayer;
        Actions.OnDummyAdded += InitializeNewDummy;
        Actions.OnDummyRemoved += RemovePlayer;

        // Debugging setting changes are now handled via OnValidate() when inspector values change

        // Subscribe to runtime settings changes
        if (settingsMenu != null)
        {
            settingsMenu.OnSettingsChanged += OnRuntimeSettingsChanged;
        }
    }

    private void OnDisable()
    {
        // Actions.OnPlayerAdded -= AddPlayer;
        // Actions.OnPlayerRemoved -= RemovePlayer;
        Actions.OnDummyAdded -= InitializeNewDummy;
        Actions.OnDummyRemoved -= RemovePlayer;

        // Debugging setting changes are now handled via OnValidate() when inspector values change

        // Unsubscribe from runtime settings changes
        if (settingsMenu != null)
        {
            settingsMenu.OnSettingsChanged -= OnRuntimeSettingsChanged;
        }
    }

    private void Awake()
    {
        // only if not in unity editor

        if (!Application.isEditor)
        {
            Cursor.visible = false;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }

        gravityForceController = new();
        handForceController = new();
        handEffectsController = new();
        boundaryForceController = new();
        playerScaleController = new();
        playerScaleApplier = new();
        if (metaballsToSDF == null)
        {
            // Backward compatible: same GameObject first, then anywhere in the scene
            metaballsToSDF = GetComponent<MetaballsToSDF>();
            if (metaballsToSDF == null)
            {
                metaballsToSDF = FindFirstObjectByType<MetaballsToSDF>();
            }
        }
        bodySourceManager = GetComponent<BodySourceManager>();

        // Initialize runtime settings from inspector values
        if (settingsMenu != null)
        {
            runtimeSettings = settingsMenu.GetCurrentSettings();
        }

        // Ensure we have runtime settings (fallback if needed)
        if (runtimeSettings == null)
        {
            runtimeSettings = CreateFallbackSettings();
        }

        // Apply settings from inspector to runtime settings (without UI sync during initialization)
        if (runtimeSettings != null)
        {
            CopyInspectorToRuntime(runtimeSettings);
            RebuildEffectiveSettings();
        }
        // transform.position = new Vector3(transform.position.x, transform.position.y, cachedCurrentSettings.baseZDepth);

        // set main camera far clipping plane to cachedCurrentSettings.maxDistanceFromCamera
        // Camera.main.farClipPlane = cachedCurrentSettings.maxDistanceFromCamera + transform.position.z;
    }

    void Start()
    {
        // Perform full sync after all components are initialized
        SyncInspectorToRuntime();
    }

    /// <summary>
    /// Get the PlayerPrefs key for this scene's last used scene profile
    /// </summary>
    public string GetSceneSpecificSceneProfileKey()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return $"LastUsedSceneProfile_{sceneName}";
    }

    /// <summary>
    /// Get the PlayerPrefs key for this scene's last used post-processing profile
    /// </summary>
    public string GetSceneSpecificPostProcessingProfileKey()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return $"LastUsedPostProcessingProfile_{sceneName}";
    }

    /// <summary>
    /// Get the PlayerPrefs key for this scene's last used camera-feed post-processing profile
    /// </summary>
    public string GetSceneSpecificFeedPostProcessingProfileKey()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return $"LastUsedFeedPostProcessingProfile_{sceneName}";
    }

    void InitializeNewDummy(PlayerConstructor dummy)
    {
        if (dummy)
        {
            ulong userId = (ulong)Random.Range(90, 100); // generate random userId between 90 and 99
            while (dummies.ContainsKey(userId))
            {
                userId = (ulong)Random.Range(90, 100);
            }
            dummy.name = $"Player {userId}";
            dummy.userId = userId;
            metaballsToSDF.AssignMetaballIndex(dummy);
            dummy.unscaledSize = new Vector3(
                CurrentSettings.defaultUnscaledSize,
                CurrentSettings.defaultUnscaledSize,
                CurrentSettings.defaultUnscaledSize
            );
            ChoosePlayerColor(dummy);

            dummies[dummy.userId] = dummy.gameObject;
            players[dummy.userId] = dummy.gameObject;

            playerScaleApplier.Apply(dummy, CurrentSettings);
            UpdatePlayerDebuggingVisuals(dummy);

            // debugText.text = $"userId: {userId}\nunscaledSize: {dummy.unscaledSize.x}\nplayer: {players[userId].name}";
        }
    }

    GameObject InitializeNewPlayer(ulong userId)
    {
        GameObject newPlayer = Instantiate(playerPrefab);

        if (newPlayer.TryGetComponent<PlayerConstructor>(out var playerConstructor))
        {
            newPlayer.name = $"Player {userId}";
            playerConstructor.userId = userId;
            metaballsToSDF.AssignMetaballIndex(playerConstructor);
            ChoosePlayerColor(playerConstructor);

            playerScaleApplier.Apply(playerConstructor, CurrentSettings);
            UpdatePlayerDebuggingVisuals(playerConstructor);
        }

        return newPlayer;
    }

    void RemovePlayer(ulong userId)
    {
        lostPlayers.Remove(userId);
        if (players.TryGetValue(userId, out GameObject player) && player)
        {
            PlayerConstructor playerConstructor = player.GetComponent<PlayerConstructor>();
            if (playerConstructor.isDummy)
            {
                dummies.Remove(userId);
            }
            metaballsToSDF.RemoveMetaballIndex(playerConstructor.metaballIndex);
            Destroy(players[userId]);
            players.Remove(userId);
        }
    }

    void ChoosePlayerColor(PlayerConstructor player)
    {
        var settings = CurrentSettings;
        int slotCount = settings.particleColors != null ? settings.particleColors.Length : 0;
        if (!settings.individualColors || slotCount == 0)
        {
            player.paletteSlot = -1;
            ApplyPlayerColors(player);
            return;
        }

        // Slots held by the other players
        HashSet<int> usedSlots = new();
        foreach (var existingPlayer in players.Values)
        {
            if (existingPlayer != null && existingPlayer != player.gameObject)
            {
                int slot = existingPlayer.GetComponent<PlayerConstructor>().paletteSlot;
                if (slot >= 0)
                    usedSlots.Add(slot);
            }
        }

        int colorIndex;
        // If there are free slots, pick from those
        if (usedSlots.Count < slotCount)
        {
            do
            {
                colorIndex = Random.Range(0, slotCount);
            } while (usedSlots.Contains(colorIndex));
        }
        else
        {
            // All slots in use, pick randomly (avoid last used for variety)
            colorIndex = Random.Range(0, slotCount);
            while (colorIndex == lastColorIndex && slotCount > 1)
            {
                colorIndex = Random.Range(0, slotCount);
            }
        }

        lastColorIndex = colorIndex;
        player.paletteSlot = colorIndex;
        ApplyPlayerColors(player);
    }

    /// <summary>
    /// Pushes the colors of the player's palette slot (or the shared colors when it has none)
    /// to its skeleton and hand VFX.
    /// </summary>
    void ApplyPlayerColors(PlayerConstructor player)
    {
        var settings = CurrentSettings;
        int slot = player.paletteSlot;
        bool fromPalette =
            settings.individualColors
            && slot >= 0
            && settings.particleColors != null
            && slot < settings.particleColors.Length;

        // Skeleton colors are optional per slot: wrap a shorter array, fall back when empty.
        var skeletonPalette = settings.skeletonColors;
        player.skeletonColor =
            fromPalette && skeletonPalette != null && skeletonPalette.Length > 0
                ? skeletonPalette[slot % skeletonPalette.Length]
                : settings.skeletonColor;

        Gradient particle = fromPalette ? settings.particleColors[slot] : settings.particleColor;
        player.leftHandVfx.SetGradient("playerAuraBase", particle);
        player.rightHandVfx.SetGradient("playerAuraBase", particle);
    }

    private Vector3 GetVector3FromJoint(Joint joint)
    {
        if (projectiveAlignment && TryProjectJointThroughColorCamera(joint, out Vector3 projected))
        {
            return projected;
        }

        return new Vector3(
            joint.Position.X * CurrentSettings.bodyScale,
            joint.Position.Y * CurrentSettings.bodyScale,
            joint.Position.Z * CurrentSettings.bodyScale
        );
    }

    /// <summary>
    /// Maps a Kinect camera-space joint to the world point that sits on the ray
    /// from the render camera through the joint's pixel on the color-feed quad,
    /// at view depth Z * bodyScale. This makes skeletons/hands line up with the
    /// displayed video at every depth, since both go through the color camera's
    /// real projection instead of a linear scale.
    /// </summary>
    private bool TryProjectJointThroughColorCamera(Joint joint, out Vector3 result)
    {
        result = default;

        if (cameraFeedQuad == null || renderCameraTransform == null || bodySourceManager == null)
        {
            return false;
        }

        var mapper = bodySourceManager.Mapper;
        if (mapper == null || joint.Position.Z <= 0.01f)
        {
            return false;
        }

        Windows.Kinect.ColorSpacePoint colorPoint = mapper.MapCameraPointToColorSpace(
            joint.Position
        );
        if (
            float.IsInfinity(colorPoint.X)
            || float.IsNaN(colorPoint.X)
            || float.IsInfinity(colorPoint.Y)
            || float.IsNaN(colorPoint.Y)
        )
        {
            return false;
        }

        // Color pixel -> point on the feed quad. The quad mesh spans ±0.5 in
        // local XY with uv (0,0) at the (-0.5,-0.5) corner, and the color
        // texture's first row (image top) sits at v = 0, matching the
        // top-left-origin pixel coordinates ColorSpacePoint uses.
        Vector3 quadPoint = cameraFeedQuad.TransformPoint(
            new Vector3(
                colorPoint.X / bodySourceManager.ColorWidth - 0.5f,
                colorPoint.Y / bodySourceManager.ColorHeight - 0.5f,
                0f
            )
        );

        Vector3 camLocal = renderCameraTransform.InverseTransformPoint(quadPoint);
        if (camLocal.z <= 0.01f)
        {
            return false;
        }

        camLocal *= joint.Position.Z * CurrentSettings.bodyScale / camLocal.z;
        result = renderCameraTransform.TransformPoint(camLocal);
        return true;
    }

    // Tracking State Colors (see TrackingStateColorsTooltip).
    private static readonly Color TrackedJointColor = new(0.2f, 1f, 0.3f);
    private static readonly Color InferredJointColor = new(1f, 0.85f, 0.1f);
    private static readonly Color NotTrackedJointColor = new(1f, 0.15f, 0.15f);
    private static readonly Color LostBodyColor = new(1f, 0.25f, 1f);

    private static Color TrackingStateColor(TrackingState state)
    {
        return state switch
        {
            TrackingState.Tracked => TrackedJointColor,
            TrackingState.Inferred => InferredJointColor,
            _ => NotTrackedJointColor,
        };
    }

    void UpdateOtherPlayerData(GameObject player)
    {
        PlayerConstructor playerConstructor = player.GetComponent<PlayerConstructor>();

        // Update hand state tracking before processing hand forces
        playerConstructor.UpdateSingleHandOpenTracking();

        if (playerConstructor.beginInitialization)
        {
            metaballsToSDF.SetMetaballPosition(
                playerConstructor.metaballIndex,
                playerConstructor.GetClampedMetaballPosition()
            );
            metaballsToSDF.SetMetaballRadius(
                playerConstructor.metaballIndex,
                playerConstructor.GetMetaballRadius(cachedCurrentSettings)
            );
            // TEMPLATE: Add metaballs for each hand
            //
            // metaballsToSDF.SetMetaballPosition(
            //     playerConstructor.metaballIndex + 1,
            //     playerConstructor.HandRight.transform.position
            // );
            // metaballsToSDF.SetMetaballRadius(
            //     playerConstructor.metaballIndex + 1,
            //     playerConstructor.sphere.transform.localScale.x / 2f
            // );
            playerConstructor.SetAttractionRadius();
            playerConstructor.SetMass();
            playerConstructor.SetVfxSphereVelocity();
            playerConstructor.SetVfxHandVelocity();
            playerConstructor.SetPulseSize();
            playerConstructor.SetScale();
            handForceController.ManageHandForce(playerConstructor);
            boundaryForceController.ManageBoundaryForce(playerConstructor);
            handEffectsController.ManageHandEffects(playerConstructor, cachedCurrentSettings);
            handEffectsController.ManageHandTrailDistorters(playerConstructor);
            playerScaleController.ScaleSetup(playerConstructor);
        }

        // wait until player's hands first open to initialize particles
        if (
            !playerConstructor.beginInitialization
            && playerConstructor.leftHandState == HandState.Open
            && playerConstructor.rightHandState == HandState.Open
        )
        {
            playerConstructor.beginInitialization = true;
            // playerConstructor.InitializeParticles();
        }
        else if (!playerConstructor.beginInitialization)
        {
            playerConstructor.ResetSphereToHandMidpoint();
        }
    }

    // Joints gameplay reads that aren't bone ends: HandForce aims the push along wrist -> hand tip.
    private static readonly JointType[] extraPositionedJoints =
    {
        JointType.HandTipLeft,
        JointType.HandTipRight,
    };

    // Joints placed for Kinect players: both ends of every boneMap bone, plus
    // extraPositionedJoints. The rest keep their prefab positions, so gameplay must not read them.
    private JointType[] positionedJoints;

    private JointType[] PositionedJoints
    {
        get
        {
            if (positionedJoints == null)
            {
                var joints = new HashSet<JointType>(boneMap.Keys);
                joints.UnionWith(boneMap.Values);
                joints.UnionWith(extraPositionedJoints);
                positionedJoints = new JointType[joints.Count];
                joints.CopyTo(positionedJoints);
            }
            return positionedJoints;
        }
    }

    /// <summary>
    /// Places a Kinect player's joints, filters its hand states and draws its skeleton.
    /// <paramref name="newBodyFrame"/> is false on a physics step that sees the same Kinect
    /// frame again (frames arrive at 30 Hz, steps run at 50 Hz).
    /// </summary>
    void UpdateKinectPlayerData(Body body, GameObject player, bool newBodyFrame)
    {
        if (body.IsRestricted)
            return;

        PlayerConstructor playerConstructor = player.GetComponent<PlayerConstructor>();
        var settings = CurrentSettings;
        // Body.Joints marshals all 25 joints into a new dictionary on every access: read it once.
        var joints = body.Joints;

        foreach (JointType jointType in PositionedJoints)
        {
            PlaceJoint(playerConstructor, jointType, joints[jointType], settings);
        }

        if (TryGetBodyAnchor(joints, out Vector3 anchor))
        {
            playerConstructor.bodyAnchor = anchor;
            playerConstructor.hasBodyAnchor = true;
        }

        if (newBodyFrame)
        {
            float now = Time.time;
            playerConstructor.leftHandFilter.Update(
                body.HandLeftState,
                body.HandLeftConfidence,
                settings.handStateDebounceFrames,
                settings.handStateHighConfidenceOnly,
                settings.handStateHoldTime,
                now
            );
            playerConstructor.rightHandFilter.Update(
                body.HandRightState,
                body.HandRightConfidence,
                settings.handStateDebounceFrames,
                settings.handStateHighConfidenceOnly,
                settings.handStateHoldTime,
                now
            );
        }

        // Hands far in the background (someone behind the players): closed and hidden.
        if (
            playerConstructor.HandLeft.transform.position.z > settings.maxDistanceFromCamera
            || playerConstructor.HandRight.transform.position.z > settings.maxDistanceFromCamera
        )
        {
            playerConstructor.leftHandState = HandState.Closed;
            playerConstructor.rightHandState = HandState.Closed;
            SetSkeletonVisible(playerConstructor, false);
            playerConstructor.leftHandCollider.gameObject.SetActive(false);
            playerConstructor.rightHandCollider.gameObject.SetActive(false);
            return;
        }

        playerConstructor.leftHandCollider.gameObject.SetActive(true);
        playerConstructor.rightHandCollider.gameObject.SetActive(true);

        playerConstructor.leftHandState = playerConstructor.leftHandFilter.State;
        playerConstructor.rightHandState = playerConstructor.rightHandFilter.State;

        UpdateSkeleton(playerConstructor, joints, settings);
    }

    /// <summary>
    /// Moves a joint's transform to its Kinect position according to its tracking state:
    /// tracked joints go straight there, inferred joints (guessed, so jumpy) are eased there
    /// over Inferred Joint Smoothing, and not-tracked joints stay where they last were.
    /// </summary>
    void PlaceJoint(
        PlayerConstructor player,
        JointType jointType,
        Joint joint,
        RuntimeSceneSettings settings
    )
    {
        Transform jointTransform = player.jointMap[jointType].transform;
        int index = (int)jointType;
        bool seen = player.jointSeen[index];

        if (joint.TrackingState == TrackingState.NotTracked)
        {
            // Before the joint's first real reading there's no better place for it.
            if (!seen)
                jointTransform.position = GetVector3FromJoint(joint);
            return;
        }

        Vector3 target = GetVector3FromJoint(joint);
        if (
            seen
            && joint.TrackingState == TrackingState.Inferred
            && settings.inferredJointSmoothing > 0f
        )
        {
            float blend = 1f - Mathf.Exp(-Time.fixedDeltaTime / settings.inferredJointSmoothing);
            target = Vector3.Lerp(jointTransform.position, target, blend);
        }
        jointTransform.position = target;
        player.jointSeen[index] = true;
    }

    /// <summary>
    /// The point a lost player is matched to a re-acquired body by: the base of the neck
    /// (SpineShoulder), which stays put while the arms move. False when it isn't tracked.
    /// </summary>
    bool TryGetBodyAnchor(Dictionary<JointType, Joint> joints, out Vector3 anchor)
    {
        Joint neckBase = joints[JointType.SpineShoulder];
        if (neckBase.TrackingState == TrackingState.NotTracked)
        {
            anchor = default;
            return false;
        }
        anchor = GetVector3FromJoint(neckBase);
        return true;
    }

    void UpdateSkeleton(
        PlayerConstructor player,
        Dictionary<JointType, Joint> joints,
        RuntimeSceneSettings settings
    )
    {
        foreach (var bone in boneMap)
        {
            GameObject jointObject = player.jointMap[bone.Key];
            LineRenderer lr = jointObject.GetComponent<LineRenderer>();
            lr.enabled = settings.drawSkeleton;
            if (!settings.drawSkeleton)
                continue;

            lr.SetPosition(0, jointObject.transform.position);
            lr.SetPosition(1, player.jointMap[bone.Value].transform.position);
            if (settings.useTrackingStateColors)
            {
                lr.startColor = TrackingStateColor(joints[bone.Key].TrackingState);
                lr.endColor = TrackingStateColor(joints[bone.Value].TrackingState);
            }
            else
            {
                lr.startColor = player.skeletonColor;
                lr.endColor = player.skeletonColor;
            }
        }
    }

    /// <summary>A lost player's skeleton stays at its last pose; Tracking State Colors marks it.</summary>
    void UpdateLostSkeleton(PlayerConstructor player, RuntimeSceneSettings settings)
    {
        foreach (JointType jointType in boneMap.Keys)
        {
            LineRenderer lr = player.jointMap[jointType].GetComponent<LineRenderer>();
            lr.enabled = settings.drawSkeleton;
            if (settings.drawSkeleton && settings.useTrackingStateColors)
            {
                lr.startColor = LostBodyColor;
                lr.endColor = LostBodyColor;
            }
        }
    }

    void SetSkeletonVisible(PlayerConstructor player, bool visible)
    {
        foreach (JointType jointType in boneMap.Keys)
        {
            player.jointMap[jointType].GetComponent<LineRenderer>().enabled = visible;
        }
    }

    /// <summary>
    /// Holds Kinect players whose body dropped out instead of removing them at once. The Kinect
    /// loses bodies for a moment (occlusion, someone walking past, turning sideways) and gives a
    /// body it finds again a new tracking id. A lost player is removed after
    /// bodyLossGracePeriod; a body that comes back first takes it over (the same id here, a new
    /// id in <see cref="ReacquireLostPlayer"/>).
    /// </summary>
    void UpdateLostPlayers()
    {
        float now = Time.time;
        float grace = CurrentSettings.bodyLossGracePeriod;
        expiredPlayerIds.Clear();

        foreach (ulong trackingId in knownIds)
        {
            if (dummies.ContainsKey(trackingId))
                continue;
            if (!players.TryGetValue(trackingId, out GameObject player) || !player)
                continue;

            PlayerConstructor playerConstructor = player.GetComponent<PlayerConstructor>();
            if (trackedIds.Contains(trackingId))
            {
                // Back under the same id.
                lostPlayers.Remove(trackingId);
                playerConstructor.bodyLost = false;
                continue;
            }

            if (!lostPlayers.TryGetValue(trackingId, out float lostAt))
            {
                lostAt = now;
                lostPlayers[trackingId] = lostAt;
                playerConstructor.bodyLost = true;
            }
            if (now - lostAt >= grace)
            {
                expiredPlayerIds.Add(trackingId);
            }
        }

        foreach (ulong trackingId in expiredPlayerIds)
        {
            RemovePlayer(trackingId);
        }
    }

    /// <summary>
    /// A newly tracked body near where a lost player was last seen is most likely that person,
    /// found again under a new tracking id: give them their player back (ball, activation, hand
    /// states) instead of starting a new one. Returns null when no lost player is close enough.
    /// </summary>
    GameObject ReacquireLostPlayer(Body body)
    {
        if (lostPlayers.Count == 0 || !TryGetBodyAnchor(body.Joints, out Vector3 anchor))
            return null;

        float maxDistance = CurrentSettings.bodyReacquireDistance;
        ulong closestId = 0;
        GameObject closest = null;
        float closestDistance = float.MaxValue;
        foreach (var lost in lostPlayers)
        {
            if (!players.TryGetValue(lost.Key, out GameObject player) || !player)
                continue;
            PlayerConstructor playerConstructor = player.GetComponent<PlayerConstructor>();
            if (!playerConstructor.hasBodyAnchor)
                continue;

            float distance = Vector3.Distance(playerConstructor.bodyAnchor, anchor);
            if (distance <= maxDistance && distance < closestDistance)
            {
                closestId = lost.Key;
                closest = player;
                closestDistance = distance;
            }
        }
        if (closest == null)
            return null;

        players.Remove(closestId);
        lostPlayers.Remove(closestId);
        PlayerConstructor reacquired = closest.GetComponent<PlayerConstructor>();
        reacquired.userId = body.TrackingId;
        reacquired.bodyLost = false;
        closest.name = $"Player {body.TrackingId}";
        return closest;
    }

    private void DeleteAllBodies(List<ulong> ids)
    {
        foreach (ulong trackingId in ids)
        {
            if (!dummies.ContainsKey(trackingId))
            {
                RemovePlayer(trackingId);
            }
        }
    }

    void FixedUpdate()
    {
        if (!dummyOnlyMode)
        {
            bodyData = bodySourceManager.GetData();
            if (bodyData == null)
            {
                return;
            }

            int bodyFrameCount = bodySourceManager.BodyFrameCount;
            bool newBodyFrame = bodyFrameCount != lastBodyFrameCount;
            lastBodyFrameCount = bodyFrameCount;

            trackedIds.Clear();
            foreach (var body in bodyData)
            {
                if (body == null)
                    continue;

                if (body.IsTracked)
                    trackedIds.Add(body.TrackingId);
            }

            knownIds.Clear();
            knownIds.AddRange(players.Keys);
            UpdateLostPlayers();

            if (Input.GetMouseButtonDown(0) && (settingsMenu == null || !settingsMenu.IsMenuOpen))
            {
                DeleteAllBodies(knownIds);
            }
            else if (Input.GetMouseButtonDown(1))
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }

            foreach (var body in bodyData)
            {
                if (body == null || !body.IsTracked)
                    continue;

                ulong trackingId = body.TrackingId;
                if (!players.TryGetValue(trackingId, out GameObject player))
                {
                    player = ReacquireLostPlayer(body);
                    if (player == null)
                    {
                        player = InitializeNewPlayer(trackingId);
                    }
                    players[trackingId] = player;
                }

                UpdateKinectPlayerData(body, player, newBodyFrame);
                UpdateOtherPlayerData(player);
            }

            // Lost players keep their last pose and hand states; their ball keeps simulating.
            foreach (ulong trackingId in lostPlayers.Keys)
            {
                if (players.TryGetValue(trackingId, out GameObject lostPlayer) && lostPlayer)
                {
                    UpdateLostSkeleton(
                        lostPlayer.GetComponent<PlayerConstructor>(),
                        CurrentSettings
                    );
                    UpdateOtherPlayerData(lostPlayer);
                }
            }
        }

        if (dummies.Count > 0)
        {
            foreach (var dummy in dummies)
            {
                UpdateOtherPlayerData(dummy.Value);
            }
        }

        gravityForceController.ManageGravity();
    }

    #region Settings Management
    /// <summary>
    /// The EFFECTIVE settings (base x bodyScale^exp) that all consumers read. Data flows one
    /// way: menu/inspector -> base (runtimeSettings) -> effective (this) -> consumers. Never
    /// write into this object; mutate the base and let OnSettingsChanged trigger a rebuild.
    /// </summary>
    public RuntimeSceneSettings CurrentSettings
    {
        get
        {
            if (cachedCurrentSettings == null)
            {
                RebuildEffectiveSettings();
            }
            return cachedCurrentSettings;
        }
    }

    /// <summary>
    /// Derives the effective settings from the base object and pushes the per-player scale
    /// step to every live player. Allocates (DeepCopy) - called only on settings change.
    /// </summary>
    private void RebuildEffectiveSettings()
    {
        runtimeSettings ??= CreateFallbackSettings();
        float previousBodyScale =
            cachedCurrentSettings != null ? cachedCurrentSettings.bodyScale : 0f;
        var previousSettings = cachedCurrentSettings;
        cachedCurrentSettings = BodyScaling.CreateEffective(runtimeSettings);
        ApplyCameraFeedVisibility();

        // Only a real change of the live setting re-picks player colors (palette vs default).
        // An edited color only re-applies, so players keep their palette slots.
        if (previousSettings != null)
        {
            if (previousSettings.individualColors != cachedCurrentSettings.individualColors)
                RecolorAllPlayers();
            else if (!previousSettings.StyleColorsEqual(cachedCurrentSettings))
                RefreshPlayerColors();
        }

        if (playerScaleApplier == null)
            return;

        // bodyScale changed while players are alive: rescale their length-valued state
        // (ball size/position) so the invariant holds for the running session too.
        float newBodyScale = cachedCurrentSettings.bodyScale;
        if (
            previousBodyScale > 0f
            && newBodyScale > 0f
            && !Mathf.Approximately(previousBodyScale, newBodyScale)
        )
        {
            playerScaleApplier.RescaleLiveStateAll(
                players.Values,
                newBodyScale / previousBodyScale
            );
        }
        playerScaleApplier.ApplyToAll(players.Values, cachedCurrentSettings);
    }

    /// <summary>
    /// Shows or hides the camera feed quad. Play mode only: renderer.enabled is serialized, so
    /// toggling it in edit mode would dirty the scene; play-mode changes revert on exit.
    /// </summary>
    private void ApplyCameraFeedVisibility()
    {
        if (!Application.isPlaying || cameraFeedQuad == null)
            return;
        if (cameraFeedQuad.TryGetComponent(out Renderer feedRenderer))
            feedRenderer.enabled = cachedCurrentSettings.showCameraFeed;
    }

    private RuntimeSceneSettings CreateFallbackSettings()
    {
        var fallback = new RuntimeSceneSettings();
        CopyInspectorToRuntime(fallback);
        return fallback;
    }

    /// <summary>
    /// Copy inspector values to runtime settings
    /// </summary>
    public void CopyInspectorToRuntime(RuntimeSceneSettings target)
    {
        // Inspector values are base values at bodyScale = 1
        target.settingsVersion = RuntimeSceneSettings.CurrentSettingsVersion;

        // Gravity
        target.g = g;
        target.maxTowardsForce = maxTowardsForce;
        target.maxAwayFromForce = maxAwayFromForce;
        target.gravityForceDamper = gravityForceDamper;
        target.stopGravityDistance = stopGravityDistance;
        target.stopMovingDistance = stopMovingDistance;
        target.stopVelocity = stopVelocity;
        target.attractionRadiusMultiplier = attractionRadiusMultiplier;

        // Hands
        target.forceToMiddle = new AnimationCurve(forceToMiddle.keys);
        target.singleHandOpenForceDamper = singleHandOpenForceDamper;

        // Play Boundary
        target.addedBoundaryDistance = addedBoundaryDistance;
        target.boundaryOutwardDrag = boundaryOutwardDrag;
        target.outOfBoundsResetDelay = outOfBoundsResetDelay;

        target.pushForce = pushForce;
        target.torsoMaxForwardOffset = torsoMaxForwardOffset;
        target.torsoOffsetFalloffDistance = torsoOffsetFalloffDistance;
        target.minDrag = minDrag;
        target.maxDrag = maxDrag;
        target.alignmentVectorStrength = new AnimationCurve(alignmentVectorStrength.keys);
        target.alignmentVectorStrengthScaler = alignmentVectorStrengthScaler;
        target.handPushScaler = handPushScaler;
        target.prayToActivate = prayToActivate;
        target.prayToActivateDistance = prayToActivateDistance;

        // Breathing
        target.pulseAmount = pulseAmount;
        target.pulseSpeed = pulseSpeed;
        target.graphLimit = graphLimit;
        target.pulseFreqs = (float[])pulseFreqs.Clone();

        // Size / Grow & Shrink
        target.singleHandScaling = singleHandScaling;
        target.minimumUnscaledSize = minimumUnscaledSize;
        target.maximumUnscaledSize = maximumUnscaledSize;
        target.minHandDisplacementPerFrame = minHandDisplacementPerFrame;
        target.maxHandVelocity = maxHandVelocity;
        target.distanceDamper = new AnimationCurve(distanceDamper.keys);
        target.pulseScaleDamper = pulseScaleDamper;

        // World, size, push spread, tracking, reset
        target.mergeSizeScalerDamper = mergeSizeScalerDamper;
        target.maxDistanceBetweenHands = maxDistanceBetweenHands;
        target.baseZDepth = baseZDepth;
        target.gridScale = gridScale;
        target.defaultUnscaledSize = defaultUnscaledSize;
        target.bodyScale = bodyScale;
        target.maxDistanceFromCamera = maxDistanceFromCamera;
        target.bodyLossGracePeriod = bodyLossGracePeriod;
        target.bodyReacquireDistance = bodyReacquireDistance;
        target.inferredJointSmoothing = inferredJointSmoothing;
        target.handStateDebounceFrames = handStateDebounceFrames;
        target.handStateHoldTime = handStateHoldTime;
        target.handStateHighConfidenceOnly = handStateHighConfidenceOnly;
        target.bodyOcclusion = bodyOcclusion;
        target.occluderDepthBias = occluderDepthBias;
        target.occluderMinDepth = occluderMinDepth;
        target.occluderBodiesOnly = occluderBodiesOnly;
        target.sphereResetJitter = sphereResetJitter;

        // Hand VFX (nested, copied as one object)
        target.handVfx = handVfx != null ? handVfx.DeepCopy() : new HandVfxSettings();

        // Activation, One Hand, Spawn
        target.initializationResetDelay = initializationResetDelay;
        target.singleHandOpenThreshold = singleHandOpenThreshold;
        target.singleHandForceLerpDuration = singleHandForceLerpDuration;
        target.initializationSpeed = initializationSpeed;
        target.metaballRadiusAnimationDuration = metaballRadiusAnimationDuration;
        target.metaballRadiusAnimationStartSize = metaballRadiusAnimationStartSize;
        target.bodySpawnSize = bodySpawnSize;
        target.metaballRadiusAnimationCurve = new AnimationCurve(metaballRadiusAnimationCurve.keys);

        // Kinect, colors, debug visualizers
        target.showCameraFeed = showCameraFeed;
        target.syncParticlesToFeed = syncParticlesToFeed;
        target.drawSkeleton = drawSkeleton;
        target.skeletonLineWidth = skeletonLineWidth;
        target.useTrackingStateColors = useTrackingStateColors;
        target.individualColors = individualColors;
        target.hasStyleColors = true;
        target.skeletonColor = skeletonColor;
        target.skeletonColors = ColorSettingsUtility.Clone(skeletonColors);
        target.particleColor = ColorSettingsUtility.Clone(particleColor);
        target.particleColors = ColorSettingsUtility.Clone(particleColors);
        target.showSphereMeshOnHandCollision = showSphereMeshOnHandCollision;
        target.alwaysShowSphereMesh = alwaysShowSphereMesh;
        target.showMetaballMesh = showMetaballMesh;
        target.showPointCloud = showPointCloud;
        target.showMetaballBounds = showMetaballBounds;
        target.showAttractionRadius = showAttractionRadius;
        target.showHandTrailDistorters = showHandTrailDistorters;
        target.showSecondaryAttractor = showSecondaryAttractor;
    }

    /// <summary>
    /// Copy runtime settings back to inspector values
    /// </summary>
    public void CopyRuntimeToInspector(RuntimeSceneSettings source)
    {
        // Gravity
        g = source.g;
        maxTowardsForce = source.maxTowardsForce;
        maxAwayFromForce = source.maxAwayFromForce;
        gravityForceDamper = source.gravityForceDamper;
        stopGravityDistance = source.stopGravityDistance;
        stopMovingDistance = source.stopMovingDistance;
        stopVelocity = source.stopVelocity;
        attractionRadiusMultiplier = source.attractionRadiusMultiplier;

        // Hands
        forceToMiddle = new AnimationCurve(source.forceToMiddle.keys);
        singleHandOpenForceDamper = source.singleHandOpenForceDamper;

        // Play Boundary
        addedBoundaryDistance = source.addedBoundaryDistance;
        boundaryOutwardDrag = source.boundaryOutwardDrag;
        outOfBoundsResetDelay = source.outOfBoundsResetDelay;

        pushForce = source.pushForce;
        torsoMaxForwardOffset = source.torsoMaxForwardOffset;
        torsoOffsetFalloffDistance = source.torsoOffsetFalloffDistance;
        minDrag = source.minDrag;
        maxDrag = source.maxDrag;
        alignmentVectorStrength = new AnimationCurve(source.alignmentVectorStrength.keys);
        alignmentVectorStrengthScaler = source.alignmentVectorStrengthScaler;
        handPushScaler = source.handPushScaler;
        prayToActivate = source.prayToActivate;
        prayToActivateDistance = source.prayToActivateDistance;

        // Breathing
        pulseAmount = source.pulseAmount;
        pulseSpeed = source.pulseSpeed;
        graphLimit = source.graphLimit;
        pulseFreqs = (float[])source.pulseFreqs.Clone();

        // Size / Grow & Shrink
        singleHandScaling = source.singleHandScaling;
        minimumUnscaledSize = source.minimumUnscaledSize;
        maximumUnscaledSize = source.maximumUnscaledSize;
        minHandDisplacementPerFrame = source.minHandDisplacementPerFrame;
        maxHandVelocity = source.maxHandVelocity;
        distanceDamper = new AnimationCurve(source.distanceDamper.keys);
        pulseScaleDamper = source.pulseScaleDamper;

        // World, size, push spread, tracking, reset
        mergeSizeScalerDamper = source.mergeSizeScalerDamper;
        maxDistanceBetweenHands = source.maxDistanceBetweenHands;
        baseZDepth = source.baseZDepth;
        gridScale = source.gridScale;
        defaultUnscaledSize = source.defaultUnscaledSize;
        bodyScale = source.bodyScale;
        maxDistanceFromCamera = source.maxDistanceFromCamera;
        bodyLossGracePeriod = source.bodyLossGracePeriod;
        bodyReacquireDistance = source.bodyReacquireDistance;
        inferredJointSmoothing = source.inferredJointSmoothing;
        handStateDebounceFrames = source.handStateDebounceFrames;
        handStateHoldTime = source.handStateHoldTime;
        handStateHighConfidenceOnly = source.handStateHighConfidenceOnly;
        bodyOcclusion = source.bodyOcclusion;
        occluderDepthBias = source.occluderDepthBias;
        occluderMinDepth = source.occluderMinDepth;
        occluderBodiesOnly = source.occluderBodiesOnly;
        sphereResetJitter = source.sphereResetJitter;

        // Hand VFX (nested, copied as one object)
        handVfx = source.handVfx != null ? source.handVfx.DeepCopy() : new HandVfxSettings();

        // Activation, One Hand, Spawn
        initializationResetDelay = source.initializationResetDelay;
        singleHandOpenThreshold = source.singleHandOpenThreshold;
        singleHandForceLerpDuration = source.singleHandForceLerpDuration;
        initializationSpeed = source.initializationSpeed;
        metaballRadiusAnimationDuration = source.metaballRadiusAnimationDuration;
        metaballRadiusAnimationStartSize = source.metaballRadiusAnimationStartSize;
        bodySpawnSize = source.bodySpawnSize;
        metaballRadiusAnimationCurve = new AnimationCurve(source.metaballRadiusAnimationCurve.keys);

        // Kinect, colors, debug visualizers
        showCameraFeed = source.showCameraFeed;
        syncParticlesToFeed = source.syncParticlesToFeed;
        drawSkeleton = source.drawSkeleton;
        skeletonLineWidth = source.skeletonLineWidth;
        useTrackingStateColors = source.useTrackingStateColors;
        individualColors = source.individualColors;
        // Older files carry no colors - keep the inspector's.
        if (source.hasStyleColors)
        {
            skeletonColor = source.skeletonColor;
            skeletonColors = ColorSettingsUtility.Clone(source.skeletonColors);
            particleColor = ColorSettingsUtility.Clone(source.particleColor);
            particleColors = ColorSettingsUtility.Clone(source.particleColors);
        }
        showSphereMeshOnHandCollision = source.showSphereMeshOnHandCollision;
        alwaysShowSphereMesh = source.alwaysShowSphereMesh;
        showMetaballMesh = source.showMetaballMesh;
        showPointCloud = source.showPointCloud;
        showMetaballBounds = source.showMetaballBounds;
        showAttractionRadius = source.showAttractionRadius;
        showHandTrailDistorters = source.showHandTrailDistorters;
        showSecondaryAttractor = source.showSecondaryAttractor;
    }

    /// <summary>
    /// Sync inspector values to runtime settings
    /// </summary>
    private void SyncInspectorToRuntime()
    {
        if (runtimeSettings != null)
        {
            CopyInspectorToRuntime(runtimeSettings);
            RebuildEffectiveSettings();
            UpdateAllPlayersDebuggingVisuals();

            // Update the settings menu UI to reflect inspector changes
            if (settingsMenu != null)
            {
                settingsMenu.UpdateSettingsFromInspector(runtimeSettings);
            }

            // Update volume profile settings
            if (volumeController != null)
            {
                volumeController.ApplyCurrentSettings(runtimeSettings);
            }
        }
    }

    private void OnRuntimeSettingsChanged(RuntimeSceneSettings newSettings)
    {
        runtimeSettings = newSettings;

        // Derive the effective settings and push the per-player scale step
        RebuildEffectiveSettings();

        // Copy runtime settings back to inspector for synchronization
        CopyRuntimeToInspector(newSettings);

        // Update any cached references or trigger updates as needed
        UpdateAllPlayersDebuggingVisuals();

        // Update volume profile settings
        if (volumeController != null)
        {
            volumeController.ApplyCurrentSettings(newSettings);
        }
    }

    private void RecolorAllPlayers()
    {
        foreach (var player in players.Values)
        {
            if (player == null)
                continue;
            var pc = player.GetComponent<PlayerConstructor>();
            ChoosePlayerColor(pc);
        }
    }

    /// <summary>
    /// Re-applies the current colors to every player, keeping palette slots. A player whose
    /// slot no longer exists (palette shrank) picks a new one.
    /// </summary>
    private void RefreshPlayerColors()
    {
        var settings = CurrentSettings;
        int slotCount = settings.particleColors != null ? settings.particleColors.Length : 0;
        foreach (var player in players.Values)
        {
            if (player == null)
                continue;
            var pc = player.GetComponent<PlayerConstructor>();
            bool needsSlot = settings.individualColors && slotCount > 0;
            if (needsSlot && (pc.paletteSlot < 0 || pc.paletteSlot >= slotCount))
                ChoosePlayerColor(pc);
            else
                ApplyPlayerColors(pc);
        }
    }

#if UNITY_EDITOR
    // ---- Working-set sync for the inspector twins (editor only) ----
    //
    // Play mode: inspector edits flow to the menu, which owns the working set.
    // Edit mode: a genuine inspector edit is written straight into the working set, and the
    // first time this object is seen after a domain reload / scene open (or after play mode
    // exits, when Unity has reverted the twins) the working set is copied back INTO the twins,
    // so the inspector always shows the latest values wherever they were changed.
    //
    // OnValidate also fires on deserialization, so a per-scene snapshot of the twins tells a
    // real edit apart from Unity re-loading the serialized (possibly stale) values.

    private static readonly Dictionary<string, string> editModeTwinSnapshots = new();
    private static bool restorePendingAfterPlay;
    private bool editModeValidateQueued;

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterPlayModeHook()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
    }

    private static void OnEditorPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            // Ignore the OnValidate storm while Unity reverts the scene.
            restorePendingAfterPlay = true;
        }
        else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
        {
            // Two delay calls: let Unity finish restoring scene objects first.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    var controller = FindFirstObjectByType<SceneController>();
                    if (controller != null)
                        controller.RestoreInspectorFromWorkingSet(markSceneDirty: true);
                    restorePendingAfterPlay = false;
                };
            };
        }
    }

    /// <summary>
    /// Called when inspector values change (and on deserialization).
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            if (runtimeSettings != null)
            {
                // Sync inspector changes to runtime settings (the menu persists the working set)
                SyncInspectorToRuntime();
            }
            return;
        }

        if (
            restorePendingAfterPlay
            || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
            || UnityEditor.EditorApplication.isCompiling
            || UnityEditor.EditorApplication.isUpdating
            || editModeValidateQueued
        )
            return;

        // OnValidate must not touch other objects/assets - defer.
        editModeValidateQueued = true;
        UnityEditor.EditorApplication.delayCall += EditModeValidate;
    }

    private void EditModeValidate()
    {
        editModeValidateQueued = false;
        if (this == null || Application.isPlaying || restorePendingAfterPlay)
            return;

        string sceneName = gameObject.scene.name;
        string twinsJson = CanonicalTwinsJson();

        if (!editModeTwinSnapshots.TryGetValue(sceneName, out string known))
        {
            // First sight since domain reload / scene open: the working set is newer than the
            // serialized scene. Show it in the inspector (without dirtying the scene yet).
            RestoreInspectorFromWorkingSet(markSceneDirty: false);
            return;
        }

        if (twinsJson == known)
            return; // deserialization noise, not an edit

        // Genuine inspector edit in edit mode -> it is the newest state.
        WriteInspectorToWorkingSet();
        editModeTwinSnapshots[sceneName] = twinsJson;
    }

    private string CanonicalTwinsJson()
    {
        var twins = new RuntimeSceneSettings();
        CopyInspectorToRuntime(twins);
        return JsonUtility.ToJson(twins);
    }

    /// <summary>
    /// Overlays the inspector (scene) values onto the working set, preserving its post-processing
    /// slice and profile names. Seeds a new working set when none exists.
    /// </summary>
    private void WriteInspectorToWorkingSet()
    {
        string sceneName = gameObject.scene.name;
        var file = SettingsWorkingSet.Load(sceneName);
        RuntimeSceneSettings settings = file?.settings ?? new RuntimeSceneSettings();
        CopyInspectorToRuntime(settings);

        string sceneProfile =
            file?.sceneProfileName
            ?? PlayerPrefs.GetString($"LastUsedSceneProfile_{sceneName}", "");
        string ppProfile =
            file?.postProcessingProfileName
            ?? PlayerPrefs.GetString($"LastUsedPostProcessingProfile_{sceneName}", "");
        string feedPpProfile =
            file?.feedPostProcessingProfileName
            ?? PlayerPrefs.GetString($"LastUsedFeedPostProcessingProfile_{sceneName}", "");
        SettingsWorkingSet.Save(sceneName, settings, sceneProfile, ppProfile, feedPpProfile);
    }

    /// <summary>
    /// Copies the working set into the inspector twins (and the Volume Profile asset). When no
    /// working set exists the twins seed one instead.
    /// </summary>
    private void RestoreInspectorFromWorkingSet(bool markSceneDirty)
    {
        string sceneName = gameObject.scene.name;
        var file = SettingsWorkingSet.Load(sceneName);
        if (file == null)
        {
            WriteInspectorToWorkingSet();
            editModeTwinSnapshots[sceneName] = CanonicalTwinsJson();
            return;
        }

        // Only dirty the scene when the restore actually changes a twin - a play session
        // that ends with the values it started with must leave the scene clean.
        string before = CanonicalTwinsJson();
        if (markSceneDirty)
            UnityEditor.Undo.RecordObject(this, "Restore settings from working set");

        CopyRuntimeToInspector(file.settings);
        string after = CanonicalTwinsJson();
        editModeTwinSnapshots[sceneName] = after;
        bool twinsChanged = after != before;

        if (volumeController != null)
        {
            if (markSceneDirty)
            {
                var profiles = volumeController.GetProfiles();
                if (profiles.Length > 0)
                    UnityEditor.Undo.RecordObjects(
                        profiles,
                        "Restore post-processing from working set"
                    );
            }
            volumeController.ApplyCurrentSettings(file.settings);
        }

        if (markSceneDirty && twinsChanged)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    }
#endif

    public RuntimeSceneSettings GetRuntimeSettings()
    {
        return CurrentSettings;
    }

    /// <summary>
    /// Returns the size of the marching cubes grid in world units.
    /// </summary>
    public Vector3 GetGridSize()
    {
        return metaballsToSDF.GetGridSize();
    }
    #endregion

    #region Debugging
    private void OnAnyDebuggingSettingChanged()
    {
        UpdateAllPlayersDebuggingVisuals();
    }

    private void UpdatePlayerHandTrailDistorters(PlayerConstructor player)
    {
        if (CurrentSettings.showHandTrailDistorters)
        {
            foreach (GameObject distorter in player.leftHandTrailDistorters)
            {
                distorter.GetComponent<MeshRenderer>().enabled = true;
            }
            foreach (GameObject distorter in player.rightHandTrailDistorters)
            {
                distorter.GetComponent<MeshRenderer>().enabled = true;
            }
        }
        else
        {
            foreach (GameObject distorter in player.leftHandTrailDistorters)
            {
                distorter.GetComponent<MeshRenderer>().enabled = false;
            }
            foreach (GameObject distorter in player.rightHandTrailDistorters)
            {
                distorter.GetComponent<MeshRenderer>().enabled = false;
            }
        }
    }

    private void UpdatePlayerAttractionRadius(PlayerConstructor player)
    {
        if (CurrentSettings.showAttractionRadius)
        {
            player.radiusSprite.enabled = true;
            // set size of radius sprite
            player.radiusSprite.transform.localScale = new Vector3(
                CurrentSettings.attractionRadiusMultiplier * 0.4f * player.attractionRadiusScaler,
                CurrentSettings.attractionRadiusMultiplier * 0.4f * player.attractionRadiusScaler,
                CurrentSettings.attractionRadiusMultiplier * 0.4f * player.attractionRadiusScaler
            );
        }
        else
        {
            player.radiusSprite.enabled = false;
        }
    }

    private void UpdatePlayerSecondaryAttractor(PlayerConstructor player)
    {
        if (CurrentSettings.showSecondaryAttractor)
        {
            player.leftHandSecondaryAttractor.GetComponent<MeshRenderer>().enabled = true;
            player.rightHandSecondaryAttractor.GetComponent<MeshRenderer>().enabled = true;
        }
        else
        {
            player.leftHandSecondaryAttractor.GetComponent<MeshRenderer>().enabled = false;
            player.rightHandSecondaryAttractor.GetComponent<MeshRenderer>().enabled = false;
        }
    }

    private void UpdatePlayerDebuggingVisuals(PlayerConstructor player)
    {
        UpdatePlayerHandTrailDistorters(player);
        UpdatePlayerAttractionRadius(player);
        UpdatePlayerSecondaryAttractor(player);
    }

    private void UpdateAllPlayersDebuggingVisuals()
    {
        foreach (var player in players.Values)
        {
            if (player != null)
            {
                UpdatePlayerDebuggingVisuals(player.GetComponent<PlayerConstructor>());
            }
        }
    }
    #endregion
}
