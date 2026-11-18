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

    /// <summary>Camera feed frames delivered so far (drives Sync Particles To Feed).</summary>
    int ColorFrameCount { get; }

    /// <summary>Camera feed image size in pixels.</summary>
    int ColorWidth { get; }
    int ColorHeight { get; }

    /// <summary>
    /// Maps a camera-space point (metres) to its pixel in the camera feed image (top-left origin,
    /// image rows top to bottom, as the feed quad displays them). False when it can't.
    /// </summary>
    bool TryMapCameraPointToColor(CameraSpacePoint point, out Vector2 pixel);
}
