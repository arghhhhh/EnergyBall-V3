using Assets.Scripts;
using UnityEngine;
using Windows.Kinect;

public class HandForce
{
    readonly SceneController controller = SceneController.Instance;

    public void ManageHandForce(PlayerConstructor player)
    {
        // controller.debugText.text =
        //     $"leftHandState: {player.leftHandState}\nrightHandState: {player.rightHandState}";

        if (
            (player.leftHandState == HandState.Closed || player.leftHandState == HandState.Unknown)
            && (
                player.rightHandState == HandState.Closed
                || player.rightHandState == HandState.Unknown
            )
        )
        {
            player.pushParticles = true;
            // Apply one final push when transitioning to both-hands-closed.
            // The push target will be the last single open hand's position (if we were in that
            // state long enough), preserving the sphere's momentum direction.
            if (player.initialized && player.turnOnParticles)
            {
                AlignAndCalculateVectors(player);
            }
            player.turnOnParticles = false;
        }
        else
        {
            player.pushParticles = false;
            player.turnOnParticles = true;
            AlignAndCalculateVectors(player);
        }
    }

    /// <summary>
    /// Calculates the target position for the sphere's push force.
    /// Returns the appropriate hand position based on current state:
    /// - Single hand open: that hand's position
    /// - Both hands closed (after single-hand-open long enough): last open hand's position (preserves momentum)
    /// - Both hands open: true midpoint between hands
    /// - Both hands closed (quick switch or from both-open): true midpoint between hands
    /// </summary>
    Vector3 CalculatePushTarget(PlayerConstructor player)
    {
        var runtimeSettings = controller.GetRuntimeSettings();

        // Giving myself an extra frame of leeway in case hand tracking returns unknown
        if (
            player.leftHandState == HandState.Open
            && player.rightHandState != HandState.Open
            && player.rightHandStateClamped != HandState.Open
        )
        {
            return player.HandLeft.transform.position;
        }
        else if (
            player.leftHandState != HandState.Open
            && player.rightHandState == HandState.Open
            && player.leftHandStateClamped != HandState.Open
        )
        {
            return player.HandRight.transform.position;
        }
        else
        {
            // Check if both hands are currently closed (not both open)
            bool bothHandsClosed =
                player.leftHandState != HandState.Open && player.rightHandState != HandState.Open;

            // Only use last single open hand's position when BOTH hands are CLOSED
            // (to preserve momentum direction during final push). When both hands are OPEN,
            // always use the true midpoint.
            if (bothHandsClosed)
            {
                float timeSinceSingleHandOpen = Time.time - player.singleHandOpenStartTime;
                bool wasSingleHandOpenLongEnough =
                    player.lastSingleOpenHand != PlayerConstructor.SingleOpenHand.None
                    && timeSinceSingleHandOpen >= runtimeSettings.singleHandOpenThreshold;

                if (wasSingleHandOpenLongEnough)
                {
                    // Use the last single open hand's position as the target
                    // This preserves momentum direction when closing the last open hand
                    return player.lastSingleOpenHand == PlayerConstructor.SingleOpenHand.Left
                        ? player.HandLeft.transform.position
                        : player.HandRight.transform.position;
                }
            }

            // Default: true midpoint between both hands (used when both open, or quick switch)
            return (player.HandLeft.transform.position + player.HandRight.transform.position)
                * 0.5f;
        }
    }

    // See dev log [2024-08-03] for visuals on what this does
    void AlignAndCalculateVectors(PlayerConstructor player)
    {
        // get vector from leftWrist to leftHandTip and normalize it
        Vector3 leftHandVector = (
            player.HandtipLeft.transform.position - player.WristLeft.transform.position
        ).normalized;
        // do same for right hand
        Vector3 rightHandVector = (
            player.HandtipLeft.transform.position - player.WristRight.transform.position
        ).normalized;
        // lerp halfway between the two vectors
        Vector3 alignmentVector = Vector3.Lerp(leftHandVector, rightHandVector, 0.5f);

        // get the distance between the two hands and remap it to 0-1
        // use that value to evaluate the alignmentVectorStrength curve
        float handDistance = Vector3.Distance(
            player.HandRight.transform.position,
            player.HandLeft.transform.position
        );
        // controller.debugText.text = "Hand Distance: " + Mathf.Round(handDistance * 100) / 100;

        var runtimeSettings = controller.GetRuntimeSettings();
        float remappedHandDistance = Mathf.InverseLerp(
            0,
            runtimeSettings.maxDistanceBetweenHands,
            Mathf.Clamp(handDistance, 0, runtimeSettings.maxDistanceBetweenHands)
        );
        float alignmentVectorScaler =
            runtimeSettings.alignmentVectorStrength.Evaluate(remappedHandDistance)
            * runtimeSettings.alignmentVectorStrengthScaler;

        Vector3 pushTarget = CalculatePushTarget(player);

        Vector3 offsetTarget = pushTarget + alignmentVector * alignmentVectorScaler;
        float distance = Vector3.Distance(offsetTarget, player.sphere.transform.position);
        Vector3 direction = offsetTarget - player.sphere.position;

        ApplyForceTowardTarget(player, distance, direction);
    }

    void ApplyForceTowardTarget(PlayerConstructor player, float distance, Vector3 direction)
    {
        var runtimeSettings = controller.GetRuntimeSettings();

        float relativeDistance = Mathf.InverseLerp(
            runtimeSettings.maxDistanceBetweenHands,
            0,
            distance
        );

        // Calculate the single-hand force multiplier with smooth transition
        float singleHandMultiplier = 1f;
        if (player.IsSingleHandOpen && player.IsInbounds())
        {
            // Currently in single-hand-open state - use full damper
            singleHandMultiplier = runtimeSettings.singleHandOpenForceDamper;
        }
        else
        {
            // Check if we recently left single-hand-open state (for smooth transition)
            float timeSinceSingleHandEnded = Time.time - player.singleHandOpenEndTime;
            float transitionDuration = runtimeSettings.singleHandForceLerpDuration;

            if (timeSinceSingleHandEnded < transitionDuration && transitionDuration > 0)
            {
                // Lerp from damped force to full force over the transition duration
                float t = timeSinceSingleHandEnded / transitionDuration;
                singleHandMultiplier = Mathf.Lerp(runtimeSettings.singleHandOpenForceDamper, 1f, t);
            }
        }

        float forceDamper =
            runtimeSettings.forceToMiddle.Evaluate(relativeDistance) * singleHandMultiplier;

        Vector3 forceDirection = direction.normalized;
        Vector3 forceVector = runtimeSettings.pushForce * forceDamper * forceDirection;

        if (player.pushParticles)
        {
            forceVector *= runtimeSettings.handPushScaler;
            player.sphere.linearDamping = 0;
        }
        else
        {
            // need to make sure drag never goes below min drag. Might as well clamp both sides
            player.sphere.linearDamping = Utils.RemapClamped(
                distance,
                runtimeSettings.maxDistanceBetweenHands,
                0,
                runtimeSettings.minDrag,
                runtimeSettings.maxDrag
            );
        }

        player.sphere.AddForce(forceVector);
    }
}
