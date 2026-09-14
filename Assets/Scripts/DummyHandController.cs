using System;
using UnityEngine;
using UnityEngine.InputSystem;

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

    // Input System keys (names parse from the string fields, e.g. "UpArrow").
    Key up;
    Key down;
    Key left;
    Key right;

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

        if (IsPressed(up))
        {
            hand.transform.position += step * transform.up;
        }
        if (IsPressed(down))
        {
            hand.transform.position += -1f * step * transform.up;
        }
        if (IsPressed(left))
        {
            hand.transform.position += -1f * step * transform.right;
        }
        if (IsPressed(right))
        {
            hand.transform.position += step * transform.right;
        }
    }

    void SetKeys()
    {
        up = KeyName.Parse(upKey);
        down = KeyName.Parse(downKey);
        left = KeyName.Parse(leftKey);
        right = KeyName.Parse(rightKey);
    }

    static bool IsPressed(Key key)
    {
        var keyboard = Keyboard.current;
        return key != Key.None && keyboard != null && keyboard[key].isPressed;
    }

    void OnValidate()
    {
        speedDamper = speed / 100f;
    }
}
