using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;
using Windows.Kinect;
using Joint = Windows.Kinect.Joint;

/// <summary>
/// Replays a recorded clip in place of the Kinect: the video becomes the camera feed and a
/// bodies file (camera-space skeletons in metres plus per-hand open/closed, extracted from the
/// same video, see the energy-ball-footage pipeline) becomes the tracked bodies. Enabling this
/// component on the SceneController's GameObject switches the scene to it; the sensor isn't
/// opened.
///
/// Body frames follow the video's own frames (VideoPlayer.frameReady), so each displayed frame
/// gets the skeleton extracted from it. Joints map to feed pixels through the clip's pinhole
/// intrinsics, so Projective Alignment lines balls and hands up with the video like it does
/// with the Kinect's color camera.
///
/// Keys (menu closed): Space pauses / resumes, R restarts the clip.
/// </summary>
[RequireComponent(typeof(SceneController))]
public class RecordedBodySource : MonoBehaviour, IBodySource
{
    public enum HandStateMode
    {
        /// <summary>The open / closed state stored in the file (already debounced).</summary>
        Recorded,

        /// <summary>Threshold the raw curl here, with the hysteresis below.</summary>
        CurlThreshold,
    }

    [Header("Clip")]
    [Tooltip(
        "Folder holding the clip files. Relative paths are under StreamingAssets; absolute "
            + "paths are used as they are."
    )]
    public string clipFolder = "RecordedFootage";

    [Tooltip("The video file in the clip folder.")]
    public string videoFile = "1.mp4";

    [Tooltip("The bodies file extracted from that video (<n>_bodies3d.json).")]
    public string bodiesFile = "1_bodies3d.json";

    public bool loop = true;

    [Range(0.1f, 2f)]
    public float playbackSpeed = 1f;

    [Tooltip("Seconds into the clip playback starts (and restarts) at.")]
    [Min(0f)]
    public float startTime = 0f;

    [Header("Display")]
    [Tooltip(
        "Set the render camera (and its child cameras) to the clip's vertical field of view, so "
            + "ball sizes and distances read as real metres at the subject's depth."
    )]
    public bool matchCameraFov = true;

    [Tooltip(
        "Resize the camera feed quad to the clip's aspect ratio and fit it to the camera's view "
            + "height. With a Game view / output of the same aspect, it fills the screen."
    )]
    public bool fitFeedQuadToCamera = true;

    [Header("Bodies")]
    [Tooltip("Joints below this confidence are marked Inferred (eased by Inferred Joint Smoothing).")]
    [Range(0f, 1f)]
    public float minJointConfidence = 0.3f;

    public HandStateMode handStateMode = HandStateMode.Recorded;

    [Tooltip("CurlThreshold mode: a hand closes when its curl rises above this (1 = fist).")]
    [Range(0f, 1f)]
    public float closedCurlAbove = 0.7f;

    [Tooltip("CurlThreshold mode: a closed hand opens when its curl drops below this.")]
    [Range(0f, 1f)]
    public float openCurlBelow = 0.52f;

    [Header("Status (read-only)")]
    [SerializeField]
    private int currentFrame = -1;

    [SerializeField]
    private int clipFrameCount;

    [SerializeField]
    private string clipStatus = "Not loaded";

    // IBodySource
    public int BodyFrameCount { get; private set; }
    public int ColorFrameCount { get; private set; }
    public int ColorWidth => clip != null ? clip.width : 1;
    public int ColorHeight => clip != null ? clip.height : 1;

    private SceneController controller;
    private VideoPlayer videoPlayer;
    private RenderTexture videoTexture; // what the VideoPlayer decodes into (Unity row order)
    private RenderTexture feedTexture; // image top in row 0, like the Kinect color buffer
    private Renderer feedRenderer;
    private MaterialPropertyBlock feedProperties;

    private ClipFile clip;
    private Dictionary<int, int> bodySlotById;
    private TrackedBody[] bodies;
    private HandState[] curlStates; // CurlThreshold mode: per slot, [left, right]

    public TrackedBody[] GetBodies() => bodies;

    public bool TryMapCameraPointToColor(CameraSpacePoint point, out Vector2 pixel)
    {
        pixel = default;
        if (clip == null || point.Z <= 0.01f)
            return false;

        // Kinect camera space: +X toward the camera's left, +Y up, +Z away from it. Image
        // pixels: +u right, +v down.
        var k = clip.intrinsics;
        pixel = new Vector2(k.cx - k.fx * point.X / point.Z, k.cy - k.fy * point.Y / point.Z);
        return true;
    }

    private void Awake()
    {
        controller = GetComponent<SceneController>();
    }

    private void Start()
    {
        if (controller.dummyOnlyMode)
        {
            Debug.LogWarning(
                "RecordedBodySource: Dummy Only Mode is on, so recorded bodies are ignored."
            );
        }

        string folder = Path.IsPathRooted(clipFolder)
            ? clipFolder
            : Path.Combine(Application.streamingAssetsPath, clipFolder);
        string bodiesPath = Path.Combine(folder, bodiesFile);
        string videoPath = Path.Combine(folder, videoFile);

        if (!LoadBodies(bodiesPath))
            return;
        if (!File.Exists(videoPath))
        {
            Fail($"video not found: {videoPath}");
            return;
        }

        SetUpDisplay();
        SetUpVideo(videoPath);
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
            videoPlayer.frameReady -= OnFrameReady;
        ReleaseTexture(ref videoTexture);
        ReleaseTexture(ref feedTexture);
    }

    private void Update()
    {
        if (videoPlayer == null)
            return;

        var keyboard = Keyboard.current;
        if (keyboard == null || (controller.settingsMenu != null && controller.settingsMenu.IsMenuOpen))
            return;

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            if (videoPlayer.isPlaying)
                videoPlayer.Pause();
            else
                videoPlayer.Play();
        }
        else if (keyboard.rKey.wasPressedThisFrame)
        {
            videoPlayer.time = startTime;
            videoPlayer.Play();
        }
    }

    #region Loading

    private bool LoadBodies(string path)
    {
        if (!File.Exists(path))
        {
            Fail($"bodies file not found: {path}");
            return false;
        }

        try
        {
            clip = JsonConvert.DeserializeObject<ClipFile>(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            Fail($"couldn't read {path}: {e.Message}");
            return false;
        }

        if (clip?.frames == null || clip.intrinsics == null || clip.width <= 0 || clip.height <= 0)
        {
            Fail($"{path} is missing frames, intrinsics or the image size.");
            clip = null;
            return false;
        }
        if (clip.space != "camera_m")
        {
            Debug.LogWarning(
                $"RecordedBodySource: {bodiesFile} space is '{clip.space}', expected 'camera_m'."
            );
        }

        // One body slot per recorded id, for the whole clip.
        bodySlotById = new Dictionary<int, int>();
        foreach (var frame in clip.frames)
        {
            if (frame.bodies == null)
                continue;
            foreach (var body in frame.bodies)
            {
                if (!bodySlotById.ContainsKey(body.id))
                    bodySlotById[body.id] = bodySlotById.Count;
            }
        }

        bodies = new TrackedBody[Mathf.Max(1, bodySlotById.Count)];
        for (int i = 0; i < bodies.Length; i++)
            bodies[i] = new TrackedBody();
        curlStates = new HandState[bodies.Length * 2];

        clipFrameCount = clip.frames.Count;
        CheckProjection();
        return true;
    }

    /// <summary>
    /// The joints' stored image coordinates must match their reprojected camera-space positions,
    /// or the balls won't sit on the hands. Warns when the file's axes don't agree.
    /// </summary>
    private void CheckProjection()
    {
        float errorSum = 0f;
        int count = 0;
        for (int f = 0; f < clip.frames.Count && count < 500; f += 10)
        {
            var frameBodies = clip.frames[f].bodies;
            if (frameBodies == null)
                continue;
            foreach (var body in frameBodies)
            {
                if (body.joints == null)
                    continue;
                foreach (var joint in body.joints.Values)
                {
                    if (
                        joint.u == null
                        || joint.v == null
                        || !TryMapCameraPointToColor(ToCameraPoint(joint), out Vector2 pixel)
                    )
                        continue;
                    errorSum +=
                        Mathf.Abs(pixel.x / clip.width - joint.u.Value)
                        + Mathf.Abs(pixel.y / clip.height - joint.v.Value);
                    count++;
                }
            }
        }

        if (count > 0 && errorSum / count > 0.02f)
        {
            Debug.LogWarning(
                $"RecordedBodySource: {bodiesFile} joints reproject {errorSum / count:P1} of the "
                    + "image away from their stored u/v. The file's axes may not be Kinect camera "
                    + "space, so balls won't line up with the video."
            );
        }
    }

    private void SetUpDisplay()
    {
        float verticalFov = 2f * Mathf.Atan(clip.height * 0.5f / clip.intrinsics.fy) * Mathf.Rad2Deg;

        Transform renderCamera = controller.renderCameraTransform;
        if (matchCameraFov && renderCamera != null)
        {
            foreach (var cam in renderCamera.GetComponentsInChildren<Camera>(true))
                cam.fieldOfView = verticalFov;
        }

        Transform quad = controller.cameraFeedQuad;
        if (quad == null)
        {
            Debug.LogWarning(
                "RecordedBodySource: no Camera Feed Quad is assigned, so the video isn't shown."
            );
            return;
        }

        if (fitFeedQuadToCamera && renderCamera != null)
        {
            float distance = renderCamera.InverseTransformPoint(quad.position).z;
            Camera cam = renderCamera.GetComponent<Camera>();
            float fov = cam != null ? cam.fieldOfView : verticalFov;
            float height = 2f * distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float width = height * clip.width / clip.height;
            Vector3 scale = quad.localScale;
            quad.localScale = new Vector3(
                Mathf.Sign(scale.x) * width,
                Mathf.Sign(scale.y) * height,
                scale.z
            );
        }

        feedTexture = new RenderTexture(clip.width, clip.height, 0, RenderTextureFormat.ARGB32)
        {
            name = "Recorded Feed",
        };
        feedTexture.Create();

        feedRenderer = quad.GetComponent<Renderer>();
        if (feedRenderer != null)
        {
            feedProperties = new MaterialPropertyBlock();
            feedRenderer.GetPropertyBlock(feedProperties);
            feedProperties.SetTexture("_MainTex", feedTexture);
            feedProperties.SetTexture("_BaseMap", feedTexture);
            feedRenderer.SetPropertyBlock(feedProperties);
        }
    }

    private void SetUpVideo(string videoPath)
    {
        videoTexture = new RenderTexture(clip.width, clip.height, 0, RenderTextureFormat.ARGB32)
        {
            name = "Recorded Video",
        };
        videoTexture.Create();

        videoPlayer = gameObject.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = videoPath;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoTexture;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.isLooping = loop;
        videoPlayer.playbackSpeed = playbackSpeed;
        videoPlayer.skipOnDrop = true;
        videoPlayer.sendFrameReadyEvents = true;
        videoPlayer.frameReady += OnFrameReady;
        videoPlayer.errorReceived += (_, message) => Fail($"video error: {message}");
        videoPlayer.prepareCompleted += source =>
        {
            if (startTime > 0f)
                source.time = startTime;
            source.Play();
            clipStatus = "Playing";
        };
        clipStatus = "Preparing video";
        videoPlayer.Prepare();
    }

    #endregion

    #region Playback

    private void OnValidate()
    {
        if (videoPlayer != null)
        {
            videoPlayer.isLooping = loop;
            videoPlayer.playbackSpeed = playbackSpeed;
        }
    }

    private void OnFrameReady(VideoPlayer source, long frameIndex)
    {
        // The Kinect color buffer starts at the image's top row and the feed quad is set up for
        // that, so flip the decoded frame vertically.
        if (feedTexture != null)
            Graphics.Blit(videoTexture, feedTexture, new Vector2(1f, -1f), new Vector2(0f, 1f));
        ColorFrameCount++;

        int index = (int)frameIndex;
        if (index >= 0 && index < clip.frames.Count && index != currentFrame)
        {
            // Back to the start of a loop: the clip's people start over too.
            if (index < currentFrame)
                System.Array.Clear(curlStates, 0, curlStates.Length);
            currentFrame = index;
            ApplyFrame(clip.frames[index]);
            BodyFrameCount++;
        }
    }

    private void ApplyFrame(FrameData frame)
    {
        foreach (var body in bodies)
            body.Clear();
        if (frame.bodies == null)
            return;

        foreach (var data in frame.bodies)
        {
            if (data.joints == null || !bodySlotById.TryGetValue(data.id, out int slot))
                continue;

            TrackedBody body = bodies[slot];
            body.TrackingId = (ulong)(data.id + 1); // 0 means "no body" in Kinect terms
            body.IsTracked = true;

            foreach (var pair in data.joints)
            {
                if (!System.Enum.TryParse(pair.Key, out JointType type))
                    continue;
                body.Joints[type] = new Joint
                {
                    JointType = type,
                    Position = ToCameraPoint(pair.Value),
                    TrackingState =
                        pair.Value.c >= minJointConfidence
                            ? TrackingState.Tracked
                            : TrackingState.Inferred,
                };
            }

            ReadHand(data.handLeft, slot * 2, out body.HandLeftState, out body.HandLeftConfidence);
            ReadHand(
                data.handRight,
                slot * 2 + 1,
                out body.HandRightState,
                out body.HandRightConfidence
            );
        }
    }

    private void ReadHand(
        HandData hand,
        int curlSlot,
        out HandState state,
        out TrackingConfidence confidence
    )
    {
        if (hand == null)
        {
            state = HandState.NotTracked;
            confidence = TrackingConfidence.Low;
            return;
        }

        // A held reading is the last state carried over a frame where the hand wasn't found.
        confidence = hand.held ? TrackingConfidence.Low : TrackingConfidence.High;

        if (handStateMode == HandStateMode.Recorded)
        {
            state = ParseHandState(hand.state);
            return;
        }

        HandState previous = curlStates[curlSlot];
        if (hand.curl == null)
            state = previous == HandState.Unknown ? HandState.NotTracked : previous;
        else if (hand.curl.Value > closedCurlAbove)
            state = HandState.Closed;
        else if (hand.curl.Value < openCurlBelow || previous != HandState.Closed)
            state = HandState.Open;
        else
            state = HandState.Closed;

        if (state == HandState.Open || state == HandState.Closed)
            curlStates[curlSlot] = state;
    }

    private static HandState ParseHandState(string state)
    {
        return state switch
        {
            "Open" => HandState.Open,
            "Closed" => HandState.Closed,
            "Lasso" => HandState.Lasso,
            _ => HandState.Unknown,
        };
    }

    private static CameraSpacePoint ToCameraPoint(JointData joint)
    {
        return new CameraSpacePoint
        {
            X = joint.x,
            Y = joint.y,
            Z = joint.z,
        };
    }

    #endregion

    private void Fail(string message)
    {
        clipStatus = "Error: " + message;
        Debug.LogError("RecordedBodySource: " + message);
    }

    private static void ReleaseTexture(ref RenderTexture texture)
    {
        if (texture == null)
            return;
        texture.Release();
        Destroy(texture);
        texture = null;
    }

    #region File format (<n>_bodies3d.json)

#pragma warning disable 0649 // assigned by JsonConvert
    private class ClipFile
    {
        public string source;
        public float fps;
        public int width;
        public int height;
        public string space;
        public Intrinsics intrinsics;
        public List<FrameData> frames;
    }

    private class Intrinsics
    {
        public float fx;
        public float fy;
        public float cx;
        public float cy;
    }

    private class FrameData
    {
        public int i;
        public float t;
        public List<BodyData> bodies;
    }

    private class BodyData
    {
        public int id;
        public Dictionary<string, JointData> joints;
        public HandData handLeft;
        public HandData handRight;
    }

    private class JointData
    {
        public float x;
        public float y;
        public float z;
        public float? u;
        public float? v;
        public float c = 1f;
    }

    private class HandData
    {
        public float? curl;
        public float conf;
        public string state;
        public bool held;
    }
#pragma warning restore 0649

    #endregion
}
