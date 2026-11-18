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

    [Tooltip(
        "Shaders/DepthCoverage.compute: blurs the depth camera's has-a-reading mask for the "
            + "Infrared + Color hole fade."
    )]
    public ComputeShader coverageCompute;

    private const string ColorKeyword = "_FEED_COLOR";
    private const string InfraredKeyword = "_FEED_INFRARED";
    private const string InfraredColorKeyword = "_FEED_INFRARED_COLOR";

    private static readonly int ColorTexId = Shader.PropertyToID("_ColorTex");
    private static readonly int ColorDimsId = Shader.PropertyToID("_ColorDims");
    private static readonly int InfraredTexId = Shader.PropertyToID("_InfraredTex");
    private static readonly int DepthToColorTexId = Shader.PropertyToID("_DepthToColorTex");
    private static readonly int DepthDimsId = Shader.PropertyToID("_DepthDims");
    private static readonly int CoverageTexId = Shader.PropertyToID("_CoverageTex");
    private static readonly int BrightnessId = Shader.PropertyToID("_InfraredBrightness");
    private static readonly int GammaId = Shader.PropertyToID("_InfraredGamma");
    private static readonly int ColorAmountId = Shader.PropertyToID("_ColorAmount");

    private MeshRenderer _renderer;
    private Material _material;
    private Vector2 _scaleSign;
    private string _activeKeyword;

    // Hole fade mask (DepthCoverage.compute), rebuilt once per depth frame in Infrared + Color.
    private static readonly int CsDepthId = Shader.PropertyToID("_Depth");
    private static readonly int CsTempId = Shader.PropertyToID("_Temp");
    private static readonly int CsTempInId = Shader.PropertyToID("_TempIn");
    private static readonly int CsCoverageId = Shader.PropertyToID("_Coverage");
    private static readonly int CsSizeId = Shader.PropertyToID("_Size");
    private static readonly int CsRadiusId = Shader.PropertyToID("_Radius");
    private static readonly int CsSigmaId = Shader.PropertyToID("_Sigma");
    private RenderTexture _coverageTemp;
    private RenderTexture _coverage;
    private int _coverageFrame = -1;
    private float _coverageRadius = -1f;
    private bool _warnedNoCoverageCompute;

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
        ReleaseCoverage(ref _coverageTemp);
        ReleaseCoverage(ref _coverage);
    }

    private static void ReleaseCoverage(ref RenderTexture texture)
    {
        if (texture == null)
            return;
        texture.Release();
        Destroy(texture);
        texture = null;
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
        if (backdrop == FeedBackdrop.InfraredColor)
            UpdateCoverage(settings.infraredHoleFade);
        _material.SetTexture(CoverageTexId, _coverage);
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

    /// <summary>
    /// Rebuilds the blurred has-a-reading mask (see DepthCoverage.compute) when a new depth
    /// frame arrived or the fade radius changed.
    /// </summary>
    private void UpdateCoverage(float radius)
    {
        var depth = bodySourceManager.DepthTexture;
        if (coverageCompute == null)
        {
            // The shader then reads full coverage: holes keep a hard edge, nothing breaks.
            if (!_warnedNoCoverageCompute)
            {
                _warnedNoCoverageCompute = true;
                Debug.LogWarning(
                    "KinectFeedDisplay: no Coverage Compute assigned "
                        + "(Shaders/DepthCoverage.compute), so Hole Fade has no effect.",
                    this
                );
            }
            return;
        }
        if (depth == null)
            return;

        int frame = bodySourceManager.DepthFrameCount;
        if (
            frame == _coverageFrame
            && Mathf.Approximately(radius, _coverageRadius)
            && _coverage != null
        )
            return;
        _coverageFrame = frame;
        _coverageRadius = radius;

        int width = depth.width;
        int height = depth.height;
        EnsureCoverageTexture(ref _coverageTemp, width, height, "Depth Coverage (temp)");
        EnsureCoverageTexture(ref _coverage, width, height, "Depth Coverage");

        int taps = Mathf.Max(0, Mathf.RoundToInt(radius));
        coverageCompute.SetInts(CsSizeId, width, height);
        coverageCompute.SetInt(CsRadiusId, taps);
        // The radius spans about two standard deviations, so the falloff ends softly.
        coverageCompute.SetFloat(CsSigmaId, Mathf.Max(0.5f, radius * 0.5f));

        int groupsX = Mathf.CeilToInt(width / 8f);
        int groupsY = Mathf.CeilToInt(height / 8f);

        int horizontal = coverageCompute.FindKernel("Horizontal");
        coverageCompute.SetTexture(horizontal, CsDepthId, depth);
        coverageCompute.SetTexture(horizontal, CsTempId, _coverageTemp);
        coverageCompute.Dispatch(horizontal, groupsX, groupsY, 1);

        int vertical = coverageCompute.FindKernel("Vertical");
        coverageCompute.SetTexture(vertical, CsTempInId, _coverageTemp);
        coverageCompute.SetTexture(vertical, CsCoverageId, _coverage);
        coverageCompute.Dispatch(vertical, groupsX, groupsY, 1);
    }

    private static void EnsureCoverageTexture(
        ref RenderTexture texture,
        int width,
        int height,
        string name
    )
    {
        if (texture != null && texture.width == width && texture.height == height)
            return;
        ReleaseCoverage(ref texture);
        texture = new RenderTexture(width, height, 0, RenderTextureFormat.RHalf)
        {
            name = name,
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.Create();
    }

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
