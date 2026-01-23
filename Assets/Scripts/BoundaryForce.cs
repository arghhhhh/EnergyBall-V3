using UnityEngine;

public class BoundaryForce
{
    readonly SceneController controller = SceneController.Instance;

    /// <summary>
    /// Applies drag to stop the sphere when it exceeds the scaled grid boundary (AABB).
    /// This prevents the sphere from traveling too far outside the grid, making it faster to return.
    /// Unlike a spring approach, this doesn't bounce the sphere back - it just stops it in place.
    /// When outside the boundary on any axis, drag is applied to ALL velocity components to prevent
    /// the sphere from "sliding along the wall" when exiting at an angle.
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
        Vector3 boundaryExtents = GetScaledBoundaryExtents(runtimeSettings.addedBoundaryDistance);

        // Grid center (offset by baseZDepth on Z axis)
        Vector3 gridCenter = new Vector3(0f, 0f, runtimeSettings.baseZDepth);

        // Sphere position relative to grid center
        Vector3 spherePos = player.sphere.position;
        Vector3 relativePos = spherePos - gridCenter;

        // Check if outside boundary on any axis AND moving outward on that axis
        bool isOutsideAndMovingOut = false;
        Vector3 velocity = player.sphere.linearVelocity;

        // X axis: outside and moving outward
        if ((relativePos.x > boundaryExtents.x && velocity.x > 0) ||
            (relativePos.x < -boundaryExtents.x && velocity.x < 0))
        {
            isOutsideAndMovingOut = true;
        }

        // Y axis: outside and moving outward
        if ((relativePos.y > boundaryExtents.y && velocity.y > 0) ||
            (relativePos.y < -boundaryExtents.y && velocity.y < 0))
        {
            isOutsideAndMovingOut = true;
        }

        // Z axis: outside and moving outward
        if ((relativePos.z > boundaryExtents.z && velocity.z > 0) ||
            (relativePos.z < -boundaryExtents.z && velocity.z < 0))
        {
            isOutsideAndMovingOut = true;
        }

        // If outside on any axis and moving outward, apply drag to ALL velocity components
        if (isOutsideAndMovingOut)
        {
            Vector3 dragForce = -velocity * runtimeSettings.boundaryOutwardDrag;
            player.sphere.AddForce(dragForce);
        }
    }

    /// <summary>
    /// Calculates the scaled boundary extents (half-sizes) based on grid size and multiplier.
    /// </summary>
    private Vector3 GetScaledBoundaryExtents(float addedBoundaryDistance)
    {
        // Get grid size from MetaballsToSDF
        Vector3 gridSize = controller.GetGridSize();
        float additionalSize = addedBoundaryDistance * 2f;
        Vector3 scaledSize = gridSize + new Vector3(additionalSize, additionalSize, additionalSize);

        // Grid extents are half the grid size (distance from center to edge)
        return scaledSize / 2f;
    }
}
