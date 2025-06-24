using NaughtyAttributes;
using UnityEngine;

public enum Axis
{
    X,
    Z
}

public class DummyTransformer : MonoBehaviour
{
    SceneController controller = null;

    [Foldout("Transforms")]
    public Transform leftHand = null;

    [Foldout("Transforms")]
    public Transform rightHand = null;

    [Foldout("Transforms")]
    public Transform sphere = null;
    public Vector3 positionOffset = new(0, 0, 0);
    public float spaceBetweenHands = 1.5f;
    public Axis selectedAxis;

    void Start()
    {
        controller = GetComponent<SceneController>();
        if (leftHand != null && rightHand != null && controller != null)
        {
            if (selectedAxis == Axis.X)
            {
                leftHand.localPosition = new Vector3(
                    leftHand.localPosition.x,
                    leftHand.localPosition.y,
                    controller.so.baseZDepth
                );
                rightHand.localPosition = new Vector3(
                    rightHand.localPosition.x,
                    rightHand.localPosition.y,
                    controller.so.baseZDepth
                );
            }
            else
            {
                leftHand.localPosition = new Vector3(
                    controller.so.baseZDepth,
                    leftHand.localPosition.y,
                    leftHand.localPosition.z
                );
                rightHand.localPosition = new Vector3(
                    controller.so.baseZDepth,
                    rightHand.localPosition.y,
                    rightHand.localPosition.z
                );
            }
        }
    }

    void OnValidate()
    {
        if (leftHand != null && rightHand != null && sphere != null)
        {
            if (selectedAxis == Axis.X)
            {
                leftHand.localPosition = new Vector3(
                    positionOffset.x - (spaceBetweenHands / 2),
                    positionOffset.y,
                    positionOffset.z
                );
                rightHand.localPosition = new Vector3(
                    positionOffset.x + (spaceBetweenHands / 2),
                    positionOffset.y,
                    positionOffset.z
                );
            }
            else
            {
                leftHand.localPosition = new Vector3(
                    positionOffset.x,
                    positionOffset.y,
                    positionOffset.z - (spaceBetweenHands / 2)
                );
                rightHand.localPosition = new Vector3(
                    positionOffset.x,
                    positionOffset.y,
                    positionOffset.z + (spaceBetweenHands / 2)
                );
            }
            sphere.localPosition = new Vector3(
                positionOffset.x,
                positionOffset.y,
                positionOffset.z
            );
        }
    }
}
