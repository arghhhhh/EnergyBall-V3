using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;

public class DynamicVFXAnimTesting1 : MonoBehaviour
{
    public VisualEffect vfx; // Assign in Inspector
    public string spawnParameter = "SpawnOn";
    public string spawnAnimStartTimeParameter = "SpawnAnimStartTime";
    public Key spawnToggleKey = Key.S;

    private bool spawnAnimDirection = false;

    public string moveParameter = "MoveToEnd";
    public string moveAnimStartTimeParameter = "MoveAnimStartTime";
    public Key moveToggleKey = Key.M;

    private bool moveAnimDirection = false;

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard[spawnToggleKey].wasPressedThisFrame)
        {
            spawnAnimDirection = !spawnAnimDirection;
            vfx.SetBool(spawnParameter, spawnAnimDirection);
            vfx.SetFloat(spawnAnimStartTimeParameter, Time.time);
            Debug.Log("SpawnOn set to: " + spawnAnimDirection);
        }
        if (keyboard[moveToggleKey].wasPressedThisFrame)
        {
            moveAnimDirection = !moveAnimDirection;
            vfx.SetBool(moveParameter, moveAnimDirection);
            vfx.SetFloat(moveAnimStartTimeParameter, Time.time);
            Debug.Log("MoveToEnd set to: " + moveAnimDirection);
        }
    }
}
