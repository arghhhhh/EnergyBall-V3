using UnityEngine;
using Windows.Kinect;

public class HandEffects
{
    public void ManageHandEffects(PlayerConstructor player, RuntimeSceneSettings settings)
    {
        // Check if hands are brought together to initialize the player
        if (!player.initialized)
        {
            float activationDistance = settings?.prayToActivateDistance ?? 0.7f;

            float handDistance = Vector3.Distance(player.HandLeft.transform.position, player.HandRight.transform.position);
            if (handDistance < activationDistance)
            {
                player.initialized = true;
            }
        }
        if (player.leftHandState == HandState.Open && player.leftHandStateClamped != HandState.Open && (player.isDummy || player.initialized))
        {
            float timeSinceStateChange = Time.time - player.leftHandStateChangeTime;
            if (timeSinceStateChange > 2.0f)
            {
                if (player.leftHandOpenCoroutine != null)
                {
                    player.StopCoroutine(player.leftHandOpenCoroutine);
                }
                player.leftHandOpenCoroutine = player.StartCoroutine(player.PlayLeftHandOpenAnimationDelayed());
            }
            else
            {
                player.leftHandAnimator.CrossFade(player.openClip.name, 1f);
            }
            player.leftHandVfx.SendEvent("handOpen");
            player.leftHandStateClamped = HandState.Open;
            player.leftHandStateChangeTime = Time.time;
        }
        else if (
            player.leftHandState == HandState.Closed
            && player.leftHandStateClamped != HandState.Closed && (player.isDummy || player.initialized)
        )
        {
            if (player.leftHandOpenCoroutine != null)
            {
                player.StopCoroutine(player.leftHandOpenCoroutine);
                player.leftHandOpenCoroutine = null;
            }
            player.leftHandAnimator.CrossFade(player.closedClip.name, 1f);
            player.leftHandVfx.SendEvent("handClose");
            player.leftHandStateClamped = HandState.Closed;
            player.leftHandStateChangeTime = Time.time;
        }
        if (player.rightHandState == HandState.Open && player.rightHandStateClamped != HandState.Open && (player.isDummy || player.initialized))
        {
            float timeSinceStateChange = Time.time - player.rightHandStateChangeTime;
            if (timeSinceStateChange > 2.0f)
            {
                if (player.rightHandOpenCoroutine != null)
                {
                    player.StopCoroutine(player.rightHandOpenCoroutine);
                }
                player.rightHandOpenCoroutine = player.StartCoroutine(player.PlayRightHandOpenAnimationDelayed());
            }
            else
            {
                player.rightHandAnimator.CrossFade(player.openClip.name, 1f);
            }
            player.rightHandVfx.SendEvent("handOpen");
            player.rightHandStateClamped = HandState.Open;
            player.rightHandStateChangeTime = Time.time;
        }
        else if (
            player.rightHandState == HandState.Closed
            && player.rightHandStateClamped != HandState.Closed && player.initialized
        )
        {
            if (player.rightHandOpenCoroutine != null)
            {
                player.StopCoroutine(player.rightHandOpenCoroutine);
                player.rightHandOpenCoroutine = null;
            }
            player.rightHandAnimator.CrossFade(player.closedClip.name, 1f);
            player.rightHandVfx.SendEvent("handClose");
            player.rightHandStateClamped = HandState.Closed;
            player.rightHandStateChangeTime = Time.time;
        }

        if (player.leftHandStateClamped == HandState.Closed)
        {
            player.leftHandSecondaryAttractor.position = player.HandLeft.transform.position;
        }
        else
        {
            player.leftHandSecondaryAttractor.position = player.sphere.position;
        }

        if (player.rightHandStateClamped == HandState.Closed)
        {
            player.rightHandSecondaryAttractor.position = player.HandRight.transform.position;
        }
        else
        {
            player.rightHandSecondaryAttractor.position = player.sphere.position;
        }
    }

    public void ManageHandTrailDistorters(PlayerConstructor player)
    {
        float sphereRadius = player.sphere.transform.localScale.x / 4f;

        // LEFT HAND
        Vector3 leftStart = player.leftHandCollider.position;
        Vector3 directionToLeftHand = (leftStart - player.sphere.position).normalized;
        Vector3 leftEnd = player.sphere.position + directionToLeftHand * sphereRadius;
        int numDistortersLeft = player.leftHandTrailDistorters.Length;
        for (int i = 0; i < numDistortersLeft; i++)
        {
            float t = (i + 1f) / (numDistortersLeft + 1f);
            Vector3 pos = Vector3.Lerp(leftStart, leftEnd, t);

            // Disable BrownianMotion temporarily to set position
            if (player.leftHandTrailDistorters[i].TryGetComponent<Klak.Motion.BrownianMotion>(out var brownianMotion))
            {
                brownianMotion.enabled = false;
            }

            player.leftHandTrailDistorters[i].transform.position = pos;

            // Re-enable BrownianMotion to capture new position as initial position
            if (brownianMotion != null)
            {
                brownianMotion.enabled = true;
            }
        }

        // RIGHT HAND
        Vector3 rightStart = player.rightHandCollider.position;
        Vector3 directionToRightHand = (rightStart - player.sphere.position).normalized;
        Vector3 rightEnd = player.sphere.position + directionToRightHand * sphereRadius;
        int numDistortersRight = player.rightHandTrailDistorters.Length;
        for (int i = 0; i < numDistortersRight; i++)
        {
            float t = (i + 1f) / (numDistortersRight + 1f);
            Vector3 pos = Vector3.Lerp(rightStart, rightEnd, t);

            // Disable BrownianMotion temporarily to set position
            if (player.rightHandTrailDistorters[i].TryGetComponent<Klak.Motion.BrownianMotion>(out var brownianMotion))
            {
                brownianMotion.enabled = false;
            }

            player.rightHandTrailDistorters[i].transform.position = pos;

            // Re-enable BrownianMotion to capture new position as initial position
            if (brownianMotion != null)
            {
                brownianMotion.enabled = true;
            }
        }
    }


}
