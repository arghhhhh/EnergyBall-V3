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

        // Track single-hand-open state for momentum-preserving final push
        UpdateSingleHandOpenTracking(player);

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
            // The "midpoint" will be the last single open hand's position (if we were in that
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

    void UpdateSingleHandOpenTracking(PlayerConstructor player)
    {
        bool leftOpen =
            player.leftHandState == HandState.Open || player.leftHandStateClamped == HandState.Open;
        bool rightOpen =
            player.rightHandState == HandState.Open
            || player.rightHandStateClamped == HandState.Open;

        // Determine current single-hand state
        PlayerConstructor.SingleOpenHand currentSingleHand = PlayerConstructor.SingleOpenHand.None;
        if (leftOpen && !rightOpen)
        {
            currentSingleHand = PlayerConstructor.SingleOpenHand.Left;
        }
        else if (rightOpen && !leftOpen)
        {
            currentSingleHand = PlayerConstructor.SingleOpenHand.Right;
        }

        // Update tracking based on state changes
        if (currentSingleHand != PlayerConstructor.SingleOpenHand.None)
        {
            // We're in single-hand-open state
            if (player.lastSingleOpenHand != currentSingleHand)
            {
                // Just entered this single-hand state (or switched hands)
                player.lastSingleOpenHand = currentSingleHand;
                player.singleHandOpenStartTime = Time.time;
            }
            // If same hand, keep the existing start time
        }
        // When both hands are open or both closed, we preserve lastSingleOpenHand
        // so CalculateMidpoint can use it when transitioning to both-closed
    }

    bool isSingleHandOpen(PlayerConstructor player)
    {
        return (
                player.leftHandStateClamped == HandState.Open
                && player.rightHandStateClamped == HandState.Closed
            )
            || (
                player.leftHandStateClamped == HandState.Closed
                && player.rightHandStateClamped == HandState.Open
            );
    }

    Vector3 CalculateMidpoint(PlayerConstructor player)
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
            // Both hands are closed (or both open) - check if we were recently in single-hand-open
            // If so, use that hand's position to preserve momentum direction during final push
            float timeSinceSingleHandOpen = Time.time - player.singleHandOpenStartTime;
            bool wasSingleHandOpenLongEnough =
                player.lastSingleOpenHand != PlayerConstructor.SingleOpenHand.None
                && timeSinceSingleHandOpen >= runtimeSettings.singleHandOpenThreshold;

            if (wasSingleHandOpenLongEnough)
            {
                // Use the last single open hand's position as the "midpoint"
                // This preserves momentum direction when closing the last open hand
                return player.lastSingleOpenHand == PlayerConstructor.SingleOpenHand.Left
                    ? player.HandLeft.transform.position
                    : player.HandRight.transform.position;
            }

            // Default: true midpoint between both hands
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

        Vector3 handMidpoint = CalculateMidpoint(player);

        Vector3 offsetMidpoint = handMidpoint + alignmentVector * alignmentVectorScaler;
        float distance = Vector3.Distance(offsetMidpoint, player.sphere.transform.position);
        Vector3 direction = offsetMidpoint - player.sphere.position;

        PushToTarget(player, distance, direction);
    }

    void PushToTarget(PlayerConstructor player, float distance, Vector3 direction)
    {
        var runtimeSettings = controller.GetRuntimeSettings();

        float relativeDistance = Mathf.InverseLerp(
            runtimeSettings.maxDistanceBetweenHands,
            0,
            distance
        );
        float forceDamper =
            runtimeSettings.forceToMiddle.Evaluate(relativeDistance)
            * (
                (isSingleHandOpen(player) && player.IsInbounds())
                    ? runtimeSettings.singleHandOpenForceDamper
                    : 1f
            );

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
