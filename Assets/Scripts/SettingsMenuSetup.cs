using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class SettingsMenuSetup : MonoBehaviour
{
    private void Start()
    {
        var sceneController = FindFirstObjectByType<SceneController>();
        var settingsMenu = GetComponent<InGameSettingsMenu>();
        var volumeController = FindFirstObjectByType<VolumeController>();

        if (settingsMenu != null && sceneController != null)
        {
            // Connect the scene controller to the settings menu and volume controller
            sceneController.settingsMenu = settingsMenu;
            if (volumeController != null)
            {
                sceneController.volumeController = volumeController;
            }
        }
    }
}