using UnityEngine;

/// <summary>
/// Draws the Kinect backdrop on the camera feed quad (Kinect > Camera Feed > Backdrop) with the
/// EnergyBall/KinectFeed shader, and sizes the quad to the image of the camera the backdrop is
/// aligned in (Framing: Fit or Fill against the render camera's view).
///
/// The quad's size is also what lines players up with the image: SceneController projects joints
/// and BodyDepthOccluder places body pixels through the quad, so resizing it here keeps them
/// aligned in every mode, including None (no image, depth-space alignment).
///
/// Play mode only: the quad keeps its scene size and material in edit mode.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class KinectFeedDisplay : MonoBehaviour
{
    [Tooltip("Provides the color / infrared / depth-to-color textures.")]
    public BodySourceManager bodySourceManager;

    [Tooltip("The camera the quad is framed for (Main Camera, the quad's parent).")]
    public Camera renderCamera;

    [Tooltip("The EnergyBall/KinectFeed shader asset.")]
    public Shader feedShader;

    private const string ColorKeyword = "_FEED_COLOR";
    private const string InfraredKeyword = "_FEED_INFRARED";
    private const string InfraredColorKeyword = "_FEED_INFRARED_COLOR";

    private static readonly int ColorTexId = Shader.PropertyToID("_ColorTex");
    private static readonly int ColorDimsId = Shader.PropertyToID("_ColorDims");
    private static readonly int InfraredTexId = Shader.PropertyToID("_InfraredTex");
    private static readonly int DepthToColorTexId = Shader.PropertyToID("_DepthToColorTex");
    private static readonly int DepthDimsId = Shader.PropertyToID("_DepthDims");
    private static readonly int BrightnessId = Shader.PropertyToID("_InfraredBrightness");
    private static readonly int GammaId = Shader.PropertyToID("_InfraredGamma");
    private static readonly int ColorAmountId = Shader.PropertyToID("_ColorAmount");

    private MeshRenderer _renderer;
    private Material _material;
    private Vector2 _scaleSign;
    private string _activeKeyword;

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        // The scene scale carries the mirroring (negative x); only the size is replaced.
        Vector3 scale = transform.localScale;
        _scaleSign = new Vector2(scale.x < 0 ? -1f : 1f, scale.y < 0 ? -1f : 1f);

        if (renderCamera == null && transform.parent != null)
            renderCamera = transform.parent.GetComponent<Camera>();

        if (feedShader != null)
        {
            _material = new Material(feedShader) { name = "Kinect Feed (runtime)" };
            _renderer.sharedMaterial = _material;
        }
        else
        {
            Debug.LogError("KinectFeedDisplay: no feed shader assigned.", this);
        }
        _renderer.enabled = false;
    }

    private void Start()
    {
        // Size the quad before the first physics step projects any joints through it.
        var settings = Settings;
        if (settings != null)
            ApplyFraming(settings);
    }

    private void OnDestroy()
    {
        if (_material != null)
            Destroy(_material);
    }

    private static RuntimeSceneSettings Settings =>
        SceneController.Instance != null ? SceneController.Instance.GetRuntimeSettings() : null;

    private void LateUpdate()
    {
        var settings = Settings;
        if (settings == null || _material == null)
            return;

        ApplyFraming(settings);

        FeedBackdrop backdrop = settings.feedBackdrop;
        bool ready = bodySourceManager != null && HasImage(backdrop);
        _renderer.enabled = ready;
        if (!ready)
            return;

        SetKeyword(
            backdrop switch
            {
                FeedBackdrop.Infrared => InfraredKeyword,
                FeedBackdrop.InfraredColor => InfraredColorKeyword,
                _ => ColorKeyword,
            }
        );

        // Cheap per-frame binds: the textures are created once, in BodySourceManager.Start.
        _material.SetTexture(ColorTexId, bodySourceManager.ColorTexture);
        _material.SetTexture(InfraredTexId, bodySourceManager.InfraredTexture);
        _material.SetTexture(DepthToColorTexId, bodySourceManager.DepthToColorTexture);
        _material.SetVector(
            ColorDimsId,
            new Vector4(bodySourceManager.ColorWidth, bodySourceManager.ColorHeight, 0, 0)
        );
        _material.SetVector(
            DepthDimsId,
            new Vector4(bodySourceManager.DepthWidth, bodySourceManager.DepthHeight, 0, 0)
        );
        _material.SetFloat(BrightnessId, settings.infraredBrightness);
        _material.SetFloat(GammaId, settings.infraredGamma);
        _material.SetFloat(ColorAmountId, settings.infraredColorAmount);
    }

    /// <summary>
    /// The backdrop's image has arrived at least once. Before that the quad stays hidden (black)
    /// rather than showing an empty texture. Infrared + Color only needs infrared: until color
    /// arrives it shows plain infrared.
    /// </summary>
    private bool HasImage(FeedBackdrop backdrop) =>
        backdrop switch
        {
            FeedBackdrop.Color => bodySourceManager.ColorFrameCount > 0,
            FeedBackdrop.Infrared or FeedBackdrop.InfraredColor => bodySourceManager.DepthFrameCount
                > 0
                && bodySourceManager.InfraredTexture != null,
            _ => false,
        };

    private void SetKeyword(string keyword)
    {
        if (_activeKeyword == keyword)
            return;
        _material.DisableKeyword(ColorKeyword);
        _material.DisableKeyword(InfraredKeyword);
        _material.DisableKeyword(InfraredColorKeyword);
        _material.EnableKeyword(keyword);
        _activeKeyword = keyword;
    }

    /// <summary>
    /// Sizes the quad to the aligned camera's image aspect, fitted inside (Fit) or covering
    /// (Fill) the render camera's view at the quad's distance.
    /// </summary>
    private void ApplyFraming(RuntimeSceneSettings settings)
    {
        if (renderCamera == null)
            return;

        float distance = transform.localPosition.z;
        if (distance <= 0f)
            return;

        float viewHeight =
            2f * distance * Mathf.Tan(renderCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float viewWidth = viewHeight * renderCamera.aspect;

        Vector2Int imageSize =
            bodySourceManager != null
                ? bodySourceManager.FeedSize(settings.feedBackdrop.Space())
                : new Vector2Int(1920, 1080);
        float imageAspect = (float)imageSize.x / Mathf.Max(1, imageSize.y);

        // Fit: the image's limiting side matches the view; Fill: the other side does.
        bool widthLimited = imageAspect > viewWidth / viewHeight;
        if (settings.feedFraming == FeedFraming.Fill)
            widthLimited = !widthLimited;

        float width = widthLimited ? viewWidth : viewHeight * imageAspect;
        float height = widthLimited ? viewWidth / imageAspect : viewHeight;

        var scale = new Vector3(
            _scaleSign.x * width,
            _scaleSign.y * height,
            transform.localScale.z
        );
        if (transform.localScale != scale)
            transform.localScale = scale;
    }
}
