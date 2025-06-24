using UnityEngine;
using Windows.Kinect;

public class ColorDataView : MonoBehaviour
{
    // The final RenderTexture that will hold the flipped image.
    // Assign this in the Inspector.
    public RenderTexture ColorTexture;

    // The material on your quad. It should use the ColorTexture.
    // Assign this in the Inspector.
    public Material ColorMaterial;

    private KinectSensor _Sensor;
    private ColorFrameReader _Reader;
    private byte[] _Data;
    private Texture2D _Texture;

    void Start()
    {
        _Sensor = KinectSensor.GetDefault();
        if (_Sensor != null)
        {
            // Open the sensor
            if (!_Sensor.IsOpen)
            {
                _Sensor.Open();
                Debug.Log("Kinect Sensor opened.");
            }

            var frameDesc = _Sensor.ColorFrameSource.FrameDescription;
            Debug.Log(
                $"Kinect Color Frame: {frameDesc.Width}x{frameDesc.Height} @ {frameDesc.BytesPerPixel} bpp"
            );

            // Setup the reader
            _Reader = _Sensor.ColorFrameSource.OpenReader();
            if (_Reader != null)
            {
                _Reader.FrameArrived += Reader_FrameArrived;
            }
            else
            {
                Debug.LogError("Failed to open ColorFrameReader.");
            }

            // Allocate buffers
            _Data = new byte[frameDesc.Width * frameDesc.Height * 4]; // BGRA is 4 bytes
            _Texture = new Texture2D(
                frameDesc.Width,
                frameDesc.Height,
                TextureFormat.BGRA32,
                false
            );

            // Ensure the material uses the RenderTexture
            if (ColorMaterial != null && ColorTexture != null)
            {
                ColorMaterial.mainTexture = ColorTexture;
            }
        }
        else
        {
            Debug.LogError("No Kinect Sensor found!");
        }
    }

    void Reader_FrameArrived(object sender, ColorFrameArrivedEventArgs args)
    {
        using (var frame = args.FrameReference.AcquireFrame())
        {
            if (frame != null)
            {
                // Copy frame data to our buffer
                frame.CopyConvertedFrameDataToArray(_Data, ColorImageFormat.Bgra);

                // Load the raw data into our intermediate texture
                _Texture.LoadRawTextureData(_Data);
                _Texture.Apply();

                // Blit from the intermediate texture to the final RenderTexture, flipping it horizontally.
                if (ColorTexture != null)
                {
                    // Correct scale and offset for horizontal flip
                    Graphics.Blit(_Texture, ColorTexture, new Vector2(1, -1), new Vector2(0, 1));
                }
            }
        }
    }

    void OnApplicationQuit()
    {
        if (_Reader != null)
        {
            _Reader.Dispose();
            _Reader = null;
        }

        if (_Sensor != null)
        {
            if (_Sensor.IsOpen)
            {
                _Sensor.Close();
            }
            _Sensor = null;
        }
    }
}
