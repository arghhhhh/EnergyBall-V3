using UnityEngine;
using Windows.Kinect;

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

    // This Texture2D will serve as our intermediate buffer for the raw Kinect data.
    private Texture2D _colorTexture2D;
    private byte[] _colorData;
    private Body[] _bodyData = null;

    private int _colorFrameCount = 0;
    private int _bodyFrameCount = 0;

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
                    Graphics.Blit(
                        _colorTexture2D,
                        ColorTexture,
                        new Vector2(1, -1),
                        new Vector2(0, 0)
                    );
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
