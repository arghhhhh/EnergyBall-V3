using UnityEngine;
using Windows.Kinect;

public class KinectManager : MonoBehaviour
{
    // Public properties for Unity Inspector
    public Material ColorMaterial;
    public RenderTexture ColorTexture;

    // Kinect Sensor
    private KinectSensor _sensor;

    // Color Frame
    private ColorFrameReader _colorReader;
    private Texture2D _colorTexture;
    private byte[] _colorData;

    // Body Frame
    private BodyFrameReader _bodyReader;
    private Body[] _bodyData = null;

    // Public getter for the body data
    public Body[] GetData()
    {
        return _bodyData;
    }

    void Start()
    {
        _sensor = KinectSensor.GetDefault();

        if (_sensor != null)
        {
            // --- Initialize Color Stream ---
            _colorReader = _sensor.ColorFrameSource.OpenReader();
            if (_colorReader != null)
            {
                var frameDesc = _sensor.ColorFrameSource.CreateFrameDescription(
                    ColorImageFormat.Bgra
                );
                _colorTexture = new Texture2D(
                    frameDesc.Width,
                    frameDesc.Height,
                    TextureFormat.BGRA32,
                    false
                );
                _colorData = new byte[frameDesc.BytesPerPixel * frameDesc.LengthInPixels];
                _colorReader.FrameArrived += Reader_ColorFrameArrived;
                Debug.Log("ColorFrameReader initialized.");
            }
            else
            {
                Debug.LogError("Failed to open ColorFrameReader.");
            }

            // --- Initialize Body Stream ---
            _bodyReader = _sensor.BodyFrameSource.OpenReader();
            if (_bodyReader != null)
            {
                if (_bodyData == null)
                {
                    _bodyData = new Body[_sensor.BodyFrameSource.BodyCount];
                }
                Debug.Log("BodyFrameReader initialized.");
            }
            else
            {
                Debug.LogError("Failed to open BodyFrameReader.");
            }

            // --- Open Sensor ---
            if (!_sensor.IsOpen)
            {
                _sensor.Open();
                Debug.Log("Kinect Sensor opened.");
            }
        }
        else
        {
            Debug.LogError("No Kinect Sensor found!");
        }
    }

    void Update()
    {
        // --- Process Body Data ---
        if (_bodyReader != null)
        {
            var frame = _bodyReader.AcquireLatestFrame();
            if (frame != null)
            {
                frame.GetAndRefreshBodyData(_bodyData);
                frame.Dispose();
                frame = null;
            }
        }
    }

    private void Reader_ColorFrameArrived(object sender, ColorFrameArrivedEventArgs e)
    {
        // --- Process Color Data ---
        using (var frame = e.FrameReference.AcquireFrame())
        {
            if (frame != null)
            {
                frame.CopyConvertedFrameDataToArray(_colorData, ColorImageFormat.Bgra);
                _colorTexture.LoadRawTextureData(_colorData);
                _colorTexture.Apply();

                if (ColorMaterial != null)
                {
                    ColorMaterial.mainTexture = _colorTexture;
                }

                if (ColorTexture != null)
                {
                    // Apply a vertical flip
                    Graphics.Blit(
                        _colorTexture,
                        ColorTexture,
                        new Vector2(1, -1),
                        new Vector2(0, 1)
                    );
                }
            }
        }
    }

    void OnApplicationQuit()
    {
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
        Debug.Log("KinectManager: Application Quit - Sensor and Readers disposed.");
    }
}
