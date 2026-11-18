using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Windows.Kinect;

/// <summary>
/// Opens the Kinect and turns its streams into GPU textures for the backdrop
/// (<see cref="KinectFeedDisplay"/>) and the body occluder (<see cref="BodyDepthOccluder"/>),
/// plus the body data the SceneController reads.
///
/// Depth, infrared and body index come from one MultiSourceFrameReader, so all three are from
/// the same capture. Bodies have their own reader so they aren't held back waiting for the
/// others. Every frame is copied straight into its texture's memory, with no managed buffers in
/// between. Work the current backdrop doesn't need is skipped: the color reader is paused when
/// the backdrop doesn't use color, infrared is only copied for the infrared backdrops, and the
/// depth-to-color map is only computed when something is aligned in color space.
/// </summary>
[RequireComponent(typeof(SceneController))]
public class BodySourceManager : MonoBehaviour
{
    SceneController controller;

    [Tooltip("Enable verbose logging to the console.")]
    public bool EnableVerboseLogging = true;

    private KinectSensor _sensor;
    private ColorFrameReader _colorReader;
    private BodyFrameReader _bodyReader;
    private MultiSourceFrameReader _depthReader;

    private Body[] _bodyData = null;

    private int _colorFrameCount = 0;
    private int _bodyFrameCount = 0;
    private int _depthFrameCount = 0;
    private bool _loggedColorConversion;

    /// <summary>
    /// The color camera image, still YUY2: each RGBA32 texel is two pixels (Y0, U, Y1, V), so
    /// it's half the color width. Decoded in the KinectFeed shader, which saves the CPU
    /// conversion and halves the upload. Linear (no sRGB decode), point filtered.
    /// </summary>
    public Texture2D ColorTexture { get; private set; }

    /// <summary>Infrared intensity (R16), same pixels as the depth image.</summary>
    public Texture2D InfraredTexture { get; private set; }

    // GPU-side copies of the depth-camera frames, consumed by BodyDepthOccluder and the feed.
    // DepthToColorTexture holds, per depth pixel, its color-image pixel coordinates
    // (CoordinateMapper.MapDepthFrameToColorSpace) — invalid pixels are -Infinity. It's only
    // updated while needed (see NeedsDepthToColor).
    public Texture2D DepthTexture { get; private set; }
    public Texture2D BodyIndexTexture { get; private set; }
    public Texture2D DepthToColorTexture { get; private set; }
    public int DepthWidth { get; private set; } = 512;
    public int DepthHeight { get; private set; } = 424;
    public int ColorWidth { get; private set; } = 1920;
    public int ColorHeight { get; private set; } = 1080;
    public bool DepthFramesReady { get; private set; }

    // Color frames received so far. They arrive during Update (the Kinect event pump), and only
    // while the backdrop uses color.
    public int ColorFrameCount => _colorFrameCount;

    // Depth / infrared / body index frames received so far (one capture each, 30 per second).
    public int DepthFrameCount => _depthFrameCount;

    // Body frames received so far (acquired in Update, 30 per second). Lets FixedUpdate consumers
    // tell a new frame from the same one seen on a second physics step.
    public int BodyFrameCount => _bodyFrameCount;

    public CoordinateMapper Mapper => _sensor != null ? _sensor.CoordinateMapper : null;

    public Body[] GetData()
    {
        return _bodyData;
    }

    /// <summary>Frames received so far by the camera a backdrop is drawn from.</summary>
    public int FeedFrameCount(FeedBackdrop backdrop) =>
        backdrop == FeedBackdrop.Color ? _colorFrameCount : _depthFrameCount;

    /// <summary>Pixel size of the image shown in the given feed space.</summary>
    public Vector2Int FeedSize(FeedSpace space) =>
        space == FeedSpace.Color
            ? new Vector2Int(ColorWidth, ColorHeight)
            : new Vector2Int(DepthWidth, DepthHeight);

    /// <summary>
    /// Maps a camera-space point to its normalized position in the feed image (0..1, top-left
    /// origin, the same layout as the feed textures). False when the mapper is unavailable or
    /// the point doesn't map.
    /// </summary>
    public bool TryMapCameraPointToFeed(CameraSpacePoint point, FeedSpace space, out Vector2 uv)
    {
        uv = default;
        var mapper = Mapper;
        if (mapper == null)
            return false;

        float x,
            y;
        if (space == FeedSpace.Color)
        {
            ColorSpacePoint p = mapper.MapCameraPointToColorSpace(point);
            x = p.X / ColorWidth;
            y = p.Y / ColorHeight;
        }
        else
        {
            // Depth-space coordinates put pixel centers on integers; the half pixel matches
            // the texel centers the feed and the occluder use.
            DepthSpacePoint p = mapper.MapCameraPointToDepthSpace(point);
            x = (p.X + 0.5f) / DepthWidth;
            y = (p.Y + 0.5f) / DepthHeight;
        }

        if (!float.IsFinite(x) || !float.IsFinite(y))
            return false;

        uv = new Vector2(x, y);
        return true;
    }

    void Awake()
    {
        controller = GetComponent<SceneController>();
    }

    void Start()
    {
        if (controller.dummyOnlyMode)
        {
            return;
        }

        if (EnableVerboseLogging)
            Debug.Log("BodySourceManager: Starting up...");

        _sensor = KinectSensor.GetDefault();

        if (_sensor == null)
        {
            Debug.LogError("BodySourceManager: No Kinect Sensor found!");
            return;
        }

        if (EnableVerboseLogging)
            Debug.Log("BodySourceManager: Sensor found.");

        // --- Color stream (paused while the backdrop doesn't use it) ---
        _colorReader = _sensor.ColorFrameSource.OpenReader();
        if (_colorReader != null)
        {
            var colorDesc = _sensor.ColorFrameSource.FrameDescription;
            ColorWidth = colorDesc.Width;
            ColorHeight = colorDesc.Height;
            ColorTexture = new Texture2D(
                ColorWidth / 2,
                ColorHeight,
                TextureFormat.RGBA32,
                false,
                true
            )
            {
                name = "Kinect Color (YUY2)",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _colorReader.FrameArrived += Reader_ColorFrameArrived;

            if (EnableVerboseLogging)
                Debug.Log("BodySourceManager: ColorFrameReader initialized and subscribed.");
        }
        else
        {
            Debug.LogError("BodySourceManager: Failed to open ColorFrameReader.");
        }

        // --- Depth + Infrared + BodyIndex, one capture per frame ---
        _depthReader = _sensor.OpenMultiSourceFrameReader(
            FrameSourceTypes.Depth | FrameSourceTypes.Infrared | FrameSourceTypes.BodyIndex
        );
        if (_depthReader != null)
        {
            var depthDesc = _sensor.DepthFrameSource.FrameDescription;
            DepthWidth = depthDesc.Width;
            DepthHeight = depthDesc.Height;

            DepthTexture = CreateDepthSpaceTexture("Kinect Depth", TextureFormat.R16);
            BodyIndexTexture = CreateDepthSpaceTexture("Kinect Body Index", TextureFormat.R8);
            DepthToColorTexture = CreateDepthSpaceTexture(
                "Kinect Depth To Color",
                TextureFormat.RGFloat
            );
            // Unmapped until the first map is computed (it isn't while aligned in depth space),
            // so switching to color space never places pixels from an empty map.
            var unmapped = DepthToColorTexture.GetRawTextureData<Vector2>();
            for (int i = 0; i < unmapped.Length; i++)
                unmapped[i] = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            DepthToColorTexture.Apply(false);
            InfraredTexture = CreateDepthSpaceTexture("Kinect Infrared", TextureFormat.R16);
            // The backdrop is upscaled about 2x, so the infrared image is filtered.
            InfraredTexture.filterMode = FilterMode.Bilinear;

            if (EnableVerboseLogging)
                Debug.Log(
                    $"BodySourceManager: Depth/Infrared/BodyIndex reader initialized ({DepthWidth}x{DepthHeight})."
                );
        }
        else
        {
            Debug.LogError(
                "BodySourceManager: Failed to open the depth/infrared/body index reader."
            );
        }

        // --- Body stream ---
        _bodyReader = _sensor.BodyFrameSource.OpenReader();
        if (_bodyReader != null)
        {
            if (_bodyData == null)
            {
                _bodyData = new Body[_sensor.BodyFrameSource.BodyCount];
            }

            if (EnableVerboseLogging)
                Debug.Log("BodySourceManager: BodyFrameReader initialized.");
        }
        else
        {
            Debug.LogError("BodySourceManager: Failed to open BodyFrameReader.");
        }

        UpdateColorReaderPause();

        if (!_sensor.IsOpen)
        {
            _sensor.Open();
            if (EnableVerboseLogging)
                Debug.Log("BodySourceManager: Sensor opened.");
        }
    }

    private Texture2D CreateDepthSpaceTexture(string name, TextureFormat format) =>
        new(DepthWidth, DepthHeight, format, false, true)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

    void Update()
    {
        if (controller.dummyOnlyMode)
        {
            return;
        }

        UpdateColorReaderPause();

        if (_bodyReader != null)
        {
            using (var frame = _bodyReader.AcquireLatestFrame())
            {
                if (frame != null)
                {
                    _bodyFrameCount++;
                    if (EnableVerboseLogging && _bodyFrameCount % 100 == 0)
                    {
                        Debug.Log($"BodySourceManager: Acquired body frame #{_bodyFrameCount}");
                    }
                    frame.GetAndRefreshBodyData(_bodyData);
                }
            }
        }

        UpdateDepthFrames();
    }

    private FeedBackdrop ActiveBackdrop
    {
        get
        {
            // Without a feed quad nothing shows a backdrop, so no stream is needed for one.
            if (!controller.HasFeature(SceneController.SceneFeature.CameraFeed))
                return FeedBackdrop.None;
            return controller.GetRuntimeSettings().feedBackdrop;
        }
    }

    /// <summary>
    /// The depth-to-color map places the occluder when the backdrop is aligned in color space,
    /// and tints the infrared image for Infrared + Color.
    /// </summary>
    private bool NeedsDepthToColor(FeedBackdrop backdrop) =>
        controller.HasFeature(SceneController.SceneFeature.CameraFeed)
        && (backdrop.Space() == FeedSpace.Color || backdrop == FeedBackdrop.InfraredColor);

    private void UpdateColorReaderPause()
    {
        if (_colorReader == null)
            return;
        bool paused = !ActiveBackdrop.UsesColorStream();
        if (_colorReader.IsPaused != paused)
            _colorReader.IsPaused = paused;
    }

    private void UpdateDepthFrames()
    {
        if (_depthReader == null)
        {
            return;
        }

        var multiFrame = _depthReader.AcquireLatestFrame();
        if (multiFrame == null)
        {
            return;
        }

        var backdrop = ActiveBackdrop;
        using (var depthFrame = multiFrame.DepthFrameReference.AcquireFrame())
        using (var bodyIndexFrame = multiFrame.BodyIndexFrameReference.AcquireFrame())
        using (
            var infraredFrame = backdrop.UsesInfrared()
                ? multiFrame.InfraredFrameReference.AcquireFrame()
                : null
        )
        {
            // A sub-frame is null when the capture expired before it was acquired; skip the
            // whole capture so the textures never mix two of them.
            if (
                depthFrame == null
                || bodyIndexFrame == null
                || (backdrop.UsesInfrared() && infraredFrame == null)
            )
            {
                return;
            }

            var depthData = DepthTexture.GetRawTextureData<ushort>();
            IntPtr depthPtr = BufferPointer(depthData);
            uint depthBytes = (uint)(depthData.Length * sizeof(ushort));
            depthFrame.CopyFrameDataToIntPtr(depthPtr, depthBytes);

            var bodyIndexData = BodyIndexTexture.GetRawTextureData<byte>();
            bodyIndexFrame.CopyFrameDataToIntPtr(
                BufferPointer(bodyIndexData),
                (uint)bodyIndexData.Length
            );

            if (infraredFrame != null)
            {
                var infraredData = InfraredTexture.GetRawTextureData<ushort>();
                infraredFrame.CopyFrameDataToIntPtr(
                    BufferPointer(infraredData),
                    (uint)(infraredData.Length * sizeof(ushort))
                );
                InfraredTexture.Apply(false);
            }

            if (NeedsDepthToColor(backdrop))
            {
                // One ColorSpacePoint (two floats) per RGFloat texel.
                var mapData = DepthToColorTexture.GetRawTextureData<Vector2>();
                _sensor.CoordinateMapper.MapDepthFrameToColorSpaceUsingIntPtr(
                    depthPtr,
                    (int)depthBytes,
                    BufferPointer(mapData),
                    (uint)mapData.Length
                );
                DepthToColorTexture.Apply(false);
            }

            DepthTexture.Apply(false);
            BodyIndexTexture.Apply(false);
        }

        _depthFrameCount++;
        DepthFramesReady = true;
    }

    private static unsafe IntPtr BufferPointer<T>(Unity.Collections.NativeArray<T> data)
        where T : struct =>
        (IntPtr)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(data);

    private void Reader_ColorFrameArrived(object sender, ColorFrameArrivedEventArgs e)
    {
        using (var frame = e.FrameReference.AcquireFrame())
        {
            if (frame == null)
            {
                return;
            }

            _colorFrameCount++;
            if (EnableVerboseLogging && _colorFrameCount % 100 == 0)
            {
                Debug.Log($"BodySourceManager: Acquired color frame #{_colorFrameCount}");
            }

            var data = ColorTexture.GetRawTextureData<byte>();
            IntPtr ptr = BufferPointer(data);
            if (frame.RawColorImageFormat == ColorImageFormat.Yuy2)
            {
                // The sensor's own format: a straight copy, no conversion.
                frame.CopyRawFrameDataToIntPtr(ptr, (uint)data.Length);
            }
            else
            {
                if (!_loggedColorConversion)
                {
                    _loggedColorConversion = true;
                    Debug.LogWarning(
                        $"BodySourceManager: color frames arrive as {frame.RawColorImageFormat}, "
                            + "converting them to YUY2 on the CPU."
                    );
                }
                frame.CopyConvertedFrameDataToIntPtr(ptr, (uint)data.Length, ColorImageFormat.Yuy2);
            }
            ColorTexture.Apply(false);
        }
    }

    void OnApplicationQuit()
    {
        if (EnableVerboseLogging)
            Debug.Log("BodySourceManager: Shutting down...");

        if (_colorReader != null)
        {
            _colorReader.Dispose();
            _colorReader = null;
        }

        if (_bodyReader != null)
        {
            _bodyReader.Dispose();
            _bodyReader = null;
        }

        if (_depthReader != null)
        {
            _depthReader.Dispose();
            _depthReader = null;
        }

        if (_sensor != null)
        {
            if (_sensor.IsOpen)
            {
                _sensor.Close();
            }
            _sensor = null;
        }

        if (EnableVerboseLogging)
            Debug.Log("BodySourceManager: Sensor and Readers disposed.");
    }

    void OnDestroy()
    {
        DestroyTexture(ColorTexture);
        DestroyTexture(InfraredTexture);
        DestroyTexture(DepthTexture);
        DestroyTexture(BodyIndexTexture);
        DestroyTexture(DepthToColorTexture);
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (texture != null)
            Destroy(texture);
    }
}
