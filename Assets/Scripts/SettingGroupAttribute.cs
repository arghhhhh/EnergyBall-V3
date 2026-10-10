using System;

/// <summary>
/// Places a SceneController inspector field (or a <see cref="HandVfxSettings"/> field, which the
/// inspector flattens) in a section and group, mirroring the in-game menu's layout. Section and
/// group names must match the ones <see cref="InGameSettingsMenu"/> builds.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class SettingGroupAttribute : Attribute
{
    public string Section { get; }
    public string Group { get; }

    public SettingGroupAttribute(string section, string group)
    {
        Section = section;
        Group = group;
    }
}

/// <summary>
/// The settings sections, shared by the in-game menu and the SceneController inspector.
/// <see cref="Order"/> is the order both draw them in.
/// </summary>
public static class SettingSections
{
    public const string Space = "Space";
    public const string Kinect = "Kinect";
    public const string Ball = "Ball";
    public const string Hands = "Hands";
    public const string Particles = "Particles";
    public const string Debug = "Debug";

    public static readonly string[] Order = { Space, Kinect, Ball, Hands, Particles, Debug };
}
