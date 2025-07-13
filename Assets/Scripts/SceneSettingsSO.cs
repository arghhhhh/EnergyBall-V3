using NaughtyAttributes;
using UnityEngine;
using System;

[CreateAssetMenu]
public class SceneSettingsSO : ScriptableObject
{
    // Single event for when any debugging setting changes
    public event Action OnAnyDebuggingSettingChanged;

    [BoxGroup("Gravity Attraction")]
    public float g;

    [BoxGroup("Gravity Attraction")]
    public float maxTowardsForce;

    [BoxGroup("Gravity Attraction")]
    public float maxAwayFromForce;

    [BoxGroup("Gravity Attraction")]
    public float gravityForceDamper;

    [BoxGroup("Gravity Attraction")]
    public float stopGravityDistance;

    [BoxGroup("Gravity Attraction")]
    public float stopMovingDistance;

    [BoxGroup("Gravity Attraction")]
    public float stopVelocity;

    [BoxGroup("Gravity Attraction")]
    public float attractionRadiusMultiplier;

    [BoxGroup("Hands Attraction")]
    public AnimationCurve forceToMiddle;

    [BoxGroup("Hands Attraction")]
    public float singleHandOpenForceDamper;

    [BoxGroup("Hands Attraction")]
    public float pushForce;

    [BoxGroup("Hands Attraction")]
    public float minDrag;

    [BoxGroup("Hands Attraction")]
    public float maxDrag;

    [BoxGroup("Hands Attraction")]
    public AnimationCurve alignmentVectorStrength;

    [BoxGroup("Hands Attraction")]
    public float alignmentVectorStrengthScaler;

    [BoxGroup("Hands Attraction")]
    public float handPushScaler;

    [BoxGroup("Intrinsic Pulsation")]
    [Range(0, 10f)]
    public float pulseAmount;

    [BoxGroup("Intrinsic Pulsation")]
    public float pulseSpeed;

    [BoxGroup("Intrinsic Pulsation")]
    public float graphLimit;

    [BoxGroup("Intrinsic Pulsation")]
    public float[] pulseFreqs;

    [BoxGroup("Movement-Based Pulsation")]
    [Tooltip("Allow scaling to occur with only one hand's velocity.")]
    public bool singleHandScaling;

    [BoxGroup("Movement-Based Pulsation")]
    [Tooltip("The minimum size that the body can scale down to.")]
    public float minimumUnscaledSize;

    [BoxGroup("Movement-Based Pulsation")]
    [Range(0.0001f, 5f)]
    [Tooltip(
        "Used to mask false velocity readings due to position jitter from inaccurate sensor readings."
    )]
    public float minHandDisplacementPerFrame;

    [BoxGroup("Movement-Based Pulsation")]
    [Tooltip(
        "Dampen the ratio between body scale and hand distance based on hand distance relative to maxDistanceBetweenHands."
    )]
    public AnimationCurve distanceDamper;

    [BoxGroup("Movement-Based Pulsation")]
    [Tooltip("An overall damper for the movement-based pulsation scaling.")]
    public float pulseScaleDamper;

    [BoxGroup("Miscellaneous")]
    [Tooltip("A damper for the scaling that occurs when multiple bodies merge together.")]
    public float mergeSizeScalerDamper;

    [BoxGroup("Miscellaneous")]
    public float maxDistanceBetweenHands;
    [BoxGroup("Miscellaneous")]
    public float baseZDepth;

    [BoxGroup("Miscellaneous")]
    public float defaultUnscaledSize;

    [BoxGroup("Miscellaneous")]
    public float bodyScale;

    [BoxGroup("Miscellaneous")]
    public float maxDistanceFromCamera;

    [BoxGroup("Animation")]
    [Tooltip(
        "The amount of time it takes for the particle initialization animation to play once a new player is added to the scene."
    )]
    public float particleInitializationDelay;

    [BoxGroup("Debugging")]
    public bool dummyOnlyMode;

    [BoxGroup("Debugging")]
    public bool showSphereMeshOnHandCollision;

    [BoxGroup("Debugging")]
    [SerializeField]
    private bool _showAttractionRadius;
    public bool showAttractionRadius
    {
        get => _showAttractionRadius;
        set
        {
            if (_showAttractionRadius != value)
            {
                _showAttractionRadius = value;
                OnAnyDebuggingSettingChanged?.Invoke();
            }
        }
    }

    [BoxGroup("Debugging")]
    [SerializeField]
    private bool _showHandTrailDistorters;
    public bool showHandTrailDistorters
    {
        get => _showHandTrailDistorters;
        set
        {
            if (_showHandTrailDistorters != value)
            {
                _showHandTrailDistorters = value;
                OnAnyDebuggingSettingChanged?.Invoke();
            }
        }
    }

    [BoxGroup("Debugging")]
    [SerializeField]
    private bool _showSecondaryAttractor;
    public bool showSecondaryAttractor
    {
        get => _showSecondaryAttractor;
        set
        {
            if (_showSecondaryAttractor != value)
            {
                _showSecondaryAttractor = value;
                OnAnyDebuggingSettingChanged?.Invoke();
            }
        }
    }

    public void TriggerDebugSettingsUpdate()
    {
        OnAnyDebuggingSettingChanged?.Invoke();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            OnAnyDebuggingSettingChanged?.Invoke();
        }
    }
#endif
}
