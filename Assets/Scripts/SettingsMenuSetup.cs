using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class SettingsMenuSetup : MonoBehaviour
{
    private void Start()
    {
        var sceneController = FindFirstObjectByType<SceneController>();
        var settingsMenu = GetComponent<InGameSettingsMenu>();
        
        if (settingsMenu != null && sceneController != null)
        {
            // Ensure the settings menu has references it needs
            if (settingsMenu.GetCurrentSettings() == null && sceneController.so != null)
            {
                // Settings menu will initialize from the ScriptableObject in its Start method
            }
            
            // Connect the scene controller to the settings menu
            sceneController.settingsMenu = settingsMenu;
        }
    }
}