using System;
using UnityEngine;
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

    KeyCode bothOpen;
    KeyCode bothClosed;
    KeyCode leftOpen;
    KeyCode rightOpen;

    private float closedOpacity = 0.35f;

    void Start()
    {
        player = GetComponent<PlayerConstructor>();
        SetKeys();
        if (toggleSprites)
        {
            SetSpriteAlpha(leftHandSprite, player.leftHandState == Windows.Kinect.HandState.Open ? 1f : 0.5f);
            SetSpriteAlpha(rightHandSprite, player.rightHandState == Windows.Kinect.HandState.Open ? 1f : 0.5f);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(bothOpen))
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
        if (Input.GetKeyDown(bothClosed))
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
        if (Input.GetKeyDown(leftOpen))
        {
            player.leftHandState = Windows.Kinect.HandState.Open;
            player.rightHandState = Windows.Kinect.HandState.Closed;
            if (toggleSprites)
            {
                SetSpriteAlpha(leftHandSprite, 1f);
                SetSpriteAlpha(rightHandSprite, closedOpacity);
            }
        }
        if (Input.GetKeyDown(rightOpen))
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
        bothOpen = (KeyCode)Enum.Parse(typeof(KeyCode), bothOpenKey);
        bothClosed = (KeyCode)Enum.Parse(typeof(KeyCode), bothClosedKey);
        leftOpen = (KeyCode)Enum.Parse(typeof(KeyCode), leftOpenKey);
        rightOpen = (KeyCode)Enum.Parse(typeof(KeyCode), rightOpenKey);
    }

    void SetSpriteAlpha(SpriteRenderer sprite, float alpha)
    {
        if (sprite == null) return;
        Color color = sprite.color;
        color.a = alpha;
        sprite.color = color;
    }
}
