using Assets.Scripts;
using UnityEngine;

public class BoundaryForce
{
    readonly SceneController controller = SceneController.Instance;

    /// <summary>
    /// Applies a soft spring force to keep the sphere within a maximum distance from the hand midpoint.
    /// This prevents the sphere from traveling too far when hands are closed, making it faster to return.
    /// </summary>
    public void ManageBoundaryForce(PlayerConstructor player)
    {
        var runtimeSettings = controller.GetRuntimeSettings();

        // Skip if boundary force is disabled
        if (runtimeSettings.boundaryForceMultiplier <= 0)
        {
            return;
        }

        // Calculate hand midpoint (same logic as HandForce.CalculateMidpoint)
        Vector3 handMidpoint = (player.HandLeft.transform.position + player.HandRight.transform.position) * 0.5f;

        // Get max allowed distance based on grid size
        float maxDistance = GetMaxDistanceFromHands(runtimeSettings);

        // Calculate current distance from hands to sphere
        Vector3 toSphere = player.sphere.position - handMidpoint;
        float distance = toSphere.magnitude;

        // Only apply force if beyond the boundary
        if (distance > maxDistance)
        {
            // How far past the boundary
            float overshoot = distance - maxDistance;

            // Push back toward hand midpoint, force scales with overshoot (spring behavior)
            Vector3 pushBackDirection = -toSphere.normalized;
            float pushBackForce = overshoot * runtimeSettings.boundaryForceMultiplier;

            player.sphere.AddForce(pushBackDirection * pushBackForce);

            // Apply extra drag when moving away from hands (not when returning)
            Vector3 velocity = player.sphere.linearVelocity;
            float outwardSpeed = Vector3.Dot(velocity, toSphere.normalized);

            if (outwardSpeed > 0 && runtimeSettings.boundaryOutwardDrag > 0)
            {
                // Moving away from center - apply directional drag
                // We do this by applying an opposing force proportional to outward velocity
                Vector3 outwardVelocity = toSphere.normalized * outwardSpeed;
                Vector3 dragForce = -outwardVelocity * runtimeSettings.boundaryOutwardDrag;
                player.sphere.AddForce(dragForce);
            }
        }
    }

    /// <summary>
    /// Calculates the maximum allowed distance from hand midpoint based on grid size.
    /// Default is 1.5x the longest side of the grid / 2 (half since we measure from center).
    /// </summary>
    private float GetMaxDistanceFromHands(RuntimeSceneSettings runtimeSettings)
    {
        // Get grid size from MetaballsToSDF
        Vector3 gridSize = controller.GetGridSize();

        // Find the longest side
        float longestSide = Mathf.Max(gridSize.x, Mathf.Max(gridSize.y, gridSize.z));

        // Max distance is multiplier * (longest side / 2)
        // The /2 is because the grid is centered, so max travel from center is half the side
        return runtimeSettings.boundaryDistanceMultiplier * (longestSide / 2f);
    }
}
