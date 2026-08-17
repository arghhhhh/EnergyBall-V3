using System;

/// <summary>
/// Names the exposed HandEffects.vfx graph property a <see cref="HandVfxSettings"/>
/// field is pushed to by <see cref="PlayerScaleApplier"/>. The field type must match
/// the graph slot type (float / int / Vector2 / Vector3).
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class VfxPropertyAttribute : Attribute
{
    public string Name { get; }

    public VfxPropertyAttribute(string name) => Name = name;
}
