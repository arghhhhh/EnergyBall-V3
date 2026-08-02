using UnityEngine;
using Windows.Kinect;

public class HandEffects
{
    public void ManageHandEffects(PlayerConstructor player, RuntimeSceneSettings settings)
    {
        // Capture initialization state before checking
        bool wasInitialized = player.initialized;

        // Check if hands are brought together to initialize the player
        if (!player.initialized)
        {
            float activationDistance = settings?.prayToActivateDistance ?? 0.7f;

            float handDistance = Vector3.Distance(
                player.HandLeft.transform.position,
                player.HandRight.transform.position
            );
            if (handDistance < activationDistance)
            {
                player.initialized = true;
            }
        }

        // Detect if player just initialized this frame
        bool justInitialized = !wasInitialized && player.initialized;

        // // Check if player is in bounds and handle out-of-bounds override
        bool isInBounds = player.IsInbounds();
        bool justWentOutOfBounds = player.wasInBounds && !isInBounds;
        player.wasInBounds = isInBounds;

        player.leftHandVfx.SetBool("isInBounds", isInBounds);
        player.rightHandVfx.SetBool("isInBounds", isInBounds);

        // If player just went out of bounds, force hands to close
        if (justWentOutOfBounds)
        {
            // Force left hand to closed state
            if (player.leftHandStateClamped != HandState.Closed)
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

            // Force right hand to closed state
            if (player.rightHandStateClamped != HandState.Closed)
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

            // Stop metaball radius animation when going out of bounds
            player.StopMetaballRadiusAnimation();
            // Track when both hands became closed (forced by going out of bounds)
            player.bothHandsClosedSinceTime = Time.time;
        }

        // If player is out of bounds, keep hands closed regardless of actual hand state
        if (!isInBounds)
        {
            // Check actual hand states (not the clamped states)
            bool bothHandsClosed =
                player.leftHandState == HandState.Closed
                && player.rightHandState == HandState.Closed;
            bool bothHandsOpen =
                player.leftHandState == HandState.Open && player.rightHandState == HandState.Open;

            if (bothHandsClosed)
            {
                // Increment the timer only when both hands are closed
                player.outOfBoundsWithClosedHandsTimer += Time.deltaTime;

                // If out of bounds with closed hands for more than the configured delay, mark sphere for reset
                if (player.outOfBoundsWithClosedHandsTimer >= settings.outOfBoundsResetDelay)
                {
                    player.pendingSphereReset = true;
                    player.outOfBoundsWithClosedHandsTimer = 0f;
                }
            }
            // When one hand is open, timer pauses (doesn't increment or reset)
            // This allows single open hand to draw sphere in naturally

            // If pending reset and both hands are open, keep sphere centered between hands
            // This runs every frame to track hand movement until the sphere is back in bounds
            if (player.pendingSphereReset && bothHandsOpen)
            {
                player.ResetSphereToHandMidpoint();
            }

            // Keep hands closed - don't process normal hand state logic below
            // Update secondary attractors for closed state
            player.leftHandSecondaryAttractor.position = player.HandLeft.transform.position;
            player.rightHandSecondaryAttractor.position = player.HandRight.transform.position;
            return;
        }

        // Reset the out-of-bounds timer and pending reset flag when back in bounds
        player.outOfBoundsWithClosedHandsTimer = 0f;
        player.pendingSphereReset = false;

        // Normal hand state processing (only when in bounds)
        // IMPORTANT: Process hand CLOSINGS before OPENINGS so that bothHandsClosedSinceTime
        // is updated before we check if animation should play. This handles the case where
        // one hand closes and another opens in the same frame (e.g., switching hands).

        // --- PHASE 1: Process hand closings first ---
        bool leftHandClosing =
            player.leftHandState == HandState.Closed
            && player.leftHandStateClamped != HandState.Closed
            && (player.isDummy || player.initialized);

        bool rightHandClosing =
            player.rightHandState == HandState.Closed
            && player.rightHandStateClamped != HandState.Closed
            && player.initialized;

        if (leftHandClosing)
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

            // Check if both hands are now closed (including if right hand is also closing this frame)
            bool rightHandClosed =
                player.rightHandStateClamped == HandState.Closed
                || player.rightHandState == HandState.Closed;
            if (rightHandClosed)
            {
                player.StopMetaballRadiusAnimation();
                player.bothHandsClosedSinceTime = Time.time;
            }
        }

        if (rightHandClosing)
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

            // Check if both hands are now closed (left hand was already processed above if closing)
            bool leftHandClosed =
                player.leftHandStateClamped == HandState.Closed
                || player.leftHandState == HandState.Closed;
            if (leftHandClosed)
            {
                player.StopMetaballRadiusAnimation();
                player.bothHandsClosedSinceTime = Time.time;
            }
        }

        // --- PHASE 2: Process hand openings (after closings have updated bothHandsClosedSinceTime) ---
        // Calculate if both hands have been closed long enough to trigger animation
        // This check happens AFTER processing closings, so bothHandsClosedSinceTime is current
        float timeSinceBothHandsClosed = Time.time - player.bothHandsClosedSinceTime;
        bool bothHandsClosedLongEnough =
            timeSinceBothHandsClosed > settings.initializationResetDelay || justInitialized;

        if (
            player.leftHandState == HandState.Open
            && player.leftHandStateClamped != HandState.Open
            && (player.isDummy || player.initialized)
        )
        {
            float timeSinceStateChange = Time.time - player.leftHandStateChangeTime;

            if (timeSinceStateChange > settings.initializationResetDelay || justInitialized)
            {
                if (player.leftHandOpenCoroutine != null)
                {
                    player.StopCoroutine(player.leftHandOpenCoroutine);
                }
                player.leftHandOpenCoroutine = player.StartCoroutine(
                    player.PlayLeftHandOpenAnimationDelayed()
                );

                // Start metaball radius animation only if:
                // 1. Both hands have been closed for the delay, AND
                // 2. The OTHER hand is currently closed/not open (we're transitioning FROM both-closed state)
                // This prevents animation when going from one-hand-open to both-open (e.g., O→U or P→U)
                // Note: NotTracked is treated as "not open" for initial player state
                bool rightHandNotOpen = player.rightHandStateClamped != HandState.Open;
                if (bothHandsClosedLongEnough && rightHandNotOpen)
                {
                    player.StartMetaballRadiusAnimation(settings);
                }
            }
            else
            {
                player.leftHandAnimator.CrossFade(player.openClip.name, 1f);
            }
            player.leftHandVfx.SendEvent("handOpen");
            player.leftHandStateClamped = HandState.Open;
            player.leftHandStateChangeTime = Time.time;
        }

        if (
            player.rightHandState == HandState.Open
            && player.rightHandStateClamped != HandState.Open
            && (player.isDummy || player.initialized)
        )
        {
            float timeSinceStateChange = Time.time - player.rightHandStateChangeTime;

            if (timeSinceStateChange > settings.initializationResetDelay || justInitialized)
            {
                if (player.rightHandOpenCoroutine != null)
                {
                    player.StopCoroutine(player.rightHandOpenCoroutine);
                }
                player.rightHandOpenCoroutine = player.StartCoroutine(
                    player.PlayRightHandOpenAnimationDelayed()
                );

                // Start metaball radius animation only if:
                // 1. Both hands have been closed for the delay, AND
                // 2. The OTHER hand is currently closed/not open (we're transitioning FROM both-closed state)
                // This prevents animation when going from one-hand-open to both-open (e.g., O→U or P→U)
                // Note: NotTracked is treated as "not open" for initial player state
                bool leftHandNotOpen = player.leftHandStateClamped != HandState.Open;
                if (bothHandsClosedLongEnough && leftHandNotOpen)
                {
                    player.StartMetaballRadiusAnimation(settings);
                }
            }
            else
            {
                player.rightHandAnimator.CrossFade(player.openClip.name, 1f);
            }
            player.rightHandVfx.SendEvent("handOpen");
            player.rightHandStateClamped = HandState.Open;
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
            if (
                player
                    .leftHandTrailDistorters[i]
                    .TryGetComponent<Klak.Motion.BrownianMotion>(out var brownianMotion)
            )
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
            if (
                player
                    .rightHandTrailDistorters[i]
                    .TryGetComponent<Klak.Motion.BrownianMotion>(out var brownianMotion)
            )
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
