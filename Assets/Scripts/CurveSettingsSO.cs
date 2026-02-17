using NaughtyAttributes;
using UnityEngine;

[System.Obsolete("Curves are now managed in SceneController inspector and saved in scene profiles. This SO is kept for reference only.")]
[CreateAssetMenu(fileName = "CurveSettings", menuName = "Settings/Curve Settings")]
public class CurveSettingsSO : ScriptableObject
{
    [BoxGroup("Hands Attraction Curves")]
    [Tooltip("Force curve that controls attraction to the middle point between hands")]
    public AnimationCurve forceToMiddle = AnimationCurve.Linear(0, 0, 1, 1);

    [BoxGroup("Hands Attraction Curves")]
    [Tooltip("Alignment vector strength curve based on hand distance")]
    public AnimationCurve alignmentVectorStrength = AnimationCurve.Linear(0, 0, 1, 1);

    [BoxGroup("Movement-Based Pulsation Curves")]
    [Tooltip("Dampen the ratio between body scale and hand distance based on hand distance relative to maxDistanceBetweenHands")]
    public AnimationCurve distanceDamper = AnimationCurve.Linear(0, 0, 1, 1);
}