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
/// - After <c>holdTime</c> seconds with no reading (0 = hold forever):
///   - a hand reported NotTracked all that time goes back to NotTracked. The Kinect only
///     tracks hand states for the two bodies nearest the sensor; everyone else's hands read
///     NotTracked. Gameplay treats NotTracked as "not closed" (HandForce pulls the ball to the
///     hands), which is how players beyond the first two still steer their ball. Holding a
///     Closed state instead would leave them pushing it away indefinitely.
///   - an open hand that is tracked but unreadable (Unknown / Lasso) counts as closed.
/// Before its first confirmed reading the state is NotTracked.
///
/// Feed it once per new Kinect body frame, not per physics step, or debouncing would count
/// the same frame twice.
/// </summary>
public class HandStateFilter
{
    /// <summary>
    /// The filtered state: Open or Closed, or NotTracked before the first confirmed reading and
    /// while the Kinect isn't tracking the hand.
    /// </summary>
    public HandState State { get; private set; } = HandState.NotTracked;

    /// <summary>The last raw state from the Kinect (for debugging).</summary>
    public HandState Raw { get; private set; } = HandState.NotTracked;

    /// <summary>The last raw confidence from the Kinect (for debugging).</summary>
    public TrackingConfidence RawConfidence { get; private set; } = TrackingConfidence.Low;

    HandState pending = HandState.NotTracked;
    int pendingFrames;
    float lastReadingTime;
    float lastHandTrackedTime;

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
        if (raw != HandState.NotTracked)
            lastHandTrackedTime = now;

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
        else if (holdTime > 0f && now - lastHandTrackedTime >= holdTime)
        {
            // The Kinect isn't tracking this hand (e.g. not one of the two nearest bodies).
            State = HandState.NotTracked;
            pendingFrames = 0;
        }
        else if (holdTime > 0f && State == HandState.Open && now - lastReadingTime >= holdTime)
        {
            // Tracked but unreadable for too long: stop steering with it.
            State = HandState.Closed;
            pendingFrames = 0;
        }
    }
}
