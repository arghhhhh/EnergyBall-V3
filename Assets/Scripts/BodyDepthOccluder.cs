using UnityEngine;

/// <summary>
/// Renders Kinect-tracked bodies as an invisible depth-only occluder so that
/// energy-ball particles disappear behind people. Builds one grid vertex per
/// depth pixel; the BodyDepthOccluder shader projects each body pixel onto the
/// ray through its position on the displayed color-feed quad, at the pixel's
/// real sensor depth. Must live on the layer the Overlay Camera renders
/// (KinectOverlay) — the overlay pass clears depth, so depth written in the
/// base pass never reaches the particles.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BodyDepthOccluder : MonoBehaviour
{
    [Tooltip("The BodySourceManager providing depth/body-index textures.")]
    public BodySourceManager bodySourceManager;

    [Tooltip("The quad displaying the Kinect color feed (child of Main Camera).")]
    public Transform cameraFeedQuad;

    [Tooltip("The EnergyBall/BodyDepthOccluder shader asset.")]
    public Shader occluderShader;

    [Tooltip(
        "Push occluder slightly away from camera to avoid z-fighting with particles at body depth. "
            + "World units at bodyScale 1 (multiplied by bodyScale at runtime)."
    )]
    public float depthBias = 0.0f;

    [Tooltip("Ignore depth samples closer than this (meters).")]
    public float minDepth = 0.4f;

    [Tooltip(
        "On: only Kinect-tracked human bodies occlude particles. Off: anything the depth camera sees (chairs, walls, floor) occludes."
    )]
    public bool bodiesOnly = false;

    private Material _material;
    private MeshRenderer _renderer;
    private bool _initialized;

    void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        _renderer.enabled = false;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    void LateUpdate()
    {
        if (!_initialized)
        {
            TryInitialize();
            return;
        }

        // Quad is a child of the camera, but keep the matrix fresh in case
        // either is moved at runtime.
        _material.SetMatrix("_QuadLocalToWorld", cameraFeedQuad.localToWorldMatrix);

        float bodyScale = 1f;
        bool showPointCloud = false;
        if (SceneController.Instance != null)
        {
            // Effective settings (rebuilt on every settings change).
            var settings = SceneController.Instance.GetRuntimeSettings();
            bodyScale = settings.bodyScale;
            showPointCloud = settings.showPointCloud;
        }
        _material.SetFloat("_BodyScale", bodyScale);
        // NOTE: must be SetInt, not SetInteger — ShaderLab "Int" properties are
        // float-backed, and SetInteger writes a separate integer slot that the
        // ColorMask [_ColorMask] state binding never reads.
        _material.SetInt("_ColorMask", showPointCloud ? 15 : 0);
        // depthBias is a world-unit bias authored at 1x; minDepth is Kinect meters (pre-scale).
        _material.SetFloat("_DepthBias", depthBias * bodyScale);
        _material.SetFloat("_MinDepth", minDepth);
        _material.SetFloat("_BodiesOnly", bodiesOnly ? 1f : 0f);
    }

    private void TryInitialize()
    {
        if (
            bodySourceManager == null
            || cameraFeedQuad == null
            || occluderShader == null
            || !bodySourceManager.DepthFramesReady
        )
        {
            return;
        }

        _material = new Material(occluderShader);
        _material.SetTexture("_DepthTex", bodySourceManager.DepthTexture);
        _material.SetTexture("_BodyIndexTex", bodySourceManager.BodyIndexTexture);
        _material.SetTexture("_DepthToColorTex", bodySourceManager.DepthToColorTexture);
        _material.SetVector(
            "_ColorDims",
            new Vector4(bodySourceManager.ColorWidth, bodySourceManager.ColorHeight, 0, 0)
        );

        GetComponent<MeshFilter>().sharedMesh = BuildGridMesh(
            bodySourceManager.DepthWidth,
            bodySourceManager.DepthHeight
        );
        _renderer.sharedMaterial = _material;
        _renderer.enabled = true;
        _initialized = true;
    }

    // One vertex per depth pixel; positions are computed entirely in the vertex
    // shader from the pixel's UV, so vertex positions here are dummies.
    private static Mesh BuildGridMesh(int width, int height)
    {
        var vertices = new Vector3[width * height];
        var uvs = new Vector2[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                uvs[i] = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
            }
        }

        var triangles = new int[(width - 1) * (height - 1) * 6];
        int t = 0;
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int i = y * width + x;
                triangles[t++] = i;
                triangles[t++] = i + width;
                triangles[t++] = i + 1;
                triangles[t++] = i + 1;
                triangles[t++] = i + width;
                triangles[t++] = i + width + 1;
            }
        }

        var mesh = new Mesh
        {
            name = "BodyDepthOccluderGrid",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            vertices = vertices,
            uv = uvs,
            triangles = triangles,
        };
        // Positions come from the vertex shader; disable frustum culling by
        // giving the mesh effectively infinite bounds.
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
        return mesh;
    }

    void OnDestroy()
    {
        if (_material != null)
        {
            Destroy(_material);
        }
    }
}
