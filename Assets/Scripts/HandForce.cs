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
