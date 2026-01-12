# Adding New Settings Menu Tabs with Independent Profiles

This guide explains how to add a new tab to the settings menu system that has its own independent profile management, separate from existing Scene and Post-Processing tabs.

## Overview

The settings menu system supports multiple tabs where each tab:
- Has its own profile dropdown, Load/Save/Save As buttons
- Maintains independent profile files in separate directories
- Only saves/loads settings relevant to that tab
- Prevents cross-contamination between different setting types

## Prerequisites

Before adding a new tab, ensure you understand:
- Unity UI Toolkit (UXML/USS)
- C# serialization with `[SerializeField]`
- The existing `RuntimeSceneSettings` class structure
- How `JsonUtility.ToJson()` works with Unity objects
- The SceneController inspector-based settings system

## Step-by-Step Guide

### 1. Update the TabType Enum

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Add your new tab type to the enum:

```csharp
public enum TabType
{
    Scene,
    PostProcessing,
    YourNewTab  // Add this line
}
```

### 2. Add Settings to SceneController Inspector

**File:** `Assets/Scripts/SceneController.cs`

Add your new settings directly to the SceneController for inspector editing. **IMPORTANT:** Follow the existing patterns:

```csharp
[Header("Your New Settings Category")]
[BoxGroup("Your New Settings")]
public float yourNewFloatSetting = 1.0f;
[BoxGroup("Your New Settings")]
public bool yourNewBoolSetting = false;
[BoxGroup("Your New Settings")]
[Range(0, 10f)]
public float yourRangedSetting = 5.0f;
```

### 3. Add Settings Properties to RuntimeSceneSettings

**File:** `Assets/Scripts/RuntimeSceneSettings.cs`

Add corresponding properties in RuntimeSceneSettings. **IMPORTANT:** Follow the serialization patterns:

```csharp
[Header("Your New Settings Category")]
// For simple properties, use public fields:
public float yourNewFloatSetting = 1.0f;
public bool yourNewBoolSetting = false;

// For properties with change notifications, use private fields with [SerializeField]:
[SerializeField] private float _yourComplexSetting = 0.5f;
public float yourComplexSetting
{
    get => _yourComplexSetting;
    set
    {
        if (_yourComplexSetting != value)
        {
            _yourComplexSetting = value;
            OnAnyDebuggingSettingChanged?.Invoke(); // Trigger change notifications
        }
    }
}
```

**⚠️ Critical:** Properties with private backing fields MUST have `[SerializeField]` on the private field, or they won't be saved to JSON.

### 4. Update SceneController Copy Methods

**File:** `Assets/Scripts/SceneController.cs`

Update the `CopyInspectorToRuntime()` and `CopyRuntimeToInspector()` methods to handle your new settings:

```csharp
// In CopyInspectorToRuntime() method, add:
target.yourNewFloatSetting = yourNewFloatSetting;
target.yourNewBoolSetting = yourNewBoolSetting;
target.yourRangedSetting = yourRangedSetting;

// In CopyRuntimeToInspector() method, add:
yourNewFloatSetting = source.yourNewFloatSetting;
yourNewBoolSetting = source.yourNewBoolSetting;
yourRangedSetting = source.yourRangedSetting;
```

### 5. Update UXML Structure

**File:** `Assets/UI/SettingsMenu.uxml`

Add your new tab button and content:

```xml
<ui:VisualElement name="TabContainer" class="tab-container">
    <ui:Button text="Scene" name="SceneTab" class="tab-button active" />
    <ui:Button text="Post Processing" name="PostProcessingTab" class="tab-button" />
    <ui:Button text="Your New Tab" name="YourNewTabTab" class="tab-button" />
</ui:VisualElement>

<ui:VisualElement name="TabContent" class="tab-content">
    <!-- Existing tabs... -->
    
    <ui:VisualElement name="YourNewTabContent" class="tab-panel">
        <ui:VisualElement name="YourNewTabProfileControls" class="profile-controls">
            <ui:DropdownField name="YourNewTabProfileDropdown" style="flex-grow: 1;" />
            <ui:Button text="Load" name="YourNewTabLoadButton" class="profile-button" />
            <ui:Button text="Save" name="YourNewTabSaveButton" class="profile-button" />
            <ui:Button text="Save As..." name="YourNewTabSaveAsButton" class="profile-button" />
        </ui:VisualElement>
        <ui:ScrollView name="YourNewTabPanel" class="settings-scroll">
            <!-- Settings will be populated dynamically -->
        </ui:ScrollView>
    </ui:VisualElement>
</ui:VisualElement>
```

### 6. Add Private Fields to InGameSettingsMenu

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Add UI element references and directory paths:

```csharp
private DropdownField sceneProfileDropdown, postProcessingProfileDropdown, yourNewTabProfileDropdown;
private Button sceneLoadButton, sceneSaveButton, sceneSaveAsButton;
private Button postProcessingLoadButton, postProcessingSaveButton, postProcessingSaveAsButton;
private Button yourNewTabLoadButton, yourNewTabSaveButton, yourNewTabSaveAsButton; // Add these
private Button closeButton;
private Button sceneTab, postProcessingTab, yourNewTabTab; // Add this
private ScrollView sceneSettingsPanel;
private ScrollView postProcessingPanel;
private ScrollView yourNewTabPanel; // Add this

// Directory paths
private string sceneProfilesDirectory;
private string postProcessingProfilesDirectory;
private string yourNewTabProfilesDirectory; // Add this

// Profile paths
private string currentSceneProfilePath = "";
private string currentPostProcessingProfilePath = "";
private string currentYourNewTabProfilePath = ""; // Add this

// PlayerPrefs keys (now scene-specific)
private string lastUsedSceneProfileKey = "LastUsedSceneProfile";
private string lastUsedPostProcessingProfileKey = "LastUsedPostProcessingProfile";
private string lastUsedYourNewTabProfileKey = "LastUsedYourNewTabProfile"; // Add this
```

### 7. Initialize Directory in Awake()

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

```csharp
void Awake()
{
    // Existing directory creation...

    yourNewTabProfilesDirectory = Path.Combine(Application.streamingAssetsPath, "SettingsProfiles", "YourNewTab");
    if (!Directory.Exists(yourNewTabProfilesDirectory))
    {
        Directory.CreateDirectory(yourNewTabProfilesDirectory);
    }

    // Initialize scene-specific keys early (this automatically handles per-scene persistence)
    InitializeSceneSpecificKeysFromController();
}
```

### 8. Update SetupUI() Method

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Add UI element queries and event handlers:

```csharp
private void SetupUI()
{
    // Existing code...
    
    // Your new tab controls
    var yourNewTabContent = root.Q<VisualElement>("YourNewTabContent");
    if (yourNewTabContent != null)
    {
        yourNewTabProfileDropdown = yourNewTabContent.Q<DropdownField>("YourNewTabProfileDropdown");
        yourNewTabLoadButton = yourNewTabContent.Q<Button>("YourNewTabLoadButton");
        yourNewTabSaveButton = yourNewTabContent.Q<Button>("YourNewTabSaveButton");
        yourNewTabSaveAsButton = yourNewTabContent.Q<Button>("YourNewTabSaveAsButton");
    }
    
    yourNewTabPanel = root.Q<ScrollView>("YourNewTabPanel");
    yourNewTabTab = root.Q<Button>("YourNewTabTab");
    
    // Event handlers
    if (yourNewTabLoadButton != null) yourNewTabLoadButton.clicked += () => LoadSelectedProfile("yournewTab");
    if (yourNewTabSaveButton != null) yourNewTabSaveButton.clicked += () => SaveCurrentProfile(TabType.YourNewTab);
    if (yourNewTabSaveAsButton != null) yourNewTabSaveAsButton.clicked += () => ShowSaveAsDialog(TabType.YourNewTab);
    
    yourNewTabTab.clicked += () => SwitchTab("yournewTab");
    
    // Dropdown change callback
    if (yourNewTabProfileDropdown != null)
    {
        yourNewTabProfileDropdown.RegisterValueChangedCallback(evt => {
            if (!string.IsNullOrEmpty(evt.newValue))
            {
                LoadSelectedProfile("yournewTab");
            }
        });
    }
}
```

### 9. Update Start() Method

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Add refresh call for your new tab:

```csharp
private void Start()
{
    InitializeRuntimeSettings();
    SetupUI();
    RefreshSceneProfiles();
    RefreshPostProcessingProfiles();
    RefreshYourNewTabProfiles(); // Add this line
    CreateSettingsUI();
}
```

### 10. Create Settings UI Method

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Add a method to create your tab's UI content:

```csharp
private void CreateYourNewTabContent()
{
    CreateYourNewTabGroup(yourNewTabPanel);
}

private void CreateYourNewTabGroup(ScrollView parentContainer)
{
    var group = CreateGroup("Your New Settings", parentContainer);
    
    // Add your settings using existing helper methods:
    CreateFloatField(group, "Your Float Setting", () => runtimeSettings.yourNewFloatSetting, v => runtimeSettings.yourNewFloatSetting = v);
    CreateToggleField(group, "Your Bool Setting", () => runtimeSettings.yourNewBoolSetting, v => runtimeSettings.yourNewBoolSetting = v);
    CreateFloatField(group, "Your Complex Setting", () => runtimeSettings.yourComplexSetting, v => runtimeSettings.yourComplexSetting = v);
}
```

Update `CreateSettingsUI()`:

```csharp
private void CreateSettingsUI()
{
    // Existing null checks...
    
    sceneSettingsPanel.Clear();
    postProcessingPanel.Clear();
    yourNewTabPanel.Clear(); // Add this
    settingElements.Clear();
    
    // Scene Settings Tab
    CreateSceneSettingsContent();
    
    // Post Processing Tab
    CreatePostProcessingContent();
    
    // Your New Tab
    CreateYourNewTabContent(); // Add this
}
```

### 11. Create Profile Management Methods

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Add the refresh and copy methods for your tab:

```csharp
private void RefreshYourNewTabProfiles()
{
    if (yourNewTabProfileDropdown == null) return;

    // Ensure we're using scene-specific keys
    EnsureSceneSpecificKeys();

    var profileFiles = Directory.GetFiles(yourNewTabProfilesDirectory, "*.json")
        .Select(Path.GetFileNameWithoutExtension)
        .ToList();

    yourNewTabProfileDropdown.choices = profileFiles;

    // Skip loading profile if refresh is suppressed (during save operations)
    if (isRefreshingSuppressed) return;

    // Try to restore last used profile for this specific scene
    string lastUsedProfile = PlayerPrefs.GetString(lastUsedYourNewTabProfileKey, "");
    if (!string.IsNullOrEmpty(lastUsedProfile) && profileFiles.Contains(lastUsedProfile))
    {
        yourNewTabProfileDropdown.SetValueWithoutNotify(lastUsedProfile);
        LoadProfile(Path.Combine(yourNewTabProfilesDirectory, lastUsedProfile + ".json"), ProfileType.YourNewTab);
    }
    else if (profileFiles.Count > 0)
    {
        // If no scene-specific profile exists, use the first available but don't save it as preference yet
        yourNewTabProfileDropdown.SetValueWithoutNotify(profileFiles[0]);
        LoadProfile(Path.Combine(yourNewTabProfilesDirectory, profileFiles[0] + ".json"), ProfileType.YourNewTab);
    }
}

private void CopyYourNewTabSettings(RuntimeSceneSettings source, RuntimeSceneSettings destination)
{
    // Copy only your tab's settings to destination
    destination.yourNewFloatSetting = source.yourNewFloatSetting;
    destination.yourNewBoolSetting = source.yourNewBoolSetting;
    destination.yourComplexSetting = source.yourComplexSetting;
    
    // Explicitly set ALL other settings to defaults to prevent contamination
    // Scene settings:
    destination.g = 0.0f;
    destination.maxTowardsForce = 0.0f;
    // ... (copy all the scene setting defaults from CopyPostProcessingSettings)
    
    // Post-processing settings:
    destination.bloomThreshold = 0.0f;
    destination.bloomIntensity = 0.0f;
    // ... (copy all the post-processing setting defaults from CopySceneSettings)
}

private void MergeYourNewTabSettings(RuntimeSceneSettings loadedSettings)
{
    // Copy only your tab's settings from loaded profile
    // Keep all other settings intact
    runtimeSettings.yourNewFloatSetting = loadedSettings.yourNewFloatSetting;
    runtimeSettings.yourNewBoolSetting = loadedSettings.yourNewBoolSetting;
    runtimeSettings.yourComplexSetting = loadedSettings.yourComplexSetting;
}
```

### 12. Update Existing Methods

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Update several methods to handle your new tab:

#### Update RefreshProfileDropdowns():
```csharp
private void RefreshProfileDropdowns()
{
    RefreshSceneProfiles();
    RefreshPostProcessingProfiles();
    RefreshYourNewTabProfiles(); // Add this
}
```

#### Update LoadSelectedProfile():
```csharp
private void LoadSelectedProfile(string tabType)
{
    if (tabType == "scene")
    {
        if (sceneProfileDropdown == null || string.IsNullOrEmpty(sceneProfileDropdown.value)) return;
        var profilePath = Path.Combine(sceneProfilesDirectory, sceneProfileDropdown.value + ".json");
        LoadProfile(profilePath, TabType.Scene);
    }
    else if (tabType == "postprocessing")
    {
        if (postProcessingProfileDropdown == null || string.IsNullOrEmpty(postProcessingProfileDropdown.value)) return;
        var profilePath = Path.Combine(postProcessingProfilesDirectory, postProcessingProfileDropdown.value + ".json");
        LoadProfile(profilePath, TabType.PostProcessing);
    }
    else if (tabType == "yournewTab") // Add this block
    {
        if (yourNewTabProfileDropdown == null || string.IsNullOrEmpty(yourNewTabProfileDropdown.value)) return;
        var profilePath = Path.Combine(yourNewTabProfilesDirectory, yourNewTabProfileDropdown.value + ".json");
        LoadProfile(profilePath, TabType.YourNewTab);
    }
}
```

#### Update LoadProfile():
```csharp
private void LoadProfile(string path, ProfileType profileType)
{
    // Existing code...
    
    // Add this block:
    else if (profileType == ProfileType.YourNewTab)
    {
        // Load only your tab's settings, keep current other settings
        MergeYourNewTabSettings(loadedSettings);
        currentYourNewTabProfilePath = path;
        
        // Save as last used profile
        string profileName = Path.GetFileNameWithoutExtension(path);
        PlayerPrefs.SetString(lastUsedYourNewTabProfileKey, profileName);
        PlayerPrefs.Save();
        
        // Add any special handling for your tab here
        // (like VolumeController.OnProfileSaved for post-processing)
    }
    
    // Existing RefreshUI() and OnSettingsChanged calls...
}
```

#### Update SaveProfile():
```csharp
private void SaveProfile(string path, TabType tabType)
{
    try
    {
        RuntimeSceneSettings settingsToSave;
        
        if (tabType == TabType.Scene)
        {
            // Existing scene code...
        }
        else if (tabType == TabType.PostProcessing)
        {
            // Existing post-processing code...
        }
        else if (tabType == TabType.YourNewTab) // Add this block
        {
            // Create a settings object with only your tab's data
            settingsToSave = new RuntimeSceneSettings();
            CopyYourNewTabSettings(runtimeSettings, settingsToSave);
            currentYourNewTabProfilePath = path;
            
            // Add any special save handling for your tab here
        }
        
        // Existing JSON serialization code...
    }
    catch (Exception e)
    {
        Debug.LogError($"Failed to save {tabType} profile: {e.Message}");
    }
}
```

#### Update SaveAsNewProfile():
```csharp
private void SaveAsNewProfile(string profileName, TabType tabType)
{
    string profilePath;
    string lastUsedKey;
    
    if (tabType == TabType.Scene)
    {
        profilePath = Path.Combine(sceneProfilesDirectory, profileName + ".json");
        lastUsedKey = lastUsedSceneProfileKey;
    }
    else if (tabType == TabType.PostProcessing)
    {
        profilePath = Path.Combine(postProcessingProfilesDirectory, profileName + ".json");
        lastUsedKey = lastUsedPostProcessingProfileKey;
    }
    else if (tabType == TabType.YourNewTab) // Add this block
    {
        profilePath = Path.Combine(yourNewTabProfilesDirectory, profileName + ".json");
        lastUsedKey = lastUsedYourNewTabProfileKey;
    }
    
    SaveProfile(profilePath, tabType);
    
    // Suppress profile loading during dropdown refresh
    isRefreshingSuppressed = true;
    RefreshProfileDropdowns();
    isRefreshingSuppressed = false;
    
    // Set dropdown value without triggering the callback
    if (tabType == TabType.Scene)
    {
        sceneProfileDropdown.SetValueWithoutNotify(profileName);
    }
    else if (tabType == TabType.PostProcessing)
    {
        postProcessingProfileDropdown.SetValueWithoutNotify(profileName);
    }
    else if (tabType == TabType.YourNewTab) // Add this block
    {
        yourNewTabProfileDropdown.SetValueWithoutNotify(profileName);
    }
    
    // Save as last used profile for this tab
    PlayerPrefs.SetString(lastUsedKey, profileName);
    PlayerPrefs.Save();
}
```

#### Update SaveCurrentProfile():
```csharp
private void SaveCurrentProfile(TabType tabType)
{
    if (tabType == TabType.Scene)
    {
        if (string.IsNullOrEmpty(currentSceneProfilePath))
        {
            ShowSaveAsDialog(tabType);
            return;
        }
        SaveProfile(currentSceneProfilePath, tabType);
    }
    else if (tabType == TabType.PostProcessing)
    {
        if (string.IsNullOrEmpty(currentPostProcessingProfilePath))
        {
            ShowSaveAsDialog(tabType);
            return;
        }
        SaveProfile(currentPostProcessingProfilePath, tabType);
    }
    else if (tabType == TabType.YourNewTab) // Add this block
    {
        if (string.IsNullOrEmpty(currentYourNewTabProfilePath))
        {
            ShowSaveAsDialog(tabType);
            return;
        }
        SaveProfile(currentYourNewTabProfilePath, tabType);
    }
}
```

#### Update SwitchTab():
```csharp
private void SwitchTab(string tabName)
{
    var root = uiDocument.rootVisualElement;
    var sceneTabContent = root.Q<VisualElement>("SceneTabContent");
    var postProcessingTabContent = root.Q<VisualElement>("PostProcessingTabContent");
    var yourNewTabContent = root.Q<VisualElement>("YourNewTabContent"); // Add this
    
    if (tabName == "scene")
    {
        sceneTab.AddToClassList("active");
        postProcessingTab.RemoveFromClassList("active");
        yourNewTabTab.RemoveFromClassList("active"); // Add this
        sceneTabContent?.AddToClassList("active");
        postProcessingTabContent?.RemoveFromClassList("active");
        yourNewTabContent?.RemoveFromClassList("active"); // Add this
    }
    else if (tabName == "postprocessing")
    {
        sceneTab.RemoveFromClassList("active");
        postProcessingTab.AddToClassList("active");
        yourNewTabTab.RemoveFromClassList("active"); // Add this
        sceneTabContent?.RemoveFromClassList("active");
        postProcessingTabContent?.AddToClassList("active");
        yourNewTabContent?.RemoveFromClassList("active"); // Add this
    }
    else if (tabName == "yournewTab") // Add this block
    {
        sceneTab.RemoveFromClassList("active");
        postProcessingTab.RemoveFromClassList("active");
        yourNewTabTab.AddToClassList("active");
        sceneTabContent?.RemoveFromClassList("active");
        postProcessingTabContent?.RemoveFromClassList("active");
        yourNewTabContent?.AddToClassList("active");
    }
}
```

### 13. Create Default Profile

Create a default profile file for your new tab:

**File:** `Assets/StreamingAssets/SettingsProfiles/YourNewTab/Default.json`

```json
{
    "yourNewFloatSetting": 1.0,
    "yourNewBoolSetting": false,
    "yourComplexSetting": 0.5
}
```

Make sure to only include settings relevant to your tab and set all other values to zero/defaults.

### 14. Update RuntimeSceneSettings Methods

**File:** `Assets/Scripts/RuntimeSceneSettings.cs`

Update the `DeepCopy()` method to handle your new settings:

```csharp
public RuntimeSceneSettings DeepCopy()
{
    // Existing code...

    // Add your new settings:
    copy.yourNewFloatSetting = yourNewFloatSetting;
    copy.yourNewBoolSetting = yourNewBoolSetting;
    copy._yourComplexSetting = _yourComplexSetting;

    return copy;
}
```

**Note:** The old `CopyFromScriptableObject()` method is now deprecated since settings are managed directly in the SceneController inspector.

## Common Pitfalls to Avoid

1. **Serialization Issues**: Always use `[SerializeField]` on private fields used by properties
2. **Profile Contamination**: Always explicitly zero out other tabs' settings in Copy methods
3. **Callback Loops**: Use `SetValueWithoutNotify()` when programmatically setting dropdown values
4. **Missing Refresh**: Don't forget to call your refresh method in `Start()` and `RefreshProfileDropdowns()`
5. **Tab Switching**: Update all tab visibility states in `SwitchTab()` method
6. **Directory Creation**: Initialize your profile directory in `Awake()`
7. **Scene-Specific Keys**: Call `EnsureSceneSpecificKeys()` in your refresh methods for per-scene persistence
8. **Inspector Sync**: Update both `CopyInspectorToRuntime()` and `CopyRuntimeToInspector()` methods in SceneController

## Testing Checklist

After implementing your new tab:

- [ ] Tab button appears and switches correctly
- [ ] Profile dropdown shows available profiles
- [ ] Settings UI appears with your custom controls
- [ ] Changing settings updates values in real-time
- [ ] Save button saves current settings to profile
- [ ] Save As creates new profile with current settings
- [ ] Load button loads selected profile
- [ ] Switching profiles via dropdown loads correctly
- [ ] Settings persist between game sessions (per-scene)
- [ ] Other tabs' settings are not affected
- [ ] Profile files only contain relevant settings
- [ ] Inspector values sync with runtime settings menu
- [ ] Per-scene profile persistence works (different scenes remember different profiles)
- [ ] Settings changes in inspector immediately update runtime menu

## File Structure

Your new tab will create this file structure:
```
Assets/
├── StreamingAssets/
│   └── SettingsProfiles/
│       ├── Scene/
│       ├── PostProcessing/
│       └── YourNewTab/          <- New directory
│           └── Default.json     <- New default profile
├── Scripts/
│   ├── SceneController.cs       <- Updated (inspector settings + sync methods)
│   ├── InGameSettingsMenu.cs    <- Updated (UI + profile management)
│   └── RuntimeSceneSettings.cs  <- Updated (properties)
└── UI/
    └── SettingsMenu.uxml        <- Updated (tab UI)
```

## New System Features

This updated guide reflects the new architecture:

- **Inspector-Based Settings**: All settings are directly editable in the SceneController inspector
- **Bidirectional Sync**: Inspector ↔ Runtime settings menu synchronization
- **Per-Scene Persistence**: Each scene remembers its own profile selections independently
- **No ScriptableObjects**: Settings management moved away from ScriptableObject dependencies

This guide ensures your new tab follows the same patterns and architecture as the existing Scene and Post-Processing tabs, maintaining consistency and preventing common issues.