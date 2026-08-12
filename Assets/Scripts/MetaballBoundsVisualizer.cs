using UnityEngine;

/// <summary>
/// Draws the metaball volume's bounding box as a wireframe in game view so the
/// grid placement can be checked while tuning baseZDepth. The box is world-fixed
/// at (0, 0, baseZDepth) with size MetaballsToSDF.GetGridSize() — the same
/// volume used for metaball clamping and boundary forces. Toggled by the
/// "Show Metaball Bounds" debug setting.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MetaballBoundsVisualizer : MonoBehaviour
{
    [Tooltip("Material for the wire box (SkeletonLine works: vertex-colored, ZTest Always).")]
    public Material lineMaterial;

    [Tooltip("Wireframe color.")]
    public Color color = new Color(0.2f, 1f, 0.4f, 1f);

    private MeshRenderer _renderer;

    void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        _renderer.enabled = false;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        if (lineMaterial != null)
        {
            _renderer.sharedMaterial = lineMaterial;
        }
        GetComponent<MeshFilter>().sharedMesh = BuildWireCube(color);
    }

    void LateUpdate()
    {
        var controller = SceneController.Instance;
        if (controller == null)
        {
            return;
        }

        var settings = controller.GetRuntimeSettings();
        _renderer.enabled = settings.showMetaballBounds;
        if (!_renderer.enabled)
        {
            return;
        }

        transform.position = new Vector3(0f, 0f, settings.baseZDepth);
        transform.localScale = controller.GetGridSize();
    }

    // Unit cube (±0.5) as 12 line-topology edges; scaled via the transform.
    private static Mesh BuildWireCube(Color color)
    {
        var vertices = new Vector3[8];
        var colors = new Color[8];
        for (int i = 0; i < 8; i++)
        {
            vertices[i] = new Vector3(
                (i & 4) != 0 ? 0.5f : -0.5f,
                (i & 2) != 0 ? 0.5f : -0.5f,
                (i & 1) != 0 ? 0.5f : -0.5f
            );
            colors[i] = color;
        }

        // Pairs of vertex indices differing by exactly one axis bit
        int[] edges = { 0, 1, 0, 2, 1, 3, 2, 3, 4, 5, 4, 6, 5, 7, 6, 7, 0, 4, 1, 5, 2, 6, 3, 7 };

        var mesh = new Mesh
        {
            name = "MetaballBoundsWire",
            vertices = vertices,
            colors = colors,
        };
        mesh.SetIndices(edges, MeshTopology.Lines, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        return mesh;
    }
}
