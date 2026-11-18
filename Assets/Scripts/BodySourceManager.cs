using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;
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

    // Occluder hole fill scratch (see FillOccluderHolesJob), one entry per depth pixel;
    // allocated on first use, disposed in OnDestroy.
    private NativeArray<int> _holeStack;
    private NativeArray<int> _holePixels;
    private NativeArray<byte> _holeSeen;

    // Body index value for "no tracked person" (0-5 are players).
    private const byte NoBody = 255;

    // The fill runs on a worker thread: scheduled in Update as soon as the depth frame is in,
    // completed in LateUpdate (before rendering). These mark the main thread's share.
    private static readonly ProfilerMarker ScheduleFillMarker = new(
        "BodySourceManager.FillOccluderHoles.Schedule"
    );
    private static readonly ProfilerMarker CompleteFillMarker = new(
        "BodySourceManager.FillOccluderHoles.Complete"
    );
    private JobHandle _fillHandle;
    private bool _fillPending;

    // Aligned in color space, the depth-to-color map places the occluder, so it's built from
    // the filled depth once the fill completes.
    private bool _mapFromFilledPending;

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

    // What BodyDepthOccluder draws: depth and body index with holes on tracked people filled
    // (see FillOccluderHoles). Updated only while the occluder draws.
    public Texture2D OccluderDepthTexture { get; private set; }
    public Texture2D OccluderBodyIndexTexture { get; private set; }
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
            OccluderDepthTexture = CreateDepthSpaceTexture(
                "Kinect Occluder Depth",
                TextureFormat.R16
            );
            OccluderBodyIndexTexture = CreateDepthSpaceTexture(
                "Kinect Occluder Body Index",
                TextureFormat.R8
            );
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

        CompleteOccluderFill();

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

            bool occluderFilled = OccluderDraws;
            if (occluderFilled)
            {
                using (ScheduleFillMarker.Auto())
                {
                    // Fill in place, in the occluder textures' own memory, on a worker thread.
                    var occluderDepth = OccluderDepthTexture.GetRawTextureData<ushort>();
                    var occluderBodyIndex = OccluderBodyIndexTexture.GetRawTextureData<byte>();
                    occluderDepth.CopyFrom(depthData);
                    occluderBodyIndex.CopyFrom(bodyIndexData);
                    EnsureHoleScratch(depthData.Length);
                    _fillHandle = new FillOccluderHolesJob
                    {
                        depth = occluderDepth,
                        bodyIndex = occluderBodyIndex,
                        stack = _holeStack,
                        pixels = _holePixels,
                        seen = _holeSeen,
                        width = DepthWidth,
                    }.Schedule();
                    JobHandle.ScheduleBatchedJobs();
                    _fillPending = true;
                }
            }

            if (NeedsDepthToColor(backdrop))
            {
                // Aligned in color space, the map only places the occluder, so it's built from
                // the filled depth, after the fill (CompleteOccluderFill). Otherwise from the
                // real depth: the Infrared + Color tint needs its holes (they stay untinted).
                if (occluderFilled && backdrop.Space() == FeedSpace.Color)
                {
                    _mapFromFilledPending = true;
                }
                else
                {
                    var mapData = DepthToColorTexture.GetRawTextureData<Vector2>();
                    MapDepthToColor(depthPtr, (int)depthBytes, mapData);
                }
            }

            DepthTexture.Apply(false);
            BodyIndexTexture.Apply(false);
        }

        _depthFrameCount++;
        DepthFramesReady = true;
    }

    void LateUpdate()
    {
        // A fill still running is left for the next rendered frame instead of waited for:
        // depth arrives at 30 fps, so the occluder is at most one rendered frame late and the
        // main thread never waits. Except with Sync Particles To Feed on: the particle layer
        // only redraws on this frame (a new depth frame), so it waits to use this frame's fill.
        if (_fillPending && !_fillHandle.IsCompleted && !ParticlesSyncedToDepth)
            return;
        CompleteOccluderFill();
    }

    /// <summary>The particle layer only redraws on new depth-camera frames.</summary>
    private bool ParticlesSyncedToDepth
    {
        get
        {
            var settings = controller.GetRuntimeSettings();
            return settings.syncParticlesToFeed
                && settings.feedBackdrop.IsShown()
                && settings.feedBackdrop.Space() == FeedSpace.Depth;
        }
    }

    /// <summary>
    /// Completes the occluder fill scheduled in Update and uploads its textures. Called from
    /// LateUpdate (before rendering) once the job is done, and at the start of the next depth
    /// frame, which must not overwrite the textures while the job still writes them.
    /// </summary>
    private void CompleteOccluderFill()
    {
        if (!_fillPending)
            return;

        using (CompleteFillMarker.Auto())
        {
            _fillHandle.Complete();
            _fillPending = false;
            OccluderDepthTexture.Apply(false);
            OccluderBodyIndexTexture.Apply(false);

            if (_mapFromFilledPending)
            {
                _mapFromFilledPending = false;
                var occluderDepth = OccluderDepthTexture.GetRawTextureData<ushort>();
                MapDepthToColor(
                    BufferPointer(occluderDepth),
                    occluderDepth.Length * sizeof(ushort),
                    DepthToColorTexture.GetRawTextureData<Vector2>()
                );
            }
        }
    }

    private void MapDepthToColor(IntPtr depth, int depthBytes, NativeArray<Vector2> map)
    {
        // One ColorSpacePoint (two floats) per RGFloat texel.
        _sensor.CoordinateMapper.MapDepthFrameToColorSpaceUsingIntPtr(
            depth,
            depthBytes,
            BufferPointer(map),
            (uint)map.Length
        );
        DepthToColorTexture.Apply(false);
    }

    /// <summary>The body occluder draws: Body Occlusion on, or the point cloud debug view.</summary>
    private bool OccluderDraws
    {
        get
        {
            var settings = controller.GetRuntimeSettings();
            return settings.bodyOcclusion || settings.showPointCloud;
        }
    }

    private void EnsureHoleScratch(int length)
    {
        if (_holeStack.IsCreated && _holeStack.Length == length)
            return;
        DisposeHoleScratch();
        _holeStack = new NativeArray<int>(length, Allocator.Persistent);
        _holePixels = new NativeArray<int>(length, Allocator.Persistent);
        _holeSeen = new NativeArray<byte>(length, Allocator.Persistent);
    }

    private void DisposeHoleScratch()
    {
        if (_holeStack.IsCreated)
            _holeStack.Dispose();
        if (_holePixels.IsCreated)
            _holePixels.Dispose();
        if (_holeSeen.IsCreated)
            _holeSeen.Dispose();
    }

    /// <summary>
    /// Fills holes in a tracked person's depth so the occluder still hides particles behind
    /// them. A hole is a connected area without a depth reading: a face too close or so bright
    /// the sensor saturated, IR-absorbing fabric. Only a hole whose border is mostly a tracked
    /// person is filled (windows, out-of-range walls and the like stay open, rather than
    /// becoming invisible walls). It gets the nearest depth on that person's part of its border
    /// (the part of the person the hole is on is nearer than its edges, if anything, and an
    /// occluder slightly in front hides less wrongly than one behind) and that border pixel's
    /// body index, so Bodies Only still counts it.
    ///
    /// Burst-compiled, on a worker thread (see CompleteOccluderFill), in place on the occluder
    /// textures. Only holes touching a tracked person
    /// are traced (others can't pass the guard), and a frame without one does nothing, so big
    /// out-of-range backgrounds cost nothing unless someone stands in front of them.
    /// </summary>
    [BurstCompile]
    private struct FillOccluderHolesJob : IJob
    {
        public NativeArray<ushort> depth;
        public NativeArray<byte> bodyIndex;
        public NativeArray<int> stack;
        public NativeArray<int> pixels;
        public NativeArray<byte> seen;
        public int width;

        private static readonly ProfilerMarker JobMarker = new(
            "BodySourceManager.FillOccluderHolesJob"
        );

        public void Execute()
        {
            using (JobMarker.Auto())
                Fill();
        }

        private void Fill()
        {
            int n = depth.Length;

            bool anyPerson = false;
            for (int i = 0; i < n && !anyPerson; i++)
                anyPerson = bodyIndex[i] != NoBody;
            if (!anyPerson)
                return;

            for (int i = 0; i < n; i++)
                seen[i] = 0;

            for (int start = 0; start < n; start++)
            {
                if (depth[start] != 0 || seen[start] != 0 || !TouchesPerson(start))
                    continue;

                // Flood the hole (4-connected). Pixels are marked when pushed, so each is pushed
                // once. Two holes are never adjacent, so every border pixel has a reading. A
                // border pixel touching the hole on two sides counts twice, which only weights
                // the vote.
                int count = 0;
                int top = 0;
                int total = 0;
                int person = 0;
                int nearestDepth = int.MaxValue;
                byte nearestBody = NoBody;
                stack[top++] = start;
                seen[start] = 1;
                while (top > 0)
                {
                    int i = stack[--top];
                    pixels[count++] = i;
                    int x = i % width;
                    if (x > 0)
                        Visit(
                            i - 1,
                            ref top,
                            ref total,
                            ref person,
                            ref nearestDepth,
                            ref nearestBody
                        );
                    if (x < width - 1)
                        Visit(
                            i + 1,
                            ref top,
                            ref total,
                            ref person,
                            ref nearestDepth,
                            ref nearestBody
                        );
                    if (i >= width)
                        Visit(
                            i - width,
                            ref top,
                            ref total,
                            ref person,
                            ref nearestDepth,
                            ref nearestBody
                        );
                    if (i + width < n)
                        Visit(
                            i + width,
                            ref top,
                            ref total,
                            ref person,
                            ref nearestDepth,
                            ref nearestBody
                        );
                }

                // Mostly a person around it (at least half its border): fill.
                if (person == 0 || person * 2 < total)
                    continue;
                for (int k = 0; k < count; k++)
                {
                    depth[pixels[k]] = (ushort)nearestDepth;
                    bodyIndex[pixels[k]] = nearestBody;
                }
            }
        }

        private bool TouchesPerson(int i)
        {
            int x = i % width;
            return (x > 0 && IsPerson(i - 1))
                || (x < width - 1 && IsPerson(i + 1))
                || (i >= width && IsPerson(i - width))
                || (i + width < depth.Length && IsPerson(i + width));
        }

        private bool IsPerson(int j) => depth[j] != 0 && bodyIndex[j] != NoBody;

        private void Visit(
            int j,
            ref int top,
            ref int total,
            ref int person,
            ref int nearestDepth,
            ref byte nearestBody
        )
        {
            ushort d = depth[j];
            if (d != 0)
            {
                total++;
                byte body = bodyIndex[j];
                if (body != NoBody)
                {
                    person++;
                    if (d < nearestDepth)
                    {
                        nearestDepth = d;
                        nearestBody = body;
                    }
                }
            }
            else if (seen[j] == 0)
            {
                seen[j] = 1;
                stack[top++] = j;
            }
        }
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
        // A fill still running would write into textures and scratch freed below.
        _fillHandle.Complete();
        _fillPending = false;
        DestroyTexture(ColorTexture);
        DestroyTexture(InfraredTexture);
        DestroyTexture(DepthTexture);
        DestroyTexture(BodyIndexTexture);
        DestroyTexture(DepthToColorTexture);
        DestroyTexture(OccluderDepthTexture);
        DestroyTexture(OccluderBodyIndexTexture);
        DisposeHoleScratch();
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (texture != null)
            Destroy(texture);
    }
}
