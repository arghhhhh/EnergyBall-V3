using System;
using UnityEngine;

[System.Serializable]
public class RuntimeSceneSettings
{
    public event Action OnAnyDebuggingSettingChanged;

    [Header("Gravity Attraction")]
    public float g = 9.81f;
    public float maxTowardsForce = 10f;
    public float maxAwayFromForce = 10f;
    public float gravityForceDamper = 1f;
    public float stopGravityDistance = 0.1f;
    public float stopMovingDistance = 0.05f;
    public float stopVelocity = 0.1f;
    public float attractionRadiusMultiplier = 1f;

    [Header("Hands Attraction")]
    public AnimationCurve forceToMiddle = AnimationCurve.Linear(0, 0, 1, 1);
    public float singleHandOpenForceDamper = 1f;
    public float pushForce = 5f;
    public float minDrag = 0.1f;
    public float maxDrag = 5f;
    public AnimationCurve alignmentVectorStrength = AnimationCurve.Linear(0, 0, 1, 1);
    public float alignmentVectorStrengthScaler = 1f;
    public float handPushScaler = 1f;

    [Header("Intrinsic Pulsation")]
    [Range(0, 10f)]
    public float pulseAmount = 1f;
    public float pulseSpeed = 1f;
    public float graphLimit = 10f;
    public float[] pulseFreqs = new float[] { 1f, 2f, 3f };

    [Header("Movement-Based Pulsation")]
    public bool singleHandScaling = true;
    public float minimumUnscaledSize = 0.5f;
    [Range(0.0001f, 5f)]
    public float minHandDisplacementPerFrame = 0.01f;
    public AnimationCurve distanceDamper = AnimationCurve.Linear(0, 0, 1, 1);
    public float pulseScaleDamper = 1f;

    [Header("Miscellaneous")]
    public float mergeSizeScalerDamper = 1f;
    public float maxDistanceBetweenHands = 2f;
    public float baseZDepth = 5f;
    public float defaultUnscaledSize = 1f;
    public float bodyScale = 1f;
    public float maxDistanceFromCamera = 10f;

    [Header("Animation")]
    public float particleInitializationDelay = 1f;

    [Header("Debugging")]
    public bool dummyOnlyMode = false;
    public bool showSphereMeshOnHandCollision = false;
    
    private bool _showAttractionRadius = false;
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

    private bool _showHandTrailDistorters = false;
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

    private bool _showSecondaryAttractor = false;
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

    public void CopyFromScriptableObject(SceneSettingsSO so)
    {
        g = so.g;
        maxTowardsForce = so.maxTowardsForce;
        maxAwayFromForce = so.maxAwayFromForce;
        gravityForceDamper = so.gravityForceDamper;
        stopGravityDistance = so.stopGravityDistance;
        stopMovingDistance = so.stopMovingDistance;
        stopVelocity = so.stopVelocity;
        attractionRadiusMultiplier = so.attractionRadiusMultiplier;
        forceToMiddle = new AnimationCurve(so.forceToMiddle.keys);
        singleHandOpenForceDamper = so.singleHandOpenForceDamper;
        pushForce = so.pushForce;
        minDrag = so.minDrag;
        maxDrag = so.maxDrag;
        alignmentVectorStrength = new AnimationCurve(so.alignmentVectorStrength.keys);
        alignmentVectorStrengthScaler = so.alignmentVectorStrengthScaler;
        handPushScaler = so.handPushScaler;
        pulseAmount = so.pulseAmount;
        pulseSpeed = so.pulseSpeed;
        graphLimit = so.graphLimit;
        pulseFreqs = (float[])so.pulseFreqs.Clone();
        singleHandScaling = so.singleHandScaling;
        minimumUnscaledSize = so.minimumUnscaledSize;
        minHandDisplacementPerFrame = so.minHandDisplacementPerFrame;
        distanceDamper = new AnimationCurve(so.distanceDamper.keys);
        pulseScaleDamper = so.pulseScaleDamper;
        mergeSizeScalerDamper = so.mergeSizeScalerDamper;
        maxDistanceBetweenHands = so.maxDistanceBetweenHands;
        baseZDepth = so.baseZDepth;
        defaultUnscaledSize = so.defaultUnscaledSize;
        bodyScale = so.bodyScale;
        maxDistanceFromCamera = so.maxDistanceFromCamera;
        particleInitializationDelay = so.particleInitializationDelay;
        dummyOnlyMode = so.dummyOnlyMode;
        showSphereMeshOnHandCollision = so.showSphereMeshOnHandCollision;
        _showAttractionRadius = so.showAttractionRadius;
        _showHandTrailDistorters = so.showHandTrailDistorters;
        _showSecondaryAttractor = so.showSecondaryAttractor;
    }

    public RuntimeSceneSettings DeepCopy()
    {
        var copy = new RuntimeSceneSettings();
        copy.g = g;
        copy.maxTowardsForce = maxTowardsForce;
        copy.maxAwayFromForce = maxAwayFromForce;
        copy.gravityForceDamper = gravityForceDamper;
        copy.stopGravityDistance = stopGravityDistance;
        copy.stopMovingDistance = stopMovingDistance;
        copy.stopVelocity = stopVelocity;
        copy.attractionRadiusMultiplier = attractionRadiusMultiplier;
        copy.forceToMiddle = new AnimationCurve(forceToMiddle.keys);
        copy.singleHandOpenForceDamper = singleHandOpenForceDamper;
        copy.pushForce = pushForce;
        copy.minDrag = minDrag;
        copy.maxDrag = maxDrag;
        copy.alignmentVectorStrength = new AnimationCurve(alignmentVectorStrength.keys);
        copy.alignmentVectorStrengthScaler = alignmentVectorStrengthScaler;
        copy.handPushScaler = handPushScaler;
        copy.pulseAmount = pulseAmount;
        copy.pulseSpeed = pulseSpeed;
        copy.graphLimit = graphLimit;
        copy.pulseFreqs = (float[])pulseFreqs.Clone();
        copy.singleHandScaling = singleHandScaling;
        copy.minimumUnscaledSize = minimumUnscaledSize;
        copy.minHandDisplacementPerFrame = minHandDisplacementPerFrame;
        copy.distanceDamper = new AnimationCurve(distanceDamper.keys);
        copy.pulseScaleDamper = pulseScaleDamper;
        copy.mergeSizeScalerDamper = mergeSizeScalerDamper;
        copy.maxDistanceBetweenHands = maxDistanceBetweenHands;
        copy.baseZDepth = baseZDepth;
        copy.defaultUnscaledSize = defaultUnscaledSize;
        copy.bodyScale = bodyScale;
        copy.maxDistanceFromCamera = maxDistanceFromCamera;
        copy.particleInitializationDelay = particleInitializationDelay;
        copy.dummyOnlyMode = dummyOnlyMode;
        copy.showSphereMeshOnHandCollision = showSphereMeshOnHandCollision;
        copy._showAttractionRadius = _showAttractionRadius;
        copy._showHandTrailDistorters = _showHandTrailDistorters;
        copy._showSecondaryAttractor = _showSecondaryAttractor;
        return copy;
    }
}