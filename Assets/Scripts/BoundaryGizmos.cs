using MarchingCubes;
using UnityEngine;

public class BoundaryGizmos : MonoBehaviour
{
    [Header("Visibility")]
    public bool showGridBoundary = true;
    public bool showForceBoundary = true;

    [Header("Colors")]
    public Color gridBoundaryColor = new Color(0f, 1f, 0f, 0.5f);
    public Color forceBoundaryColor = new Color(1f, 0.5f, 0f, 0.5f);

    [Header("References (auto-found if empty)")]
    public SceneController sceneController;
    public MetaballsToSDF metaballsToSDF;

    void OnDrawGizmos()
    {
        // Find references if not set
        if (sceneController == null)
        {
            sceneController = FindFirstObjectByType<SceneController>();
        }
        if (metaballsToSDF == null)
        {
            metaballsToSDF = FindFirstObjectByType<MetaballsToSDF>();
        }

        if (metaballsToSDF == null)
            return;

        Vector3 gridSize = metaballsToSDF.GetGridSize();
        if (gridSize == Vector3.zero)
            return;

        // Get baseZDepth - use inspector value in edit mode, runtime settings in play mode
        float baseZDepth = sceneController != null ? sceneController.baseZDepth : 5f;
        float addedBoundaryDistance =
            sceneController != null ? sceneController.addedBoundaryDistance : 1.5f;

        // Grid boundary (marching cubes)
        if (showGridBoundary)
        {
            DrawGridBoundary(gridSize, baseZDepth);
        }

        // Force boundary (scaled grid box) - stationary, works in both modes
        if (showForceBoundary)
        {
            DrawForceBoundary(gridSize, baseZDepth, addedBoundaryDistance);
        }
    }

    void DrawGridBoundary(Vector3 gridSize, float baseZDepth)
    {
        Gizmos.color = gridBoundaryColor;

        // Grid is centered at origin, offset by baseZDepth on Z
        Vector3 center = new Vector3(0f, 0f, baseZDepth);

        Gizmos.DrawWireCube(center, gridSize);
    }

    void DrawForceBoundary(Vector3 gridSize, float baseZDepth, float addedBoundaryDistance)
    {
        Gizmos.color = forceBoundaryColor;

        float doubledSize = addedBoundaryDistance * 2f;
        Vector3 scaledSize = gridSize + new Vector3(doubledSize, doubledSize, doubledSize);

        // Draw at grid center - boundary is stationary relative to the grid
        Vector3 center = new Vector3(0f, 0f, baseZDepth);
        Gizmos.DrawWireCube(center, scaledSize);
    }
}
