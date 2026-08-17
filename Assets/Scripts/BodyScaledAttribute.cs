using System;

/// <summary>
/// Marks a settings field whose stored value is a base value at bodyScale = 1.
/// <see cref="BodyScaling.CreateEffective"/> multiplies it by
/// <c>bodyScale^Exponent</c> when deriving the effective settings consumers read.
///
/// Exponents: lengths / velocities / per-frame displacements 1; rigidbody forces
/// 2 (mass is proportional to s); spatial frequencies -1; time, ratios, rates,
/// curves, bools 0 (no attribute). Hand-VFX "forces" act on unit-mass particles
/// and are 1. See Docs/BodyScale-settings-audit.md for the derivation.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class BodyScaledAttribute : Attribute
{
    public int Exponent { get; }

    public BodyScaledAttribute(int exponent = 1) => Exponent = exponent;
}
