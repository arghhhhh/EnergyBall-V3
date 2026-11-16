using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;

public class DummySceneControl : MonoBehaviour
{
    private PlayerConstructor player;
    public string bothOpenKey = "U";
    public string bothClosedKey = "I";
    public string leftOpenKey = "O";
    public string rightOpenKey = "P";
    public bool toggleSprites = false;
    public SpriteRenderer leftHandSprite;
    public SpriteRenderer rightHandSprite;

    Key bothOpen;
    Key bothClosed;
    Key leftOpen;
    Key rightOpen;

    private float closedOpacity = 0.35f;

    void Start()
    {
        player = GetComponent<PlayerConstructor>();
        SetKeys();
        if (toggleSprites)
        {
            SetSpriteAlpha(
                leftHandSprite,
                player.leftHandState == Windows.Kinect.HandState.Open ? 1f : 0.5f
            );
            SetSpriteAlpha(
                rightHandSprite,
                player.rightHandState == Windows.Kinect.HandState.Open ? 1f : 0.5f
            );
        }
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (WasPressed(keyboard, bothOpen))
        {
            player.leftHandState = Windows.Kinect.HandState.Open;
            player.rightHandState = Windows.Kinect.HandState.Open;
            if (toggleSprites)
            {
                SetSpriteAlpha(leftHandSprite, 1f);
                SetSpriteAlpha(rightHandSprite, 1f);
            }
            Debug.Log("Both open");
        }
        if (WasPressed(keyboard, bothClosed))
        {
            player.leftHandState = Windows.Kinect.HandState.Closed;
            player.rightHandState = Windows.Kinect.HandState.Closed;
            if (toggleSprites)
            {
                SetSpriteAlpha(leftHandSprite, closedOpacity);
                SetSpriteAlpha(rightHandSprite, closedOpacity);
            }
            Debug.Log("Both closed");
        }
        if (WasPressed(keyboard, leftOpen))
        {
            player.leftHandState = Windows.Kinect.HandState.Open;
            player.rightHandState = Windows.Kinect.HandState.Closed;
            if (toggleSprites)
            {
                SetSpriteAlpha(leftHandSprite, 1f);
                SetSpriteAlpha(rightHandSprite, closedOpacity);
            }
        }
        if (WasPressed(keyboard, rightOpen))
        {
            player.leftHandState = Windows.Kinect.HandState.Closed;
            player.rightHandState = Windows.Kinect.HandState.Open;
            if (toggleSprites)
            {
                SetSpriteAlpha(leftHandSprite, closedOpacity);
                SetSpriteAlpha(rightHandSprite, 1f);
            }
        }
    }

    void SetKeys()
    {
        bothOpen = ParseKey(bothOpenKey);
        bothClosed = ParseKey(bothClosedKey);
        leftOpen = ParseKey(leftOpenKey);
        rightOpen = ParseKey(rightOpenKey);
    }

    // Key names follow UnityEngine.InputSystem.Key (letters, "UpArrow", "Space", ...).
    Key ParseKey(string keyName)
    {
        if (Enum.TryParse(keyName, true, out Key key) && key != Key.None)
            return key;
        Debug.LogWarning($"{name}: unknown key \"{keyName}\"", this);
        return Key.None;
    }

    static bool WasPressed(Keyboard keyboard, Key key) =>
        key != Key.None && keyboard[key].wasPressedThisFrame;

    void SetSpriteAlpha(SpriteRenderer sprite, float alpha)
    {
        if (sprite == null)
            return;
        Color color = sprite.color;
        color.a = alpha;
        sprite.color = color;
    }
}
