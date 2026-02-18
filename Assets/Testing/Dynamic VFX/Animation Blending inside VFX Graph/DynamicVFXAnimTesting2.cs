using UnityEngine;
using UnityEngine.VFX;

public class DynamicVFXAnimTesting2 : MonoBehaviour
{
    public VisualEffect vfx; // Assign in Inspector
    public string DirectionParameter = "Direction";
    public string StartTimeParameter = "StartTime";
    public string prevStartTimeParameter = "PrevStartTime";
    public string prevValueParameter = "PrevValue";
    public string currentValueParameter = "CurrentValue";
    public KeyCode animToggleKey = KeyCode.S;

    private bool animDirection = false;
    private float prevStartTime = 0f; // Track the previous start time
    private Vector4 prevValue = Vector4.zero; // Track the previous value

    void Start()
    {
        prevStartTime = vfx.GetFloat(StartTimeParameter);
    }

    void Update()
    {
        if (Input.GetKeyDown(animToggleKey))
        {
            // Store the current start time as the previous start time
            prevStartTime = vfx.GetFloat(StartTimeParameter);
            prevValue = vfx.GetVector4(currentValueParameter);

            animDirection = !animDirection;
            vfx.SetBool(DirectionParameter, animDirection);
            vfx.SetFloat(StartTimeParameter, Time.time);
            vfx.SetVector4(currentValueParameter, new Vector4(1, 2, 3, 4));
            vfx.SetFloat(prevStartTimeParameter, prevStartTime);
            vfx.SetVector4(prevValueParameter, prevValue);
            Debug.Log("AnimOn set to: " + animDirection);
        }
    }
}
