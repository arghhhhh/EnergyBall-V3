using UnityEngine;
using Windows.Kinect;

[RequireComponent(typeof(SceneController))]
public class BodySourceManager : MonoBehaviour
{
    SceneController controller;

    [Tooltip("Enable verbose logging to the console.")]
    public bool EnableVerboseLogging = true;

    [Header("Rendering Targets")]
    // The final RenderTexture that will hold the flipped image. Assign this in the Inspector.
    public RenderTexture ColorTexture;

    // The material on your quad. It should use the ColorTexture. Assign this in the Inspector.
    public Material ColorMaterial;

    private KinectSensor _sensor;
    private ColorFrameReader _colorReader;
    private BodyFrameReader _bodyReader;
    private DepthFrameReader _depthReader;
    private BodyIndexFrameReader _bodyIndexReader;

    // This Texture2D will serve as our intermediate buffer for the raw Kinect data.
    private Texture2D _colorTexture2D;
    private byte[] _colorData;
    private Body[] _bodyData = null;

    private ushort[] _depthData;
    private byte[] _bodyIndexData;
    private ColorSpacePoint[] _depthToColorPoints;

    private int _colorFrameCount = 0;
    private int _bodyFrameCount = 0;

    // GPU-side copies of the depth-camera frames, consumed by BodyDepthOccluder.
    // DepthToColorTexture holds, per depth pixel, its color-image pixel coordinates
    // (CoordinateMapper.MapDepthFrameToColorSpace) — invalid pixels are -Infinity.
    public Texture2D DepthTexture { get; private set; }
    public Texture2D BodyIndexTexture { get; private set; }
    public Texture2D DepthToColorTexture { get; private set; }
    public int DepthWidth { get; private set; }
    public int DepthHeight { get; private set; }
    public int ColorWidth { get; private set; } = 1920;
    public int ColorHeight { get; private set; } = 1080;
    public bool DepthFramesReady { get; private set; }

    public CoordinateMapper Mapper => _sensor != null ? _sensor.CoordinateMapper : null;

    public Body[] GetData()
    {
        return _bodyData;
    }

    void Awake()
    {
        controller = GetComponent<SceneController>();
    }

    void Start()
    {
        var runtimeSettings = controller.GetRuntimeSettings();
        if (runtimeSettings.dummyOnlyMode)
        {
            return;
        }

        if (EnableVerboseLogging)
            Debug.Log("BodySourceManager: Starting up...");

        _sensor = KinectSensor.GetDefault();

        if (_sensor != null)
        {
            if (EnableVerboseLogging)
                Debug.Log("BodySourceManager: Sensor found.");

            // --- Initialize Color Stream ---
            _colorReader = _sensor.ColorFrameSource.OpenReader();
            if (_colorReader != null)
            {
                var frameDesc = _sensor.ColorFrameSource.CreateFrameDescription(
                    ColorImageFormat.Bgra
                );

                // Initialize our intermediate Texture2D and data buffer
                _colorTexture2D = new Texture2D(
                    frameDesc.Width,
                    frameDesc.Height,
                    TextureFormat.BGRA32,
                    false
                );
                _colorData = new byte[frameDesc.BytesPerPixel * frameDesc.LengthInPixels];

                _colorReader.FrameArrived += Reader_ColorFrameArrived;

                if (EnableVerboseLogging)
                    Debug.Log("BodySourceManager: ColorFrameReader initialized and subscribed.");

                if (ColorMaterial != null && ColorTexture != null)
                {
                    ColorMaterial.mainTexture = ColorTexture;
                }
            }
            else
            {
                Debug.LogError("BodySourceManager: Failed to open ColorFrameReader.");
            }

            // --- Initialize Depth + BodyIndex Streams (for body occlusion) ---
            _depthReader = _sensor.DepthFrameSource.OpenReader();
            _bodyIndexReader = _sensor.BodyIndexFrameSource.OpenReader();
            if (_depthReader != null && _bodyIndexReader != null)
            {
                var depthDesc = _sensor.DepthFrameSource.FrameDescription;
                DepthWidth = depthDesc.Width;
                DepthHeight = depthDesc.Height;
                var colorDesc = _sensor.ColorFrameSource.CreateFrameDescription(
                    ColorImageFormat.Bgra
                );
                ColorWidth = colorDesc.Width;
                ColorHeight = colorDesc.Height;

                int depthLen = DepthWidth * DepthHeight;
                _depthData = new ushort[depthLen];
                _bodyIndexData = new byte[depthLen];
                _depthToColorPoints = new ColorSpacePoint[depthLen];

                DepthTexture = new Texture2D(DepthWidth, DepthHeight, TextureFormat.R16, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                BodyIndexTexture = new Texture2D(DepthWidth, DepthHeight, TextureFormat.R8, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                DepthToColorTexture = new Texture2D(
                    DepthWidth,
                    DepthHeight,
                    TextureFormat.RGFloat,
                    false
                )
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };

                if (EnableVerboseLogging)
                    Debug.Log(
                        $"BodySourceManager: Depth/BodyIndex readers initialized ({DepthWidth}x{DepthHeight})."
                    );
            }
            else
            {
                Debug.LogError("BodySourceManager: Failed to open Depth/BodyIndex readers.");
            }

            // --- Initialize Body Stream ---
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

            // --- Open Sensor ---
            if (!_sensor.IsOpen)
            {
                _sensor.Open();
                if (EnableVerboseLogging)
                    Debug.Log("BodySourceManager: Sensor opened.");
            }
        }
        else
        {
            Debug.LogError("BodySourceManager: No Kinect Sensor found!");
        }
    }

    void Update()
    {
        var runtimeSettings = controller.GetRuntimeSettings();
        if (runtimeSettings.dummyOnlyMode)
        {
            return;
        }

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

    private void UpdateDepthFrames()
    {
        if (_depthReader == null || _bodyIndexReader == null)
        {
            return;
        }

        bool gotDepth = false;
        using (var frame = _depthReader.AcquireLatestFrame())
        {
            if (frame != null)
            {
                frame.CopyFrameDataToArray(_depthData);
                gotDepth = true;
            }
        }

        bool gotBodyIndex = false;
        using (var frame = _bodyIndexReader.AcquireLatestFrame())
        {
            if (frame != null)
            {
                frame.CopyFrameDataToArray(_bodyIndexData);
                gotBodyIndex = true;
            }
        }

        if (gotDepth)
        {
            _sensor.CoordinateMapper.MapDepthFrameToColorSpace(_depthData, _depthToColorPoints);

            DepthTexture.SetPixelData(_depthData, 0);
            DepthTexture.Apply(false);
            DepthToColorTexture.SetPixelData(_depthToColorPoints, 0);
            DepthToColorTexture.Apply(false);
        }

        if (gotBodyIndex)
        {
            BodyIndexTexture.SetPixelData(_bodyIndexData, 0);
            BodyIndexTexture.Apply(false);
        }

        if (gotDepth && gotBodyIndex)
        {
            DepthFramesReady = true;
        }
    }

    private void Reader_ColorFrameArrived(object sender, ColorFrameArrivedEventArgs e)
    {
        using (var frame = e.FrameReference.AcquireFrame())
        {
            if (frame != null)
            {
                _colorFrameCount++;
                if (EnableVerboseLogging && _colorFrameCount % 100 == 0)
                {
                    Debug.Log($"BodySourceManager: Acquired color frame #{_colorFrameCount}");
                }

                frame.CopyConvertedFrameDataToArray(_colorData, ColorImageFormat.Bgra);
                _colorTexture2D.LoadRawTextureData(_colorData);
                _colorTexture2D.Apply();

                if (ColorTexture != null)
                {
                    Graphics.Blit(_colorTexture2D, ColorTexture);
                }
            }
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

        if (_bodyIndexReader != null)
        {
            _bodyIndexReader.Dispose();
            _bodyIndexReader = null;
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
}
