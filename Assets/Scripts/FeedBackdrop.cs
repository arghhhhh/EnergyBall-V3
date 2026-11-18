using UnityEngine;

/// <summary>
/// What the camera feed quad shows behind the particles (Kinect > Camera Feed > Backdrop).
/// Color aligns everything (feed image, joints, body occluder) in the color camera's space; the
/// other modes align in the depth camera's space, which the infrared image shares and the bodies
/// are tracked in.
/// </summary>
public enum FeedBackdrop
{
    [InspectorName("None")]
    None = 0,

    [InspectorName("Color")]
    Color = 1,

    [InspectorName("Infrared")]
    Infrared = 2,

    [InspectorName("Infrared + Color")]
    InfraredColor = 3,
}

/// <summary>How the feed image is sized against the screen when their aspect ratios differ.</summary>
public enum FeedFraming
{
    /// <summary>The whole image is visible; bars where the screen is wider or taller.</summary>
    Fit = 0,

    /// <summary>The image covers the screen; the overhang is cropped.</summary>
    Fill = 1,
}

/// <summary>The camera whose image the feed quad shows and that joints are projected through.</summary>
public enum FeedSpace
{
    Color,
    Depth,
}

public static class FeedBackdropExtensions
{
    public static bool IsShown(this FeedBackdrop backdrop) => backdrop != FeedBackdrop.None;

    /// <summary>The backdrop reads the color stream (the image or its hue).</summary>
    public static bool UsesColorStream(this FeedBackdrop backdrop) =>
        backdrop == FeedBackdrop.Color || backdrop == FeedBackdrop.InfraredColor;

    public static bool UsesInfrared(this FeedBackdrop backdrop) =>
        backdrop == FeedBackdrop.Infrared || backdrop == FeedBackdrop.InfraredColor;

    /// <summary>
    /// Only the color image is aligned in color space. None uses depth space: it needs no
    /// color work at all, and the depth camera is the one the bodies are tracked in.
    /// </summary>
    public static FeedSpace Space(this FeedBackdrop backdrop) =>
        backdrop == FeedBackdrop.Color ? FeedSpace.Color : FeedSpace.Depth;
}
