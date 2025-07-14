using UnityEngine;
using Windows.Kinect;

public class HandEffects
{
    public void ManageHandEffects(PlayerConstructor player)
    {
        if (player.leftHandState == HandState.Open && player.leftHandStatePrev != HandState.Open)
        {
            float timeSinceStateChange = Time.time - player.leftHandStateChangeTime;
            if (timeSinceStateChange > 2.0f)
            {
                if (player.leftHandOpenCoroutine != null)
                {
                    player.StopCoroutine(player.leftHandOpenCoroutine);
                }
                player.leftHandOpenCoroutine = player.StartCoroutine(player.PlayLeftHandOpenAnimationDelayed());
            } else {
                player.leftHandAnimator.CrossFade(player.openClip.name, 1f);
            }
            player.leftHandVfx.SendEvent("handOpen");
            player.leftHandStatePrev = HandState.Open;
            player.leftHandStateChangeTime = Time.time;
        }
        else if (
            player.leftHandState == HandState.Closed
            && player.leftHandStatePrev != HandState.Closed
        )
        {
            if (player.leftHandOpenCoroutine != null)
            {
                player.StopCoroutine(player.leftHandOpenCoroutine);
                player.leftHandOpenCoroutine = null;
            }
            player.leftHandAnimator.CrossFade(player.closedClip.name, 1f);
            player.leftHandVfx.SendEvent("handClose");
            player.leftHandStatePrev = HandState.Closed;
            player.leftHandStateChangeTime = Time.time;
        }
        if (player.rightHandState == HandState.Open && player.rightHandStatePrev != HandState.Open)
        {
            float timeSinceStateChange = Time.time - player.rightHandStateChangeTime;
            if (timeSinceStateChange > 2.0f)
            {
                if (player.rightHandOpenCoroutine != null)
                {
                    player.StopCoroutine(player.rightHandOpenCoroutine);
                }
                player.rightHandOpenCoroutine = player.StartCoroutine(player.PlayRightHandOpenAnimationDelayed());
            } else {
                player.rightHandAnimator.CrossFade(player.openClip.name, 1f);
            }
            player.rightHandVfx.SendEvent("handOpen");
            player.rightHandStatePrev = HandState.Open;
            player.rightHandStateChangeTime = Time.time;
        }
        else if (
            player.rightHandState == HandState.Closed
            && player.rightHandStatePrev != HandState.Closed
        )
        {
            if (player.rightHandOpenCoroutine != null)
            {
                player.StopCoroutine(player.rightHandOpenCoroutine);
                player.rightHandOpenCoroutine = null;
            }
            player.rightHandAnimator.CrossFade(player.closedClip.name, 1f);
            player.rightHandVfx.SendEvent("handClose");
            player.rightHandStatePrev = HandState.Closed;
            player.rightHandStateChangeTime = Time.time;
        }
    }

    public void ManageHandTrailDistorters(PlayerConstructor player)
    {
        // LEFT HAND
        Vector3 leftStart = player.leftHandCollider.position;
        Vector3 leftEnd = player.sphere.position - player.sphere.transform.forward * player.sphere.transform.localScale.x / 2f;
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
        Vector3 rightEnd = player.sphere.position - player.sphere.transform.forward * player.sphere.transform.localScale.x / 2f;
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
