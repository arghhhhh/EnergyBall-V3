using System;
using UnityEngine;

/// <summary>
/// Tunable values for the per-hand HandEffects.vfx graph (plus the two TD trail
/// distorter objects). Stored as base values at bodyScale = 1; every dimensioned
/// field carries a <see cref="BodyScaledAttribute"/> and every graph value a
/// <see cref="VfxPropertyAttribute"/>. Nested inside <see cref="RuntimeSceneSettings"/>
/// as <c>handVfx</c> and copied as one object at every plumbing site.
/// The [Header] group names match the in-game menu groups. Per-value rules are
/// derived in Docs/HandEffects-scale-audit.md.
/// </summary>
[Serializable]
public class HandVfxSettings
{
    [Header("Spawn & Size")]
    [VfxProperty("spawnRate")]
    [Tooltip("Particles spawned per second (count - never scaled).")]
    public int spawnRate = 1500;

    [BodyScaled(1), VfxProperty("spawnSphereRadius")]
    [Tooltip("Radius of the spawn sphere around the hand (m at 1x).")]
    public float spawnSphereRadius = 0.012f;

    [BodyScaled(1), VfxProperty("spawnVeloSpread")]
    [Tooltip("Initial velocity spread (m/s at 1x).")]
    public float spawnVeloSpread = 0.1f;

    [BodyScaled(1), VfxProperty("sizeRange")]
    [Tooltip("Random per-particle size range (m at 1x).")]
    public Vector2 sizeRange = new(0.008f, 0.014f);

    [BodyScaled(1), VfxProperty("lifetimeRemapMaxDist")]
    [Tooltip("Hand-to-ball distance at which particle lifetime hits its max (m at 1x).")]
    public float lifetimeRemapMaxDist = 0.6f;

    [BodyScaled(1), VfxProperty("boundsPadding")]
    [Tooltip("Extra padding on the particle system bounds (m at 1x).")]
    public Vector3 boundsPadding = Vector3.zero;

    [Header("Main Attractor")]
    [BodyScaled(1), VfxProperty("mainAttractionSpeed")]
    [Tooltip("ConformToSDF attraction speed toward the ball surface (m/s at 1x).")]
    public float mainAttractionSpeed = 0.95f;

    [BodyScaled(1), VfxProperty("mainAttractionForce")]
    [Tooltip("ConformToSDF attraction acceleration clamp (unit-mass particles, x s).")]
    public float mainAttractionForce = 43f;

    [BodyScaled(1), VfxProperty("mainStickDistance")]
    [Tooltip("ConformToSDF stick distance from the ball surface (m at 1x).")]
    public float mainStickDistance = 0.02f;

    [BodyScaled(1), VfxProperty("mainStickForce")]
    [Tooltip("ConformToSDF stick acceleration (x s).")]
    public float mainStickForce = 0.4f;

    [BodyScaled(1), VfxProperty("seekStrength")]
    [Tooltip("Heat-seeking steering velocity added toward the ball (m/s at 1x).")]
    public float seekStrength = 1f;

    [Header("Trail Distorters")]
    [BodyScaled(1), VfxProperty("tdRadius")]
    [Tooltip(
        "Conform-to-sphere radius of the TD1/TD2 trail distorters (m at 1x). "
            + "Also drives the TD debug sphere size."
    )]
    public float tdRadius = 0.1f;

    [BodyScaled(1), VfxProperty("tdStickDistance")]
    [Tooltip("TD conform stick distance (m at 1x).")]
    public float tdStickDistance = 0.02f;

    [BodyScaled(1), VfxProperty("tdStickForce")]
    [Tooltip("TD conform stick acceleration (x s).")]
    public float tdStickForce = 1f;

    [BodyScaled(1), VfxProperty("tdAttractionForce")]
    [Tooltip("TD conform attraction acceleration clamp (x s).")]
    public float tdAttractionForce = 2f;

    [BodyScaled(1), VfxProperty("tdAttractionSpeed")]
    [Tooltip("TD conform attraction speed (m/s at 1x).")]
    public float tdAttractionSpeed = 40f;

    [BodyScaled(1)]
    [Tooltip(
        "BrownianMotion.positionAmount of the TD1/TD2 objects - wander amplitude (m at 1x). "
            + "Not a graph value."
    )]
    public float tdWanderAmount = 0.06f;

    [Header("Secondary Attractor")]
    [BodyScaled(1), VfxProperty("saAttractionSpeed")]
    [Tooltip("Secondary-attractor conform attraction speed (m/s at 1x).")]
    public float saAttractionSpeed = 0.95f;

    [BodyScaled(1), VfxProperty("saAttractionForce")]
    [Tooltip("Secondary-attractor conform attraction acceleration clamp (x s).")]
    public float saAttractionForce = 10f;

    [BodyScaled(1), VfxProperty("saStickDistance")]
    [Tooltip("Secondary-attractor conform stick distance (m at 1x).")]
    public float saStickDistance = 0.02f;

    [BodyScaled(1), VfxProperty("saStickForce")]
    [Tooltip("Secondary-attractor conform stick acceleration (x s).")]
    public float saStickForce = 1f;

    [BodyScaled(1), VfxProperty("saMinRadius")]
    [Tooltip("Minimum radius of the secondary attractor sphere (m at 1x).")]
    public float saMinRadius = 0f;

    [Header("Noise & Turbulence")]
    [BodyScaled(1), VfxProperty("noiseScale")]
    [Tooltip("Curl-noise positional amplitude (m at 1x).")]
    public float noiseScale = 0.02f;

    [BodyScaled(-1), VfxProperty("noiseFrequency")]
    [Tooltip("Curl-noise spatial frequency (1/m at 1x).")]
    public float noiseFrequency = 4.5f;

    [VfxProperty("noiseRoughness")]
    [Tooltip("Curl-noise fractal gain (dimensionless).")]
    public float noiseRoughness = 0.5f;

    [VfxProperty("noiseOctaves")]
    [Tooltip("Curl-noise octave count.")]
    public int noiseOctaves = 1;

    [BodyScaled(1), VfxProperty("turbulenceIntensity")]
    [Tooltip("Turbulence block intensity (acceleration, x s).")]
    public float turbulenceIntensity = 1f;

    [BodyScaled(-1), VfxProperty("turbulenceFrequency")]
    [Tooltip("Turbulence block spatial frequency (1/m at 1x).")]
    public float turbulenceFrequency = 5f;

    [Header("Stretch")]
    [BodyScaled(-1), VfxProperty("lengthScaler")]
    [Tooltip(
        "Velocity to stretch multiplier (s/m at 1x; converts a world velocity into the "
            + "dimensionless _Scale.y)."
    )]
    public float lengthScaler = 1.25f;

    [VfxProperty("minStretchLength")]
    [Tooltip("Floor on the dimensionless _Scale.y stretch multiplier.")]
    public float minStretchLength = 0.01f;

    [Header("Bursts (CHat/OHat)")]
    [BodyScaled(1), VfxProperty("cHatSize")]
    [Tooltip("Closed-hat burst particle size (m at 1x).")]
    public float cHatSize = 0.005f;

    [BodyScaled(1), VfxProperty("cHatNoiseAmp")]
    [Tooltip("Closed-hat turbulence amplitude (acceleration, x s).")]
    public float cHatNoiseAmp = 1f;

    [BodyScaled(-1), VfxProperty("cHatNoiseFreq")]
    [Tooltip("Closed-hat turbulence spatial frequency (1/m at 1x).")]
    public float cHatNoiseFreq = 20f;

    [BodyScaled(1), VfxProperty("cHatNoiseYScroll")]
    [Tooltip("Closed-hat noise Y scroll speed (m/s at 1x).")]
    public float cHatNoiseYScroll = 0.08f;

    [BodyScaled(1), VfxProperty("cHatSpawnVeloSphereRadius")]
    [Tooltip("Closed-hat spawn velocity sphere radius (m/s at 1x).")]
    public float cHatSpawnVeloSphereRadius = 1f;

    [BodyScaled(1), VfxProperty("oHatSize")]
    [Tooltip("Open-hat burst particle size (m at 1x).")]
    public float oHatSize = 0.004f;

    [BodyScaled(1), VfxProperty("oHatNoiseAmp")]
    [Tooltip("Open-hat noise-field positional amplitude (m at 1x).")]
    public float oHatNoiseAmp = 0.008f;

    [BodyScaled(-1), VfxProperty("oHatNoiseFreq")]
    [Tooltip("Open-hat noise-field spatial frequency (1/m at 1x).")]
    public float oHatNoiseFreq = 10f;

    [BodyScaled(1), VfxProperty("oHatNoiseYScroll")]
    [Tooltip("Open-hat noise Y scroll speed (m/s at 1x).")]
    public float oHatNoiseYScroll = 0.2f;

    [Header("Snare")]
    [BodyScaled(1), VfxProperty("snareSizeRange")]
    [Tooltip("Snare burst size range (m at 1x).")]
    public Vector2 snareSizeRange = new(0.016f, 0.03f);

    [BodyScaled(1), VfxProperty("snareRadiusRandRange")]
    [Tooltip("Snare spawn-circle radius random range (m at 1x).")]
    public Vector2 snareRadiusRandRange = new(-0.02f, 0.04f);

    [BodyScaled(1), VfxProperty("snareSpawnVeloSphereRadius")]
    [Tooltip("Snare spawn velocity sphere radius (m/s at 1x).")]
    public float snareSpawnVeloSphereRadius = 0.2f;

    public HandVfxSettings DeepCopy()
    {
        // Every field is a value type, so a member-wise copy is already deep.
        return (HandVfxSettings)MemberwiseClone();
    }
}
