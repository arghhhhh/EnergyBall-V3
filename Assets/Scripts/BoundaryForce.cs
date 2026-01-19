using UnityEngine;

public class BoundaryForce
{
    readonly SceneController controller = SceneController.Instance;

    /// <summary>
    /// Applies drag to stop the sphere when it exceeds the scaled grid boundary (AABB).
    /// This prevents the sphere from traveling too far outside the grid, making it faster to return.
    /// Unlike a spring approach, this doesn't bounce the sphere back - it just stops it in place.
    /// Drag is applied per-axis only when outside the boundary and moving outward on that axis.
    /// </summary>
    public void ManageBoundaryForce(PlayerConstructor player)
    {
        var runtimeSettings = controller.GetRuntimeSettings();

        // Skip if boundary drag is disabled
        if (runtimeSettings.boundaryOutwardDrag <= 0)
        {
            return;
        }

        // Get scaled boundary extents
        Vector3 boundaryExtents = GetScaledBoundaryExtents(runtimeSettings);

        // Grid center (offset by baseZDepth on Z axis)
        Vector3 gridCenter = new Vector3(0f, 0f, runtimeSettings.baseZDepth);

        // Sphere position relative to grid center
        Vector3 spherePos = player.sphere.position;
        Vector3 relativePos = spherePos - gridCenter;

        // Current velocity
        Vector3 velocity = player.sphere.linearVelocity;

        // Calculate drag force per axis
        Vector3 dragForce = Vector3.zero;

        // X axis
        if (relativePos.x > boundaryExtents.x && velocity.x > 0)
        {
            // Outside positive X boundary and moving further out
            dragForce.x = -velocity.x * runtimeSettings.boundaryOutwardDrag;
        }
        else if (relativePos.x < -boundaryExtents.x && velocity.x < 0)
        {
            // Outside negative X boundary and moving further out
            dragForce.x = -velocity.x * runtimeSettings.boundaryOutwardDrag;
        }

        // Y axis
        if (relativePos.y > boundaryExtents.y && velocity.y > 0)
        {
            dragForce.y = -velocity.y * runtimeSettings.boundaryOutwardDrag;
        }
        else if (relativePos.y < -boundaryExtents.y && velocity.y < 0)
        {
            dragForce.y = -velocity.y * runtimeSettings.boundaryOutwardDrag;
        }

        // Z axis
        if (relativePos.z > boundaryExtents.z && velocity.z > 0)
        {
            dragForce.z = -velocity.z * runtimeSettings.boundaryOutwardDrag;
        }
        else if (relativePos.z < -boundaryExtents.z && velocity.z < 0)
        {
            dragForce.z = -velocity.z * runtimeSettings.boundaryOutwardDrag;
        }

        // Apply combined drag force
        if (dragForce != Vector3.zero)
        {
            player.sphere.AddForce(dragForce);
        }
    }

    /// <summary>
    /// Calculates the scaled boundary extents (half-sizes) based on grid size and multiplier.
    /// </summary>
    private Vector3 GetScaledBoundaryExtents(RuntimeSceneSettings runtimeSettings)
    {
        // Get grid size from MetaballsToSDF
        Vector3 gridSize = controller.GetGridSize();

        // Scale the grid extents by the multiplier
        // Grid extents are half the grid size (distance from center to edge)
        return (gridSize / 2f) * runtimeSettings.boundaryDistanceMultiplier;
    }
}
