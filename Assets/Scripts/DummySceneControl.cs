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

    KeyCode bothOpen;
    KeyCode bothClosed;
    KeyCode leftOpen;
    KeyCode rightOpen;

    void Start()
    {
        player = GetComponent<PlayerConstructor>();
        SetKeys();
    }

    void Update()
    {
        if (Input.GetKeyDown(bothOpen))
        {
            player.leftHandState = Windows.Kinect.HandState.Open;
            player.rightHandState = Windows.Kinect.HandState.Open;
            Debug.Log("Both open");
        }
        if (Input.GetKeyDown(bothClosed))
        {
            player.leftHandState = Windows.Kinect.HandState.Closed;
            player.rightHandState = Windows.Kinect.HandState.Closed;
            Debug.Log("Both closed");
        }
        if (Input.GetKeyDown(leftOpen))
        {
            player.leftHandState = Windows.Kinect.HandState.Open;
            player.rightHandState = Windows.Kinect.HandState.Closed;
        }
        if (Input.GetKeyDown(rightOpen))
        {
            player.leftHandState = Windows.Kinect.HandState.Closed;
            player.rightHandState = Windows.Kinect.HandState.Open;
        }
    }

    void SetKeys()
    {
        bothOpen = (KeyCode)Enum.Parse(typeof(KeyCode), bothOpenKey);
        bothClosed = (KeyCode)Enum.Parse(typeof(KeyCode), bothClosedKey);
        leftOpen = (KeyCode)Enum.Parse(typeof(KeyCode), leftOpenKey);
        rightOpen = (KeyCode)Enum.Parse(typeof(KeyCode), rightOpenKey);
    }
}
