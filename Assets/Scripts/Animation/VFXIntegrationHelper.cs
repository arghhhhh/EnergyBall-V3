using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Helper class to bridge between your game events and the VFX Animation Blender
/// This demonstrates how to integrate the animation system with your actual VFX graph
/// </summary>
[RequireComponent(typeof(VFXAnimationBlender))]
[RequireComponent(typeof(VisualEffect))]
public class VFXIntegrationHelper : MonoBehaviour
{
    [Header("References")]
    VFXAnimationBlender animationBlender;
    VisualEffect vfxGraph;

    [Header("VFX Parameters")]
    public string emissionRateProperty = "EmissionRate";
    public string particleLifetimeProperty = "ParticleLifetime";
    public string velocityProperty = "Velocity";

    void Start()
    {
        // Ensure we have references
        if (animationBlender == null)
            animationBlender = GetComponent<VFXAnimationBlender>();

        if (vfxGraph == null)
            vfxGraph = GetComponent<VisualEffect>();
    }

    // Call this from your game logic when hand opens
    public void OnHandOpenEvent()
    {
        if (animationBlender != null)
        {
            animationBlender.TriggerHandOpen();
        }
    }

    // Call this from your game logic when hand closes
    public void OnHandCloseEvent()
    {
        if (animationBlender != null)
        {
            animationBlender.TriggerHandClose();
        }
    }

    // Example of how you might trigger these from other scripts
    void Update()
    {
        // Example: Trigger on specific input (replace with your actual game logic)
        if (Input.GetKeyDown(KeyCode.T))
        {
            OnHandOpenEvent();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            OnHandCloseEvent();
        }
    }
}