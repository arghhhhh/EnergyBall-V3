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

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (IsHeld(keyboard, up))
        {
            hand.transform.position += step * transform.up;
        }
        if (IsHeld(keyboard, down))
        {
            hand.transform.position += -1f * step * transform.up;
        }
        if (IsHeld(keyboard, left))
        {
            hand.transform.position += -1f * step * transform.right;
        }
        if (IsHeld(keyboard, right))
        {
            hand.transform.position += step * transform.right;
        }
    }

    void SetKeys()
    {
        up = ParseKey(upKey);
        down = ParseKey(downKey);
        left = ParseKey(leftKey);
        right = ParseKey(rightKey);
    }

    // Key names follow UnityEngine.InputSystem.Key (letters, "UpArrow", "Space", ...).
    Key ParseKey(string keyName)
    {
        if (Enum.TryParse(keyName, true, out Key key) && key != Key.None)
            return key;
        Debug.LogWarning($"{name}: unknown key \"{keyName}\"", this);
        return Key.None;
    }

    static bool IsHeld(Keyboard keyboard, Key key) => key != Key.None && keyboard[key].isPressed;

    void OnValidate()
    {
        speedDamper = speed / 100f;
    }
}
