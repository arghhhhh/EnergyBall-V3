using NaughtyAttributes;
using UnityEngine;

public enum Axis
{
    X,
    Z,
}

/// <summary>
/// Places a dummy's hands (and sphere) from values authored at bodyScale = 1.
/// At runtime the lateral offsets are multiplied by the effective bodyScale and the
/// depth axis is set to the effective <c>baseZDepth</c> — the same treatment Kinect
/// joints get in <c>SceneController.GetVector3FromJoint</c>. OnValidate previews the
/// unscaled layout in the editor.
/// </summary>
public class DummyTransformer : MonoBehaviour
{
    [Foldout("Transforms")]
    public Transform leftHand = null;

    [Foldout("Transforms")]
    public Transform rightHand = null;

    [Foldout("Transforms")]
    public Transform sphere = null;

    [Tooltip(
        "Hand-pair centre at bodyScale 1 (multiplied by bodyScale at runtime; the depth axis is replaced by baseZDepth)."
    )]
    public Vector3 positionOffset = new(0, 0, 0);

    [Tooltip("Distance between the hands at bodyScale 1 (multiplied by bodyScale at runtime).")]
    public float spaceBetweenHands = 1.5f;
    public Axis selectedAxis;

    // Last effective bodyScale / baseZDepth the hands were laid out for.
    private float appliedBodyScale = 1f;
    private float appliedDepth = 0f;
    private bool laidOut = false;

    void Start()
    {
        var controller = SceneController.Instance;
        if (leftHand == null || rightHand == null || controller == null)
            return;

        var runtimeSettings = controller.GetRuntimeSettings(); // effective
        appliedBodyScale = SafeScale(runtimeSettings.bodyScale);
        appliedDepth = runtimeSettings.baseZDepth;
        Layout(appliedBodyScale, appliedDepth, useDepth: true);
        laidOut = true;
    }

    void Update()
    {
        // When bodyScale (or baseZDepth) changes at runtime, rescale the hands' CURRENT
        // positions proportionally - like Kinect joints, which are joint x bodyScale - instead of
        // snapping them back to the authored layout. Allocation-free check every frame.
        if (!laidOut || SceneController.Instance == null)
            return;
        var runtimeSettings = SceneController.Instance.GetRuntimeSettings();
        float bodyScale = SafeScale(runtimeSettings.bodyScale);
        float depth = runtimeSettings.baseZDepth;
        if (
            Mathf.Approximately(bodyScale, appliedBodyScale)
            && Mathf.Approximately(depth, appliedDepth)
        )
            return;

        float ratio = bodyScale / appliedBodyScale;
        leftHand.localPosition = Rescale(leftHand.localPosition, ratio, depth);
        rightHand.localPosition = Rescale(rightHand.localPosition, ratio, depth);
        appliedBodyScale = bodyScale;
        appliedDepth = depth;
    }

    private Vector3 Rescale(Vector3 p, float ratio, float depth)
    {
        return selectedAxis == Axis.X
            ? new Vector3(p.x * ratio, p.y * ratio, depth)
            : new Vector3(depth, p.y * ratio, p.z * ratio);
    }

    private static float SafeScale(float s) => s > 0f ? s : 1f;

    void OnValidate()
    {
        if (leftHand != null && rightHand != null && sphere != null)
        {
            Layout(1f, 0f, useDepth: false);
        }
    }

    private void Layout(float bodyScale, float depth, bool useDepth)
    {
        Vector3 centre = positionOffset * bodyScale;
        float half = spaceBetweenHands * bodyScale / 2f;

        if (selectedAxis == Axis.X)
        {
            float z = useDepth ? depth : centre.z;
            leftHand.localPosition = new Vector3(centre.x - half, centre.y, z);
            rightHand.localPosition = new Vector3(centre.x + half, centre.y, z);
            if (sphere != null)
                sphere.localPosition = new Vector3(centre.x, centre.y, z);
        }
        else
        {
            float x = useDepth ? depth : centre.x;
            leftHand.localPosition = new Vector3(x, centre.y, centre.z - half);
            rightHand.localPosition = new Vector3(x, centre.y, centre.z + half);
            if (sphere != null)
                sphere.localPosition = new Vector3(x, centre.y, centre.z);
        }
    }
}
