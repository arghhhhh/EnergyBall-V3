using System;
using System.Collections;
using System.Collections.Generic;
using Assets.Scripts;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.VFX;
using Windows.Kinect;

[DefaultExecutionOrder(100)]
public class PlayerConstructor : MonoBehaviour
{
    [HideInInspector]
    public ulong userId;
    public int metaballIndex;

    private SceneController controller = null;
    public Rigidbody sphere;
    public SpriteRenderer radiusSprite;

    [HideInInspector]
    public bool pushParticles = false;

    [Foldout("Animations")]
    public AnimationClip emptyClip;

    [Foldout("Animations")]
    public AnimationClip openClip;

    [Foldout("Animations")]
    public AnimationClip closedClip;

    [HideInInspector]
    public bool turnOnParticles = false;

    [HideInInspector]
    public bool beginInitialization = false;

    [HideInInspector]
    public bool initialized = false;

    [HideInInspector]
    public float attractionRadius = 1f;

    [Range(0.001f, 1)]
    public float attractionRadiusScaler = 1f;

    [HideInInspector]
    public Dictionary<PlayerConstructor, float> orbitalBodies = new();

    [HideInInspector]
    public Vector3 unscaledSize = Vector3.one;

    [HideInInspector]
    public Vector3 intrinsicPulseSize = Vector3.zero;
    float pulseOffset;

    [HideInInspector]
    public Vector3 pulseSizeFromMotion = Vector3.zero;

    [HideInInspector]
    public Vector3 prevVelocity = Vector3.zero;

    [HideInInspector]
    public float prevAcceleration = 0f;

    #region hands
    [Foldout("Left Hand")]
    [HideInInspector]
    public Vector3 leftHandPrevPosition = Vector3.zero;

    [Foldout("Left Hand")]
    public Transform leftHandCollider;

    [Foldout("Left Hand")]
    public GameObject[] leftHandTrailDistorters = new GameObject[2];

    [Foldout("Left Hand")]
    [HideInInspector]
    public HandState leftHandState;

    [Foldout("Left Hand")]
    [HideInInspector]
    public HandState leftHandStateClamped = HandState.NotTracked;

    [Foldout("Left Hand")]
    public Transform leftHandSecondaryAttractor;

    [Foldout("Left Hand")]
    public VisualEffect leftHandVfx;

    [Foldout("Left Hand")]
    public Animator leftHandAnimator;

    [Foldout("Left Hand")]
    public float leftHandStateChangeTime = 0f;

    [Foldout("Left Hand")]
    [HideInInspector]
    public Coroutine leftHandOpenCoroutine = null;

    [Foldout("Right Hand")]
    [HideInInspector]
    public Vector3 rightHandPrevPosition = Vector3.zero;

    [Foldout("Right Hand")]
    public Transform rightHandCollider;

    [Foldout("Right Hand")]
    public GameObject[] rightHandTrailDistorters = new GameObject[2];

    [Foldout("Right Hand")]
    public Transform rightHandSecondaryAttractor;

    [Foldout("Right Hand")]
    [HideInInspector]
    public HandState rightHandState;

    [Foldout("Right Hand")]
    [HideInInspector]
    public HandState rightHandStateClamped = HandState.NotTracked;

    [Foldout("Right Hand")]
    public float rightHandStateChangeTime = 0f;

    [Foldout("Right Hand")]
    [HideInInspector]
    public Coroutine rightHandOpenCoroutine = null;

    [Foldout("Right Hand")]
    public VisualEffect rightHandVfx;

    [Foldout("Right Hand")]
    public Animator rightHandAnimator;
    #endregion

    #region joints
    [Foldout("Joints")]
    public GameObject SpineBase;

    [Foldout("Joints")]
    public GameObject SpineMid;

    [Foldout("Joints")]
    public GameObject Neck;

    [Foldout("Joints")]
    public GameObject Head;

    [Foldout("Joints")]
    public GameObject ShoulderLeft;

    [Foldout("Joints")]
    public GameObject ElbowLeft;

    [Foldout("Joints")]
    public GameObject WristLeft;

    [Foldout("Joints")]
    public GameObject HandLeft;

    [Foldout("Joints")]
    public GameObject ShoulderRight;

    [Foldout("Joints")]
    public GameObject ElbowRight;

    [Foldout("Joints")]
    public GameObject WristRight;

    [Foldout("Joints")]
    public GameObject HandRight;

    [Foldout("Joints")]
    public GameObject HipLeft;

    [Foldout("Joints")]
    public GameObject KneeLeft;

    [Foldout("Joints")]
    public GameObject AnkleLeft;

    [Foldout("Joints")]
    public GameObject FootLeft;

    [Foldout("Joints")]
    public GameObject HipRight;

    [Foldout("Joints")]
    public GameObject KneeRight;

    [Foldout("Joints")]
    public GameObject AnkleRight;

    [Foldout("Joints")]
    public GameObject FootRight;

    [Foldout("Joints")]
    public GameObject SpineShoulder;

    [Foldout("Joints")]
    public GameObject HandtipLeft;

    [Foldout("Joints")]
    public GameObject ThumbLeft;

    [Foldout("Joints")]
    public GameObject HandtipRight;

    [Foldout("Joints")]
    public GameObject ThumbRight;
    #endregion
    public Dictionary<JointType, GameObject> jointMap;

    [HideInInspector]
    public Color skeletonColor;
    public bool isDummy = false;

    [HideInInspector]
    public bool wasInBounds = true;

    [HideInInspector]
    public float outOfBoundsWithClosedHandsTimer = 0f;

    [HideInInspector]
    public bool pendingSphereReset = false;

    // Metaball radius animation - NonSerialized to prevent Unity from persisting stale animation state
    [System.NonSerialized]
    public bool metaballRadiusAnimating = false;

    [System.NonSerialized]
    public float metaballRadiusAnimationStartTime = 0f;

    // Tracks the radius at the moment animation was interrupted, for smooth transitions
    [System.NonSerialized]
    public float metaballRadiusAtAnimationStart = 0f;

    // Tracks when both hands became closed - used to determine if animation should play
    // Animation only plays if BOTH hands have been closed for initializationResetDelay
    // (not just one hand). This prevents animation when quickly switching which hand is open.
    [System.NonSerialized]
    public float bothHandsClosedSinceTime = 0f;

    // Tracks single-hand-open state for momentum-preserving final push when both hands close.
    // When transitioning from single-hand-open to both-hands-closed, the "push target" should
    // be the last single open hand's position (not the midpoint) to preserve momentum direction.
    public enum SingleOpenHand
    {
        None,
        Left,
        Right,
    }

    [System.NonSerialized]
    public SingleOpenHand lastSingleOpenHand = SingleOpenHand.None;

    [System.NonSerialized]
    public float singleHandOpenStartTime = 0f;

    // Tracks when we exited single-hand-open state, used for smooth force damper transition
    [System.NonSerialized]
    public float singleHandOpenEndTime = 0f;

    /// <summary>
    /// Returns true if exactly one hand is open (using clamped states).
    /// </summary>
    public bool IsSingleHandOpen =>
        (leftHandStateClamped == HandState.Open && rightHandStateClamped == HandState.Closed)
        || (leftHandStateClamped == HandState.Closed && rightHandStateClamped == HandState.Open);

    /// <summary>
    /// Updates the tracking of which single hand is open and when it started.
    /// Should be called every frame from SceneController.UpdateOtherPlayerData.
    /// </summary>
    public void UpdateSingleHandOpenTracking()
    {
        bool leftOpen = leftHandState == HandState.Open || leftHandStateClamped == HandState.Open;
        bool rightOpen =
            rightHandState == HandState.Open || rightHandStateClamped == HandState.Open;

        // Determine current single-hand state
        SingleOpenHand currentSingleHand = SingleOpenHand.None;
        if (leftOpen && !rightOpen)
        {
            currentSingleHand = SingleOpenHand.Left;
        }
        else if (rightOpen && !leftOpen)
        {
            currentSingleHand = SingleOpenHand.Right;
        }

        // Track if we were in single-hand-open state last frame
        bool wasInSingleHandOpen = lastSingleOpenHand != SingleOpenHand.None;

        // Update tracking based on state changes
        if (currentSingleHand != SingleOpenHand.None)
        {
            // We're in single-hand-open state
            if (lastSingleOpenHand != currentSingleHand)
            {
                // Just entered this single-hand state (or switched hands)
                lastSingleOpenHand = currentSingleHand;
                singleHandOpenStartTime = Time.time;
            }
            // If same hand, keep the existing start time
        }
        else if (leftOpen && rightOpen)
        {
            // Both hands are open - record when we left single-hand-open state
            // for smooth force damper transition
            if (wasInSingleHandOpen)
            {
                singleHandOpenEndTime = Time.time;
            }
            // Reset tracking so that closing both hands from this state
            // uses the midpoint, not a stale single-hand position
            lastSingleOpenHand = SingleOpenHand.None;
        }
        // When both hands are closed, we preserve lastSingleOpenHand
        // so CalculatePushTarget can use it for the final momentum-preserving push
    }

    private void Awake()
    {
        jointMap = new Dictionary<JointType, GameObject>()
        {
            { JointType.SpineBase, SpineBase },
            { JointType.SpineMid, SpineMid },
            { JointType.Neck, Neck },
            { JointType.Head, Head },
            { JointType.ShoulderLeft, ShoulderLeft },
            { JointType.ElbowLeft, ElbowLeft },
            { JointType.WristLeft, WristLeft },
            { JointType.HandLeft, HandLeft },
            { JointType.ShoulderRight, ShoulderRight },
            { JointType.ElbowRight, ElbowRight },
            { JointType.WristRight, WristRight },
            { JointType.HandRight, HandRight },
            { JointType.HipLeft, HipLeft },
            { JointType.KneeLeft, KneeLeft },
            { JointType.AnkleLeft, AnkleLeft },
            { JointType.FootLeft, FootLeft },
            { JointType.HipRight, HipRight },
            { JointType.KneeRight, KneeRight },
            { JointType.AnkleRight, AnkleRight },
            { JointType.FootRight, FootRight },
            { JointType.SpineShoulder, SpineShoulder },
            { JointType.HandTipLeft, HandtipLeft },
            { JointType.ThumbLeft, ThumbLeft },
            { JointType.HandTipRight, HandtipRight },
            { JointType.ThumbRight, ThumbRight },
        };
    }

    private void Start()
    {
        controller = SceneController.Instance;
        var runtimeSettings = controller.GetRuntimeSettings();
        unscaledSize = new Vector3(
            runtimeSettings.defaultUnscaledSize,
            runtimeSettings.defaultUnscaledSize,
            runtimeSettings.defaultUnscaledSize
        );
        pulseOffset = UnityEngine.Random.Range(0, 10) / 10f + UnityEngine.Random.Range(0, 10);
        radiusSprite.enabled = false;

        // Set initialized based on prayToActivate setting
        // If prayToActivate is true, player starts uninitialized and must bring hands together
        // If prayToActivate is false, player starts initialized
        initialized = !runtimeSettings.prayToActivate;

        if (isDummy)
        {
            Actions.OnDummyAdded?.Invoke(this);
        }
    }

    public IEnumerator PlayLeftHandOpenAnimationDelayed()
    {
        var runtimeSettings = controller.GetRuntimeSettings();
        // Play open animation at specified speed to simulate initialization
        leftHandAnimator.SetFloat("OpenSpeed", runtimeSettings.initializationSpeed);
        leftHandAnimator.CrossFade(openClip.name, 1f);
        yield return new WaitForSeconds(openClip.length / runtimeSettings.initializationSpeed);

        // Return to normal speed
        leftHandAnimator.SetFloat("OpenSpeed", 1f);

        if (leftHandState == HandState.Open)
        {
            leftHandAnimator.CrossFade(openClip.name, 1f);
        }

        leftHandOpenCoroutine = null;
    }

    public IEnumerator PlayRightHandOpenAnimationDelayed()
    {
        var runtimeSettings = controller.GetRuntimeSettings();
        // Play open animation at specified speed to simulate initialization
        rightHandAnimator.SetFloat("OpenSpeed", runtimeSettings.initializationSpeed);
        rightHandAnimator.CrossFade(openClip.name, 1f);
        yield return new WaitForSeconds(openClip.length / runtimeSettings.initializationSpeed);

        // Return to normal speed
        rightHandAnimator.SetFloat("OpenSpeed", 1f);

        if (rightHandState == HandState.Open)
        {
            rightHandAnimator.CrossFade(openClip.name, 1f);
        }

        rightHandOpenCoroutine = null;
    }

    public void SetAttractionRadius()
    {
        var runtimeSettings = controller.GetRuntimeSettings();
        attractionRadius =
            Utils.GetVector3Avg(sphere.transform.localScale)
            * runtimeSettings.attractionRadiusMultiplier
            * attractionRadiusScaler
            / 2f; // divide by two since it's the radius

        if (runtimeSettings.showAttractionRadius)
        {
            // set size of radius sprite
            radiusSprite.transform.localScale = new Vector3(
                runtimeSettings.attractionRadiusMultiplier * 0.4f * attractionRadiusScaler,
                runtimeSettings.attractionRadiusMultiplier * 0.4f * attractionRadiusScaler,
                runtimeSettings.attractionRadiusMultiplier * 0.4f * attractionRadiusScaler
            );
        }
    }

    public void SetMass()
    {
        sphere.mass = Utils.GetVector3Avg(sphere.transform.localScale);
    }

    public void SetPulseSize()
    {
        var runtimeSettings = controller.GetRuntimeSettings();
        if (runtimeSettings.pulseAmount == 0)
        {
            intrinsicPulseSize = Vector3.zero;
        }
        else
        {
            float playerPulseAmt =
                Utils.GetVector3Avg(unscaledSize) * runtimeSettings.pulseAmount / 10f;
            // float playerPulseSpeed = acceleration * pulseSpeed / 100f;
            float pulseFunc = 0f;
            // use desmos graphing calculator to find graph bounds
            float playerLimit = runtimeSettings.graphLimit * playerPulseAmt;
            for (int i = 0; i < runtimeSettings.pulseFreqs.Length; i++)
            {
                // y=sin(2x)+sin(0.8x)+sin(0.3x)...
                pulseFunc += Mathf.Sin(
                    runtimeSettings.pulseFreqs[i]
                        * runtimeSettings.pulseSpeed
                        * (Time.fixedTime + pulseOffset)
                );
            }

            float simplePulseSize = playerPulseAmt * pulseFunc;
            float remappedSize = Utils.Remap(
                simplePulseSize,
                -playerLimit,
                playerLimit,
                0f,
                playerPulseAmt
            );

            intrinsicPulseSize = Utils.FloatToVector3(remappedSize);
        }
    }

    public void SetScale()
    {
        var runtimeSettings = controller.GetRuntimeSettings();
        if (runtimeSettings.mergeSizeScalerDamper != 0)
        {
            Vector3 sizeScaler = Vector3.zero;

            foreach (KeyValuePair<PlayerConstructor, float> orbitalBody in orbitalBodies)
            {
                // float startScaleDistance = Utils.GetVector3Avg(unscaledSize+orbitalBody.Key.unscaledSize) / 2f;
                float startScaleDistance =
                    Utils.GetVector3Avg(
                        unscaledSize
                            + intrinsicPulseSize
                            + orbitalBody.Key.unscaledSize
                            + orbitalBody.Key.intrinsicPulseSize
                    ) / 2f;
                float t = Mathf.InverseLerp(startScaleDistance, 0f, orbitalBody.Value);
                sizeScaler +=
                    (orbitalBody.Key.unscaledSize + orbitalBody.Key.intrinsicPulseSize) * t;
            }

            sizeScaler *= runtimeSettings.mergeSizeScalerDamper;
            sphere.transform.localScale = unscaledSize + intrinsicPulseSize + sizeScaler;

            // we only get the unscaled size when there are no orbiting bodies
            if (orbitalBodies.Count == 0)
            {
                // remove pulseSize when calculating true unscaledSize
                unscaledSize = sphere.transform.localScale - intrinsicPulseSize;
            }
        }
        else
        {
            sphere.transform.localScale = unscaledSize + intrinsicPulseSize;
        }
    }

    public bool IsInbounds()
    {
        return IsInbounds(sphere.transform.position);
    }

    public bool IsInbounds(Vector3 position)
    {
        Vector3 bounds = controller.GetGridSize() / 2f;
        var runtimeSettings = controller.GetRuntimeSettings();

        Vector3 gridMin = new(-bounds.x, -bounds.y, -bounds.z + runtimeSettings.baseZDepth);
        Vector3 gridMax = new(bounds.x, bounds.y, bounds.z + runtimeSettings.baseZDepth);

        return position.x >= gridMin.x
            && position.x <= gridMax.x
            && position.y >= gridMin.y
            && position.y <= gridMax.y
            && position.z >= gridMin.z
            && position.z <= gridMax.z;
    }

    public Vector3 GetClampedMetaballPosition()
    {
        Vector3 spherePos = sphere.transform.position;

        // If in bounds, return the actual sphere position
        if (IsInbounds())
        {
            return spherePos;
        }

        // Get grid boundaries
        Vector3 bounds = controller.GetGridSize() / 2f;
        var runtimeSettings = controller.GetRuntimeSettings();

        // Adjust bounds for Z-depth offset
        Vector3 gridMin = new(-bounds.x, -bounds.y, -bounds.z + runtimeSettings.baseZDepth);
        Vector3 gridMax = new(bounds.x, bounds.y, bounds.z + runtimeSettings.baseZDepth);

        // Simple axis-aligned clamping: clamp each axis independently to the boundary.
        // This preserves the sphere's position on axes that are in bounds while clamping
        // only the out-of-bounds axes. BoundaryForce handles slowing the sphere when it
        // exceeds the boundary, so complex ray-intersection logic is no longer needed.
        return new Vector3(
            Mathf.Clamp(spherePos.x, gridMin.x, gridMax.x),
            Mathf.Clamp(spherePos.y, gridMin.y, gridMax.y),
            Mathf.Clamp(spherePos.z, gridMin.z, gridMax.z)
        );
    }

    public void ResetSphereToHandMidpoint()
    {
        Vector3 handMidpoint = (HandLeft.transform.position + HandRight.transform.position) / 2f;
        sphere.transform.position =
            handMidpoint
            + new Vector3(
                UnityEngine.Random.Range(-0.5f, 0.5f),
                UnityEngine.Random.Range(-0.5f, 0.5f),
                UnityEngine.Random.Range(-0.5f, 0.5f)
            );
        sphere.linearVelocity = Vector3.zero;
        sphere.angularVelocity = Vector3.zero;
    }

    #region Metaball Radius Animation
    /// <summary>
    /// Starts the metaball radius animation from a small size to the current sphere scale.
    /// If an animation is already in progress, this restarts it from the current animated radius
    /// for a smooth transition.
    /// </summary>
    public void StartMetaballRadiusAnimation(RuntimeSceneSettings settings)
    {
        // If already animating, capture current animated radius for smooth transition
        if (metaballRadiusAnimating)
        {
            metaballRadiusAtAnimationStart = GetCurrentAnimatedRadius(settings);
        }
        else
        {
            metaballRadiusAtAnimationStart = settings.metaballRadiusAnimationStartSize;
        }

        metaballRadiusAnimating = true;
        metaballRadiusAnimationStartTime = Time.time;
    }

    /// <summary>
    /// Stops the metaball radius animation. Call this when hands close or player goes out of bounds.
    /// </summary>
    public void StopMetaballRadiusAnimation()
    {
        metaballRadiusAnimating = false;
    }

    /// <summary>
    /// Gets the current animated radius value without modifying state.
    /// Used internally when restarting an animation mid-progress for smooth transitions.
    /// </summary>
    private float GetCurrentAnimatedRadius(RuntimeSceneSettings settings)
    {
        if (!metaballRadiusAnimating)
        {
            return sphere.transform.localScale.x;
        }

        float elapsed = Time.time - metaballRadiusAnimationStartTime;
        float t = Mathf.Clamp01(elapsed / settings.metaballRadiusAnimationDuration);
        // Apply animation curve to remap linear t to curved progression
        float curvedT = settings.metaballRadiusAnimationCurve.Evaluate(t);

        return Mathf.Lerp(metaballRadiusAtAnimationStart, sphere.transform.localScale.x, curvedT);
    }

    /// <summary>
    /// Calculates and returns the metaball radius, applying animation if active.
    /// This handles the full animation lifecycle including ending the animation when complete.
    /// </summary>
    /// <param name="settings">The runtime settings containing animation parameters.</param>
    /// <returns>The radius to use for the metaball.</returns>
    public float GetMetaballRadius(RuntimeSceneSettings settings)
    {
        if (!metaballRadiusAnimating)
        {
            return sphere.transform.localScale.x;
        }

        float elapsed = Time.time - metaballRadiusAnimationStartTime;
        float t = Mathf.Clamp01(elapsed / settings.metaballRadiusAnimationDuration);
        // Apply animation curve to remap linear t to curved progression
        float curvedT = settings.metaballRadiusAnimationCurve.Evaluate(t);

        float radius = Mathf.Lerp(
            metaballRadiusAtAnimationStart,
            sphere.transform.localScale.x,
            curvedT
        );

        // End animation when complete (use linear t for timing, not curved)
        if (t >= 1f)
        {
            metaballRadiusAnimating = false;
        }

        return radius;
    }
    #endregion

    private void OnDisable()
    {
        if (isDummy)
        {
            Actions.OnDummyRemoved?.Invoke(userId);
            return;
        }
        // Actions.OnPlayerRemoved?.Invoke(userId);
    }
}
