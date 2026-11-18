using System.Collections.Generic;
using UnityEngine;
using Windows.Kinect;
using Joint = Windows.Kinect.Joint;

/// <summary>
/// A managed snapshot of one body in one body frame: what <see cref="SceneController"/> reads.
/// The live Kinect fills it from a native <see cref="Body"/> once per new body frame
/// (<see cref="CopyFrom"/>), and a recording fills it straight from file data, so gameplay
/// doesn't care where a body came from. Instances and their joint dictionaries are reused.
/// </summary>
public class TrackedBody
{
    public ulong TrackingId;
    public bool IsTracked;
    public bool IsRestricted;
    public HandState HandLeftState;
    public HandState HandRightState;
    public TrackingConfidence HandLeftConfidence;
    public TrackingConfidence HandRightConfidence;

    /// <summary>All 25 joints. Joints a source doesn't know are NotTracked.</summary>
    public readonly Dictionary<JointType, Joint> Joints = new(25);

    public TrackedBody()
    {
        Clear();
    }

    /// <summary>Untracked, with every joint NotTracked at the origin.</summary>
    public void Clear()
    {
        TrackingId = 0;
        IsTracked = false;
        IsRestricted = false;
        HandLeftState = HandState.NotTracked;
        HandRightState = HandState.NotTracked;
        HandLeftConfidence = TrackingConfidence.Low;
        HandRightConfidence = TrackingConfidence.Low;
        for (int i = 0; i <= (int)JointType.ThumbRight; i++)
        {
            var type = (JointType)i;
            Joints[type] = new Joint { JointType = type, TrackingState = TrackingState.NotTracked };
        }
    }

    /// <summary>
    /// Copies a native Kinect body. Each property read marshals from native code (Joints builds
    /// a new dictionary), so this reads each one once.
    /// </summary>
    public void CopyFrom(Body body)
    {
        if (body == null || !body.IsTracked)
        {
            Clear();
            return;
        }

        TrackingId = body.TrackingId;
        IsTracked = true;
        IsRestricted = body.IsRestricted;
        HandLeftState = body.HandLeftState;
        HandRightState = body.HandRightState;
        HandLeftConfidence = body.HandLeftConfidence;
        HandRightConfidence = body.HandRightConfidence;
        foreach (var pair in body.Joints)
        {
            Joints[pair.Key] = pair.Value;
        }
    }
}

/// <summary>
/// Where players' bodies and the camera feed come from: the live Kinect
/// (<see cref="BodySourceManager"/>) or a recorded clip (<see cref="RecordedBodySource"/>).
/// </summary>
public interface IBodySource
{
    /// <summary>The bodies of the latest body frame (some may be untracked), or null before one.</summary>
    TrackedBody[] GetBodies();

    /// <summary>Body frames delivered so far. Tells a new frame from one seen on an earlier step.</summary>
    int BodyFrameCount { get; }

    /// <summary>
    /// Frames delivered so far by the camera the backdrop is drawn from (drives Sync Particles
    /// To Feed). A recording has one image stream and ignores the backdrop.
    /// </summary>
    int FeedFrameCount(FeedBackdrop backdrop);

    /// <summary>
    /// Maps a camera-space point (metres) to its normalized position in the feed image (0..1,
    /// top-left origin, the layout the feed quad shows). The Kinect maps into the color or depth
    /// camera per <paramref name="space"/>; a recording maps into its own video. False when it
    /// can't.
    /// </summary>
    bool TryMapCameraPointToFeed(CameraSpacePoint point, FeedSpace space, out Vector2 uv);
}

/// <summary>
/// Per-frame depth for the body occluder (<see cref="BodyDepthOccluder"/>): the Kinect's depth
/// camera, or depth and person masks extracted from a recorded clip. Textures are created once
/// and updated in place.
/// </summary>
public interface IDepthFrameSource
{
    /// <summary>True once the textures hold a frame.</summary>
    bool DepthFramesReady { get; }

    /// <summary>
    /// R16: depth along the camera's Z axis in millimetres / 65535; 0 = no depth. The Kinect's
    /// is its hole-filled copy (see BodySourceManager.FillOccluderHoles).
    /// </summary>
    Texture2D OccluderDepthTexture { get; }

    /// <summary>R8: body index 0-5 on a tracked person's pixels, 255 elsewhere.</summary>
    Texture2D OccluderBodyIndexTexture { get; }

    /// <summary>RGFloat: each depth pixel's camera feed pixel (top-left origin); -Infinity = none.</summary>
    Texture2D DepthToColorTexture { get; }

    int DepthWidth { get; }
    int DepthHeight { get; }
    int ColorWidth { get; }
    int ColorHeight { get; }
}
