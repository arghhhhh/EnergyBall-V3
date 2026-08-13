using MarchingCubes;
using UnityEngine;

/// <summary>
/// Single home for metaball-volume visualization (absorbs the old BoundaryGizmos):
///
/// - Scene view / edit mode: gizmo wire cubes for the marching-cubes grid and
///   the force boundary (grid + addedBoundaryDistance), no play mode required.
/// - Game view (play mode): the same boxes as depth-tested line meshes on the
///   KinectOverlay layer, toggled by the "Show Metaball Bounds" debug setting —
///   the body occluder hides edges behind people/objects, giving a depth cue
///   against the flat camera feed.
///
/// The volume is world-fixed at (0, 0, baseZDepth) with size
/// MetaballsToSDF.GetGridSize(), the same box used for metaball clamping and
/// BoundaryForce.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MetaballBoundsVisualizer : MonoBehaviour
{
    [Header("Visibility")]
    public bool showGridBoundary = true;
    public bool showForceBoundary = true;

    [Header("Colors")]
    public Color gridBoundaryColor = new Color(0.2f, 1f, 0.4f, 1f);
    public Color forceBoundaryColor = new Color(1f, 0.5f, 0f, 1f);

    [Header("Runtime Rendering")]
    [Tooltip("Material for the in-game wire boxes (BoundsLine: vertex-colored, depth-tested).")]
    public Material lineMaterial;

    [Header("References (auto-found if empty)")]
    public SceneController sceneController;
    public MetaballsToSDF metaballsToSDF;

    private MeshRenderer _renderer;
    private Mesh _forceMesh;

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
        GetComponent<MeshFilter>().sharedMesh = BuildWireCube(gridBoundaryColor);
        _forceMesh = BuildWireCube(forceBoundaryColor);
    }

    void LateUpdate()
    {
        var controller = SceneController.Instance;
        if (controller == null)
        {
            return;
        }

        var settings = controller.GetRuntimeSettings();
        Vector3 gridSize = controller.GetGridSize();
        Vector3 center = new Vector3(0f, 0f, settings.baseZDepth);

        _renderer.enabled = settings.showMetaballBounds && showGridBoundary;
        transform.position = center;
        transform.localScale = gridSize;

        if (settings.showMetaballBounds && showForceBoundary && lineMaterial != null)
        {
            float doubled = settings.addedBoundaryDistance * 2f;
            Vector3 forceSize = gridSize + new Vector3(doubled, doubled, doubled);
            Graphics.DrawMesh(
                _forceMesh,
                Matrix4x4.TRS(center, Quaternion.identity, forceSize),
                lineMaterial,
                gameObject.layer
            );
        }
    }

    // Edit-mode + scene-view boxes (absorbed from BoundaryGizmos): uses
    // inspector values so it works without play mode.
    void OnDrawGizmos()
    {
        if (sceneController == null)
        {
            sceneController = FindFirstObjectByType<SceneController>();
        }
        if (metaballsToSDF == null)
        {
            metaballsToSDF = FindFirstObjectByType<MetaballsToSDF>();
        }

        if (metaballsToSDF == null)
        {
            return;
        }

        Vector3 gridSize = metaballsToSDF.GetGridSize();
        if (gridSize == Vector3.zero)
        {
            return;
        }

        float baseZDepth = sceneController != null ? sceneController.baseZDepth : 5f;
        float addedBoundaryDistance =
            sceneController != null ? sceneController.addedBoundaryDistance : 1.5f;
        Vector3 center = new Vector3(0f, 0f, baseZDepth);

        if (showGridBoundary)
        {
            Gizmos.color = gridBoundaryColor;
            Gizmos.DrawWireCube(center, gridSize);
        }

        if (showForceBoundary)
        {
            float doubled = addedBoundaryDistance * 2f;
            Gizmos.color = forceBoundaryColor;
            Gizmos.DrawWireCube(center, gridSize + new Vector3(doubled, doubled, doubled));
        }
    }

    // Unit cube (±0.5) as 12 line-topology edges; scaled at draw time.
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
