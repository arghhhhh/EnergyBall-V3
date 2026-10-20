using UnityEngine;
using Windows.Kinect;

/// <summary>
/// Turns one hand's raw Kinect hand state into the steady Open / Closed state the gameplay
/// reads (<see cref="PlayerConstructor.leftHandState"/> / <c>rightHandState</c>).
///
/// The raw state flickers even while a hand is held still: Unknown, NotTracked and Lasso
/// frames come and go, and single misread Open / Closed frames are common. So:
/// - Only Open and Closed are readings. Unknown, NotTracked and Lasso keep the current state
///   and don't cancel a pending change.
/// - A change needs <c>debounceFrames</c> consecutive readings of the new state.
/// - With <c>highConfidenceOnly</c>, a low-confidence reading counts as no reading.
/// - An open hand with no reading for <c>holdTime</c> seconds counts as closed (0 = hold).
/// Before its first confirmed reading the state is NotTracked.
///
/// Feed it once per new Kinect body frame, not per physics step, or debouncing would count
/// the same frame twice.
/// </summary>
public class HandStateFilter
{
    /// <summary>The filtered state: NotTracked until the first confirmed reading, then Open or Closed.</summary>
    public HandState State { get; private set; } = HandState.NotTracked;

    /// <summary>The last raw state from the Kinect (for debugging).</summary>
    public HandState Raw { get; private set; } = HandState.NotTracked;

    /// <summary>The last raw confidence from the Kinect (for debugging).</summary>
    public TrackingConfidence RawConfidence { get; private set; } = TrackingConfidence.Low;

    HandState pending = HandState.NotTracked;
    int pendingFrames;
    float lastReadingTime;

    public void Update(
        HandState raw,
        TrackingConfidence confidence,
        int debounceFrames,
        bool highConfidenceOnly,
        float holdTime,
        float now
    )
    {
        Raw = raw;
        RawConfidence = confidence;

        bool isReading =
            (raw == HandState.Open || raw == HandState.Closed)
            && (!highConfidenceOnly || confidence == TrackingConfidence.High);

        if (isReading)
        {
            lastReadingTime = now;
            if (raw == State)
            {
                // A reading that agrees with the current state cancels a pending change.
                pendingFrames = 0;
                return;
            }

            pendingFrames = raw == pending ? pendingFrames + 1 : 1;
            pending = raw;
            if (pendingFrames >= Mathf.Max(1, debounceFrames))
            {
                State = raw;
                pendingFrames = 0;
            }
        }
        else if (holdTime > 0f && State == HandState.Open && now - lastReadingTime >= holdTime)
        {
            // Lost the hand for too long: stop steering with it.
            State = HandState.Closed;
            pendingFrames = 0;
        }
    }
}
