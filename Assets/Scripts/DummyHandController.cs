using System;
using UnityEngine;

public class DummyHandController : MonoBehaviour
{
    public GameObject hand;

    [Tooltip(
        "Per-frame step = speed / 100 world units at bodyScale 1 (multiplied by bodyScale at runtime)."
    )]
    public float speed = 1f;
    float speedDamper;
    public string upKey = "UpArrow";
    public string downKey = "DownArrow";
    public string leftKey = "LeftArrow";
    public string rightKey = "RightArrow";

    KeyCode up;
    KeyCode down;
    KeyCode left;
    KeyCode right;

    void Start()
    {
        speedDamper = speed / 100f;
        SetKeys();
    }

    void FixedUpdate()
    {
        // The step is a per-frame displacement (a length) -> scale with bodyScale so a
        // dummy hand covers the same fraction of the body per frame at every world scale.
        float bodyScale = 1f;
        if (SceneController.Instance != null)
        {
            bodyScale = SceneController.Instance.GetRuntimeSettings().bodyScale;
            if (bodyScale <= 0f)
                bodyScale = 1f;
        }
        float step = speedDamper * bodyScale;

        if (Input.GetKey(up))
        {
            hand.transform.position += step * transform.up;
        }
        if (Input.GetKey(down))
        {
            hand.transform.position += -1f * step * transform.up;
        }
        if (Input.GetKey(left))
        {
            hand.transform.position += -1f * step * transform.right;
        }
        if (Input.GetKey(right))
        {
            hand.transform.position += step * transform.right;
        }
    }

    void SetKeys()
    {
        up = (KeyCode)Enum.Parse(typeof(KeyCode), upKey);
        down = (KeyCode)Enum.Parse(typeof(KeyCode), downKey);
        left = (KeyCode)Enum.Parse(typeof(KeyCode), leftKey);
        right = (KeyCode)Enum.Parse(typeof(KeyCode), rightKey);
    }

    void OnValidate()
    {
        speedDamper = speed / 100f;
    }
}
