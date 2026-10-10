using System;
using NaughtyAttributes;
using UnityEngine;

/// <summary>
/// Tunable values for the per-hand HandEffects.vfx graph (plus the two TD trail
/// distorter objects). Stored as base values at bodyScale = 1; every dimensioned
/// field carries a <see cref="BodyScaledAttribute"/> and every graph value a
/// <see cref="VfxPropertyAttribute"/>. Nested inside <see cref="RuntimeSceneSettings"/>
/// as <c>handVfx</c> and copied as one object at every plumbing site.
/// The SceneController inspector flattens these fields into the Particles section: the
/// [SettingGroup] / [Label] attributes and the field order match the in-game menu's
/// Particles groups. Per-value rules are derived in Docs/BodyScale.md.
/// </summary>
[Serializable]
public class HandVfxSettings
{
    // ---- Color (the player's particle gradient lives on RuntimeSceneSettings) ----
    [SettingGroup(SettingSections.Particles, "Color")]
    [Label("Color Cycle Time (s)")]
    [VfxProperty("colorCycleTime")]
    [Tooltip(
        "Seconds for a particle to travel through the player's aura gradient (sampled by age, "
            + "then held on the last color). Time - never scaled."
    )]
    public float colorCycleTime = 4f;

    // ---- Glow ----
    [SettingGroup(SettingSections.Particles, "Glow")]
    [Label("Full Radius (radii)")]
    [VfxProperty("glowFullRadius")]
    [Tooltip(
        "Distance from the vfxSphere centre, in vfxSphere radii, at which the surface glow is "
            + "full (1 = surface). Slightly above 1 keeps particles resting on the ball in the "
            + "clamped full-glow zone. Ratio - never scaled."
    )]
    public float glowFullRadius = 1.1f;

    [SettingGroup(SettingSections.Particles, "Glow")]
    [Label("Range (radii)")]
    [VfxProperty("glowRange")]
    [Tooltip(
        "How far outside glowFullRadius the surface glow starts ramping in, in vfxSphere radii. "
            + "Glow follows the particle's current distance (no collision lock). Ratio - never scaled."
    )]
    public float glowRange = 3f;

    [SettingGroup(SettingSections.Particles, "Glow")]
    [Label("Curve")]
    [VfxProperty("glowCurve")]
    [Tooltip(
        "Glow amount across the ramp. X 0 = glowRange out, X 1 = glowFullRadius. Y = glow (0-1)."
    )]
    public AnimationCurve glowCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [SettingGroup(SettingSections.Particles, "Glow")]
    [VfxProperty("surfaceBrightness")]
    [Tooltip(
        "Color multiplier at full glow, for the Multiply Color glow block (disabled by default; "
            + "the Blend Color block is the other option). Ratio - never scaled."
    )]
    public float surfaceBrightness = 3f;

    // ---- Emission ----
    [SettingGroup(SettingSections.Particles, "Emission")]
    [VfxProperty("spawnRate")]
    [Tooltip("Particles spawned per second (count - never scaled).")]
    public int spawnRate = 1500;

    [SettingGroup(SettingSections.Particles, "Emission")]
    [Label("Spawn Radius")]
    [BodyScaled(1), VfxProperty("spawnSphereRadius")]
    [Tooltip("Radius of the spawn sphere around the hand (m at 1x).")]
    public float spawnSphereRadius = 0.012f;

    [SettingGroup(SettingSections.Particles, "Emission")]
    [Label("Velocity Spread")]
    [BodyScaled(1), VfxProperty("spawnVeloSpread")]
    [Tooltip("Initial velocity spread (m/s at 1x).")]
    public float spawnVeloSpread = 0.1f;

    [SettingGroup(SettingSections.Particles, "Emission")]
    [BodyScaled(1), VfxProperty("sizeRange")]
    [Tooltip("Random per-particle size range (m at 1x).")]
    public Vector2 sizeRange = new(0.008f, 0.014f);

    // ---- Lifetime ----
    [SettingGroup(SettingSections.Particles, "Lifetime")]
    [Label("Max Lifetime (s)")]
    [VfxProperty("maxLifetime")]
    [Tooltip(
        "Fallback lifetime (s) for particles that never reach the ball, at closeProgress 0. "
            + "Arriving particles die fadeOutTime after contact instead. Time - never scaled."
    )]
    public float maxLifetime = 4f;

    [SettingGroup(SettingSections.Particles, "Lifetime")]
    [Label("Max Lifetime Closed (s)")]
    [VfxProperty("maxLifetimeClosed")]
    [Tooltip(
        "Fallback lifetime (s) at closeProgress 1; blended linearly from maxLifetime. Sampled at "
            + "spawn. Time - never scaled."
    )]
    public float maxLifetimeClosed = 1f;

    [SettingGroup(SettingSections.Particles, "Lifetime")]
    [Label("Fade In Time (s)")]
    [VfxProperty("fadeInTime")]
    [Tooltip(
        "Seconds after spawn for a particle to grow from size 0 to full size. Time - never scaled."
    )]
    public float fadeInTime = 0.1f;

    [SettingGroup(SettingSections.Particles, "Lifetime")]
    [VfxProperty("fadeInCurve")]
    [Tooltip(
        "Size multiplier over the fade-in. X 0 = spawn, X 1 = fadeInTime (remapped, so the shape "
            + "is independent of the duration). Y = fraction of full size."
    )]
    public AnimationCurve fadeInCurve = new(
        new Keyframe(0f, 0f, 0f, 2f),
        new Keyframe(1f, 1f, 0f, 0f)
    );

    [SettingGroup(SettingSections.Particles, "Lifetime")]
    [Label("Fade Out Time (s)")]
    [VfxProperty("fadeOutTime")]
    [Tooltip(
        "Seconds a particle takes to shrink and fade to 0 before death. On first contact with "
            + "the ball its lifetime is cut to age + fadeOutTime, so the fade starts there; "
            + "particles that never reach it fade over the last fadeOutTime of their lifetime. "
            + "Time - never scaled."
    )]
    public float fadeOutTime = 2f;

    // ---- Stretch ----
    [SettingGroup(SettingSections.Particles, "Stretch")]
    [BodyScaled(-1), VfxProperty("lengthScaler")]
    [Tooltip(
        "Velocity to stretch multiplier (s/m at 1x; converts a world velocity into the "
            + "dimensionless _Scale.y)."
    )]
    public float lengthScaler = 1.25f;

    [SettingGroup(SettingSections.Particles, "Stretch")]
    [VfxProperty("minStretchLength")]
    [Tooltip("Floor on the dimensionless _Scale.y stretch multiplier.")]
    public float minStretchLength = 0.01f;

    // ---- Ball Attraction (the main attractor, plus the collision shapes it uses) ----
    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [Label("Attraction Speed")]
    [BodyScaled(1), VfxProperty("mainAttractionSpeed")]
    [Tooltip("ConformToSDF attraction speed toward the ball surface (m/s at 1x).")]
    public float mainAttractionSpeed = 0.95f;

    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [Label("Attraction Force")]
    [BodyScaled(1), VfxProperty("mainAttractionForce")]
    [Tooltip("ConformToSDF attraction acceleration clamp (unit-mass particles, x s).")]
    public float mainAttractionForce = 43f;

    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [Label("Stick Distance")]
    [BodyScaled(1), VfxProperty("mainStickDistance")]
    [Tooltip("ConformToSDF stick distance from the ball surface (m at 1x).")]
    public float mainStickDistance = 0.02f;

    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [Label("Stick Force")]
    [BodyScaled(1), VfxProperty("mainStickForce")]
    [Tooltip("ConformToSDF stick acceleration (x s).")]
    public float mainStickForce = 0.4f;

    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [BodyScaled(1), VfxProperty("seekStrength")]
    [Tooltip("Heat-seeking steering velocity added toward the ball (m/s at 1x).")]
    public float seekStrength = 1f;

    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [Label("Tangential Damping (1/s)")]
    [VfxProperty("tangentialDamping")]
    [Tooltip(
        "Rate (1/s) at which tangential velocity is damped on collision; min(rate x dt, 1) blend. Never scaled."
    )]
    public float tangentialDamping = 5f;

    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [VfxProperty("tangentialDampingFade")]
    [Tooltip(
        "Distance range over which tangential damping fades in, as multiples of the vfxSphere's size (1 = its surface). x = start (no damping closer than this), y = end (full damping beyond). Ratio - never scaled."
    )]
    public Vector2 tangentialDampingFade = new Vector2(1f, 1.6f);

    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [VfxProperty("tangentialDampingFadeCurve")]
    [Tooltip(
        "Damping weight across the fade range. X 0 = fade start, X 1 = fade end (remapped, so the shape is independent of the distances). Y = fraction of tangential damping applied."
    )]
    public AnimationCurve tangentialDampingFadeCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [Label("Collision Shape Scale")]
    [VfxProperty("vfxSphereCollisionScaleMult")]
    [Tooltip(
        "Multiplier on the SDF collision shape size relative to vfxSphere (ratio - never scaled)."
    )]
    public float vfxSphereCollisionScaleMult = 0.93f;

    [SettingGroup(SettingSections.Particles, "Ball Attraction")]
    [Label("Collision Trigger Scale")]
    [VfxProperty("collisionDetectionScaleMult")]
    [Tooltip(
        "Multiplier on the collision-trigger sphere scale relative to vfxSphere (ratio - never scaled)."
    )]
    public float collisionDetectionScaleMult = 1.25f;

    // ---- Secondary Attractor ----
    [SettingGroup(SettingSections.Particles, "Secondary Attractor")]
    [Label("Attraction Speed")]
    [BodyScaled(1), VfxProperty("saAttractionSpeed")]
    [Tooltip("Secondary-attractor conform attraction speed (m/s at 1x).")]
    public float saAttractionSpeed = 0.95f;

    [SettingGroup(SettingSections.Particles, "Secondary Attractor")]
    [Label("Attraction Force")]
    [BodyScaled(1), VfxProperty("saAttractionForce")]
    [Tooltip("Secondary-attractor conform attraction acceleration clamp (x s).")]
    public float saAttractionForce = 10f;

    [SettingGroup(SettingSections.Particles, "Secondary Attractor")]
    [Label("Stick Distance")]
    [BodyScaled(1), VfxProperty("saStickDistance")]
    [Tooltip("Secondary-attractor conform stick distance (m at 1x).")]
    public float saStickDistance = 0.02f;

    [SettingGroup(SettingSections.Particles, "Secondary Attractor")]
    [Label("Stick Force")]
    [BodyScaled(1), VfxProperty("saStickForce")]
    [Tooltip("Secondary-attractor conform stick acceleration (x s).")]
    public float saStickForce = 1f;

    [SettingGroup(SettingSections.Particles, "Secondary Attractor")]
    [Label("Min Radius")]
    [BodyScaled(1), VfxProperty("saMinRadius")]
    [Tooltip("Minimum radius of the secondary attractor sphere (m at 1x).")]
    public float saMinRadius = 0f;

    // ---- Trail Distorters ----
    [SettingGroup(SettingSections.Particles, "Trail Distorters")]
    [Label("Radius")]
    [BodyScaled(1), VfxProperty("tdRadius")]
    [Tooltip(
        "Conform-to-sphere radius of the TD1/TD2 trail distorters (m at 1x). "
            + "Also drives the TD debug sphere size."
    )]
    public float tdRadius = 0.1f;

    [SettingGroup(SettingSections.Particles, "Trail Distorters")]
    [Label("Attraction Speed")]
    [BodyScaled(1), VfxProperty("tdAttractionSpeed")]
    [Tooltip("TD conform attraction speed (m/s at 1x).")]
    public float tdAttractionSpeed = 40f;

    [SettingGroup(SettingSections.Particles, "Trail Distorters")]
    [Label("Attraction Force")]
    [BodyScaled(1), VfxProperty("tdAttractionForce")]
    [Tooltip("TD conform attraction acceleration clamp (x s).")]
    public float tdAttractionForce = 2f;

    [SettingGroup(SettingSections.Particles, "Trail Distorters")]
    [Label("Stick Distance")]
    [BodyScaled(1), VfxProperty("tdStickDistance")]
    [Tooltip("TD conform stick distance (m at 1x).")]
    public float tdStickDistance = 0.02f;

    [SettingGroup(SettingSections.Particles, "Trail Distorters")]
    [Label("Stick Force")]
    [BodyScaled(1), VfxProperty("tdStickForce")]
    [Tooltip("TD conform stick acceleration (x s).")]
    public float tdStickForce = 1f;

    [SettingGroup(SettingSections.Particles, "Trail Distorters")]
    [Label("Wander Amount")]
    [BodyScaled(1)]
    [Tooltip(
        "BrownianMotion.positionAmount of the TD1/TD2 objects - wander amplitude (m at 1x). "
            + "Not a graph value."
    )]
    public float tdWanderAmount = 0.06f;

    // ---- Noise & Turbulence ----
    [SettingGroup(SettingSections.Particles, "Noise & Turbulence")]
    [BodyScaled(1), VfxProperty("noiseScale")]
    [Tooltip("Curl-noise positional amplitude (m at 1x).")]
    public float noiseScale = 0.02f;

    [SettingGroup(SettingSections.Particles, "Noise & Turbulence")]
    [BodyScaled(-1), VfxProperty("noiseFrequency")]
    [Tooltip("Curl-noise spatial frequency (1/m at 1x).")]
    public float noiseFrequency = 4.5f;

    [SettingGroup(SettingSections.Particles, "Noise & Turbulence")]
    [VfxProperty("noiseRoughness")]
    [Tooltip("Curl-noise fractal gain (dimensionless).")]
    public float noiseRoughness = 0.5f;

    [SettingGroup(SettingSections.Particles, "Noise & Turbulence")]
    [VfxProperty("noiseOctaves")]
    [Tooltip("Curl-noise octave count.")]
    public int noiseOctaves = 1;

    [SettingGroup(SettingSections.Particles, "Noise & Turbulence")]
    [BodyScaled(1), VfxProperty("turbulenceIntensity")]
    [Tooltip("Turbulence block intensity (acceleration, x s).")]
    public float turbulenceIntensity = 1f;

    [SettingGroup(SettingSections.Particles, "Noise & Turbulence")]
    [BodyScaled(-1), VfxProperty("turbulenceFrequency")]
    [Tooltip("Turbulence block spatial frequency (1/m at 1x).")]
    public float turbulenceFrequency = 5f;

    // ---- Hi-Hat Bursts (closed hat / open hat) ----
    [SettingGroup(SettingSections.Particles, "Hi-Hat Bursts")]
    [Label("Closed Size")]
    [BodyScaled(1), VfxProperty("cHatSize")]
    [Tooltip("Closed-hat burst particle size (m at 1x).")]
    public float cHatSize = 0.005f;

    [SettingGroup(SettingSections.Particles, "Hi-Hat Bursts")]
    [Label("Closed Noise Amp")]
    [BodyScaled(1), VfxProperty("cHatNoiseAmp")]
    [Tooltip("Closed-hat turbulence amplitude (acceleration, x s).")]
    public float cHatNoiseAmp = 1f;

    [SettingGroup(SettingSections.Particles, "Hi-Hat Bursts")]
    [Label("Closed Noise Freq")]
    [BodyScaled(-1), VfxProperty("cHatNoiseFreq")]
    [Tooltip("Closed-hat turbulence spatial frequency (1/m at 1x).")]
    public float cHatNoiseFreq = 20f;

    [SettingGroup(SettingSections.Particles, "Hi-Hat Bursts")]
    [Label("Closed Noise Y Scroll")]
    [BodyScaled(1), VfxProperty("cHatNoiseYScroll")]
    [Tooltip("Closed-hat noise Y scroll speed (m/s at 1x).")]
    public float cHatNoiseYScroll = 0.08f;

    [SettingGroup(SettingSections.Particles, "Hi-Hat Bursts")]
    [Label("Closed Spawn Velocity")]
    [BodyScaled(1), VfxProperty("cHatSpawnVeloSphereRadius")]
    [Tooltip("Closed-hat spawn velocity sphere radius (m/s at 1x).")]
    public float cHatSpawnVeloSphereRadius = 1f;

    [SettingGroup(SettingSections.Particles, "Hi-Hat Bursts")]
    [Label("Open Size")]
    [BodyScaled(1), VfxProperty("oHatSize")]
    [Tooltip("Open-hat burst particle size (m at 1x).")]
    public float oHatSize = 0.004f;

    [SettingGroup(SettingSections.Particles, "Hi-Hat Bursts")]
    [Label("Open Noise Amp")]
    [BodyScaled(1), VfxProperty("oHatNoiseAmp")]
    [Tooltip("Open-hat noise-field positional amplitude (m at 1x).")]
    public float oHatNoiseAmp = 0.008f;

    [SettingGroup(SettingSections.Particles, "Hi-Hat Bursts")]
    [Label("Open Noise Freq")]
    [BodyScaled(-1), VfxProperty("oHatNoiseFreq")]
    [Tooltip("Open-hat noise-field spatial frequency (1/m at 1x).")]
    public float oHatNoiseFreq = 10f;

    [SettingGroup(SettingSections.Particles, "Hi-Hat Bursts")]
    [Label("Open Noise Y Scroll")]
    [BodyScaled(1), VfxProperty("oHatNoiseYScroll")]
    [Tooltip("Open-hat noise Y scroll speed (m/s at 1x).")]
    public float oHatNoiseYScroll = 0.2f;

    // ---- Snare Bursts ----
    [SettingGroup(SettingSections.Particles, "Snare Bursts")]
    [Label("Size Range")]
    [BodyScaled(1), VfxProperty("snareSizeRange")]
    [Tooltip("Snare burst size range (m at 1x).")]
    public Vector2 snareSizeRange = new(0.016f, 0.03f);

    [SettingGroup(SettingSections.Particles, "Snare Bursts")]
    [Label("Radius Range")]
    [BodyScaled(1), VfxProperty("snareRadiusRandRange")]
    [Tooltip("Snare spawn-circle radius random range (m at 1x).")]
    public Vector2 snareRadiusRandRange = new(-0.02f, 0.04f);

    [SettingGroup(SettingSections.Particles, "Snare Bursts")]
    [Label("Spawn Velocity")]
    [BodyScaled(1), VfxProperty("snareSpawnVeloSphereRadius")]
    [Tooltip("Snare spawn velocity sphere radius (m/s at 1x).")]
    public float snareSpawnVeloSphereRadius = 0.2f;

    public HandVfxSettings DeepCopy()
    {
        // Every field is a value type except the curves, which must be copied by keys.
        var copy = (HandVfxSettings)MemberwiseClone();
        copy.tangentialDampingFadeCurve = new AnimationCurve(tangentialDampingFadeCurve.keys);
        copy.fadeInCurve = new AnimationCurve(fadeInCurve.keys);
        copy.glowCurve = new AnimationCurve(glowCurve.keys);
        return copy;
    }
}
