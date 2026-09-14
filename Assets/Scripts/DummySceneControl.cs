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

    // Input System keys (names parse from the string fields, e.g. "U").
    Key bothOpen;
    Key bothClosed;
    Key leftOpen;
    Key rightOpen;

    readonly ButtonEdge bothOpenEdge = new();
    readonly ButtonEdge bothClosedEdge = new();
    readonly ButtonEdge leftOpenEdge = new();
    readonly ButtonEdge rightOpenEdge = new();

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
        if (bothOpenEdge.Update(IsPressed(bothOpen)))
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
        if (bothClosedEdge.Update(IsPressed(bothClosed)))
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
        if (leftOpenEdge.Update(IsPressed(leftOpen)))
        {
            player.leftHandState = Windows.Kinect.HandState.Open;
            player.rightHandState = Windows.Kinect.HandState.Closed;
            if (toggleSprites)
            {
                SetSpriteAlpha(leftHandSprite, 1f);
                SetSpriteAlpha(rightHandSprite, closedOpacity);
            }
        }
        if (rightOpenEdge.Update(IsPressed(rightOpen)))
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
        bothOpen = KeyName.Parse(bothOpenKey);
        bothClosed = KeyName.Parse(bothClosedKey);
        leftOpen = KeyName.Parse(leftOpenKey);
        rightOpen = KeyName.Parse(rightOpenKey);
    }

    static bool IsPressed(Key key)
    {
        var keyboard = Keyboard.current;
        return key != Key.None && keyboard != null && keyboard[key].isPressed;
    }

    void SetSpriteAlpha(SpriteRenderer sprite, float alpha)
    {
        if (sprite == null)
            return;
        Color color = sprite.color;
        color.a = alpha;
        sprite.color = color;
    }
}
