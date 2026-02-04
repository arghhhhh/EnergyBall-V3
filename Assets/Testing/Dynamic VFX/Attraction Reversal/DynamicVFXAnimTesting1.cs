using UnityEngine;
using UnityEngine.VFX;

public class DynamicVFXAnimTesting1 : MonoBehaviour
{
    public VisualEffect vfx; // Assign in Inspector
    public string spawnParameter = "SpawnOn";
    public string spawnAnimStartTimeParameter = "SpawnAnimStartTime";
    public KeyCode spawnToggleKey = KeyCode.S;

    private bool spawnAnimDirection = false;

    public string moveParameter = "MoveToEnd";
    public string moveAnimStartTimeParameter = "MoveAnimStartTime";
    public KeyCode moveToggleKey = KeyCode.M;

    private bool moveAnimDirection = false;

    void Update()
    {
        if (Input.GetKeyDown(spawnToggleKey))
        {
            spawnAnimDirection = !spawnAnimDirection;
            vfx.SetBool(spawnParameter, spawnAnimDirection);
            vfx.SetFloat(spawnAnimStartTimeParameter, Time.time);
            Debug.Log("SpawnOn set to: " + spawnAnimDirection);
        }
        if (Input.GetKeyDown(moveToggleKey))
        {
            moveAnimDirection = !moveAnimDirection;
            vfx.SetBool(moveParameter, moveAnimDirection);
            vfx.SetFloat(moveAnimStartTimeParameter, Time.time);
            Debug.Log("MoveToEnd set to: " + moveAnimDirection);
        }
    }
}
