# Adding New Settings to the In-Game Settings Menu

This guide explains how to add new settings fields to an existing tab in the in-game settings menu. For adding entirely new tabs with independent profile management, see [Adding-New-Settings-Menu-Tabs.md](Adding-New-Settings-Menu-Tabs.md).

## Overview

The settings menu system uses `RuntimeSceneSettings` as the central data class. Settings are organized into groups within tabs, and changes are persisted through JSON profile files.

## Quick Reference

| Step | File                      | Action                                                           |
| ---- | ------------------------- | ---------------------------------------------------------------- |
| 1    | `RuntimeSceneSettings.cs` | Add the property                                                 |
| 2    | `RuntimeSceneSettings.cs` | Update `DeepCopy()` method                                       |
| 3    | `SceneController.cs`      | Add inspector field in appropriate BoxGroup                      |
| 4    | `SceneController.cs`      | Update `CopyInspectorToRuntime()` method                         |
| 5    | `SceneController.cs`      | Update `CopyRuntimeToInspector()` method                         |
| 6    | `InGameSettingsMenu.cs`   | Add UI field in appropriate group method                         |
| 7    | `InGameSettingsMenu.cs`   | Update `MergeSceneSettings()` or `MergePostProcessingSettings()` |
| 8    | `InGameSettingsMenu.cs`   | Update `CopySceneSettings()` or `CopyPostProcessingSettings()`   |

## Step-by-Step Guide

### 1. Add the Property to RuntimeSceneSettings

**File:** `Assets/Scripts/RuntimeSceneSettings.cs`

Add your new property under the appropriate `[Header]` section:

```csharp
[Header("Boundary Drag")]
public float addedBoundaryDistance = 1.5f;
public float boundaryOutwardDrag = 50f;
```

**Property Types Supported:**

- `float` - Use `CreateFloatField()` or `CreateSliderField()`
- `bool` - Use `CreateToggleField()`
- `float[]` - Use `CreateFloatArrayField()`
- `AnimationCurve` - Use `CreateCurveField()` (limited support)

**For properties with change notifications:**

```csharp
[SerializeField]
private bool _myNewSetting = false;
public bool myNewSetting
{
    get => _myNewSetting;
    set
    {
        if (_myNewSetting != value)
        {
            _myNewSetting = value;
            OnAnyDebuggingSettingChanged?.Invoke();
        }
    }
}
```

### 2. Update DeepCopy() Method

**File:** `Assets/Scripts/RuntimeSceneSettings.cs`

Add your new property to the `DeepCopy()` method to ensure it's properly copied:

```csharp
public RuntimeSceneSettings DeepCopy()
{
    var copy = new RuntimeSceneSettings();
    // ... existing properties ...

    // Add your new property
    copy.addedBoundaryDistance = addedBoundaryDistance;
    copy.boundaryOutwardDrag = boundaryOutwardDrag;

    return copy;
}
```

### 3. Add Inspector Field to SceneController

**File:** `Assets/Scripts/SceneController.cs`

Add your new property in the appropriate `[BoxGroup]` section in the inspector region:

```csharp
[BoxGroup("Debugging")]
[Tooltip("Description of what this setting does.")]
public bool myNewSetting = false;
```

### 4. Update CopyInspectorToRuntime() Method

**File:** `Assets/Scripts/SceneController.cs`

Add your property to the `CopyInspectorToRuntime()` method:

```csharp
public void CopyInspectorToRuntime(RuntimeSceneSettings target)
{
    // ... existing properties ...

    // Add your new property
    target.myNewSetting = myNewSetting;
}
```

### 5. Update CopyRuntimeToInspector() Method

**File:** `Assets/Scripts/SceneController.cs`

Add your property to the `CopyRuntimeToInspector()` method:

```csharp
private void CopyRuntimeToInspector(RuntimeSceneSettings source)
{
    // ... existing properties ...

    // Add your new property
    myNewSetting = source.myNewSetting;
}
```

### 6. Add UI Field to InGameSettingsMenu

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

#### Option A: Add to an Existing Group

Find the appropriate `Create*Group()` method and add your field:

```csharp
private void CreateHandsAttractionGroup(ScrollView parentContainer)
{
    var group = CreateGroup("Hands Attraction", parentContainer);

    // ... existing fields ...

    // Add your new field
    CreateFloatField(group, "My New Setting",
        () => runtimeSettings.myNewSetting,
        v => runtimeSettings.myNewSetting = v);
}
```

#### Option B: Create a New Group

If your settings deserve their own category, create a new group method:

```csharp
private void CreateBoundaryDragGroup(ScrollView parentContainer)
{
    var group = CreateGroup("Boundary Drag", parentContainer);

    CreateFloatField(group, "Boundary Distance Multiplier",
        () => runtimeSettings.addedBoundaryDistance,
        v => runtimeSettings.addedBoundaryDistance = v);
    CreateFloatField(group, "Boundary Outward Drag",
        () => runtimeSettings.boundaryOutwardDrag,
        v => runtimeSettings.boundaryOutwardDrag = v);
}
```

Then add it to `CreateSceneSettingsContent()` or `CreatePostProcessingContent()`:

```csharp
private void CreateSceneSettingsContent()
{
    CreateGravityAttractionGroup(sceneSettingsPanel);
    CreateHandsAttractionGroup(sceneSettingsPanel);
    CreateBoundaryDragGroup(sceneSettingsPanel);  // Add your new group
    // ... rest of groups ...
}
```

### 7. Update Merge Method for Loading

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Add your property to the appropriate merge method so it loads from profiles:

**For Scene settings** - Update `MergeSceneSettings()`:

```csharp
private void MergeSceneSettings(RuntimeSceneSettings loadedSettings)
{
    // ... existing properties ...

    // Boundary Drag settings
    runtimeSettings.addedBoundaryDistance = loadedSettings.addedBoundaryDistance;
    runtimeSettings.boundaryOutwardDrag = loadedSettings.boundaryOutwardDrag;
}
```

**For Post-Processing settings** - Update `MergePostProcessingSettings()`:

```csharp
private void MergePostProcessingSettings(RuntimeSceneSettings loadedSettings)
{
    // ... existing properties ...

    runtimeSettings.myNewPostProcessingSetting = loadedSettings.myNewPostProcessingSetting;
}
```

### 8. Update Copy Method for Saving

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Add your property to the appropriate copy method so it saves to profiles:

**For Scene settings** - Update `CopySceneSettings()`:

```csharp
private void CopySceneSettings(RuntimeSceneSettings source, RuntimeSceneSettings destination)
{
    // ... existing properties ...

    // Boundary Drag settings
    destination.addedBoundaryDistance = source.addedBoundaryDistance;
    destination.boundaryOutwardDrag = source.boundaryOutwardDrag;

    // ... zeroing out post-processing values ...
}
```

**For Post-Processing settings** - Update `CopyPostProcessingSettings()`:

```csharp
private void CopyPostProcessingSettings(RuntimeSceneSettings source, RuntimeSceneSettings destination)
{
    // ... existing properties ...

    destination.myNewPostProcessingSetting = source.myNewPostProcessingSetting;

    // ... zeroing out scene values ...
}
```

**Important:** Also add the zero/default value for your property in the _opposite_ copy method to prevent cross-contamination:

```csharp
// In CopyPostProcessingSettings(), zero out scene settings:
destination.addedBoundaryDistance = 0.0f;
destination.boundaryOutwardDrag = 0.0f;

// In CopySceneSettings(), zero out post-processing settings:
destination.myNewPostProcessingSetting = 0.0f;
```

## Available UI Field Types

### Float Field

```csharp
CreateFloatField(group, "Label", () => runtimeSettings.property, v => runtimeSettings.property = v);
```

### Slider Field (with min/max range)

```csharp
CreateSliderField(group, "Label", () => runtimeSettings.property, v => runtimeSettings.property = v, minValue, maxValue);
```

### Toggle Field (boolean)

```csharp
CreateToggleField(group, "Label", () => runtimeSettings.property, v => runtimeSettings.property = v);
```

### Float Array Field

```csharp
CreateFloatArrayField(group, "Label", () => runtimeSettings.arrayProperty, v => runtimeSettings.arrayProperty = v);
```

## Group Organization

The settings menu follows this group structure to match the SceneController inspector:

**Scene Tab:**

- Gravity Attraction
- Hands Attraction
- Boundary Drag
- Intrinsic Pulsation
- Movement-Based Pulsation
- Miscellaneous
- Animation
- Style
- Debugging

**Post-Processing Tab:**

- Bloom
- Screen Space Lens Flare
- Lens Distortion
- Color Adjustments
- White Balance

## Common Pitfalls

1. **Forgetting DeepCopy()**: Your setting won't be properly copied when backing up/restoring settings.

2. **Missing SceneController updates**: Forgetting to add the inspector field or update `CopyInspectorToRuntime()`/`CopyRuntimeToInspector()` means the setting won't sync between inspector and runtime.

3. **Missing Merge method update**: Your setting won't load from saved profiles.

4. **Missing Copy method update**: Your setting won't save to profiles.

5. **Cross-contamination**: Forgetting to zero out your setting in the opposite Copy method causes scene settings to appear in post-processing profiles and vice versa.

6. **Wrong tab**: Adding a scene setting to post-processing methods or vice versa.

## Testing Checklist

After adding your new setting:

- [ ] Setting appears in the SceneController inspector in the correct BoxGroup
- [ ] Setting appears in the correct group in the in-game settings UI
- [ ] Changing the value in inspector updates runtime (in play mode)
- [ ] Changing the value in in-game menu updates inspector
- [ ] Saving a profile includes the new setting
- [ ] Loading a profile restores the setting value
- [ ] Profile JSON files only contain relevant settings (no cross-contamination)
- [ ] DeepCopy works correctly (test by opening/closing menu)

## Example: Complete Addition

Here's a complete example of adding `addedBoundaryDistance` and `boundaryOutwardDrag`:

### RuntimeSceneSettings.cs

```csharp
[Header("Boundary Drag")]
[Tooltip("Multiplier for max distance calculation.")]
public float addedBoundaryDistance = 1.5f;

[Tooltip("Drag applied when moving away from hands while past the boundary.")]
public float boundaryOutwardDrag = 50f;
```

### RuntimeSceneSettings.cs - DeepCopy()

```csharp
copy.addedBoundaryDistance = addedBoundaryDistance;
copy.boundaryOutwardDrag = boundaryOutwardDrag;
```

### SceneController.cs - Inspector Fields

```csharp
[BoxGroup("Boundary Drag")]
[Tooltip("Multiplier for max distance calculation.")]
public float addedBoundaryDistance = 1.5f;

[BoxGroup("Boundary Drag")]
[Tooltip("Drag applied when moving away from hands while past the boundary.")]
public float boundaryOutwardDrag = 50f;
```

### SceneController.cs - CopyInspectorToRuntime()

```csharp
// Boundary Drag
target.addedBoundaryDistance = addedBoundaryDistance;
target.boundaryOutwardDrag = boundaryOutwardDrag;
```

### SceneController.cs - CopyRuntimeToInspector()

```csharp
// Boundary Drag
addedBoundaryDistance = source.addedBoundaryDistance;
boundaryOutwardDrag = source.boundaryOutwardDrag;
```

### InGameSettingsMenu.cs - New Group Method

```csharp
private void CreateBoundaryDragGroup(ScrollView parentContainer)
{
    var group = CreateGroup("Boundary Drag", parentContainer);

    CreateFloatField(group, "Boundary Distance Multiplier",
        () => runtimeSettings.addedBoundaryDistance,
        v => runtimeSettings.addedBoundaryDistance = v);
    CreateFloatField(group, "Boundary Outward Drag",
        () => runtimeSettings.boundaryOutwardDrag,
        v => runtimeSettings.boundaryOutwardDrag = v);
}
```

### InGameSettingsMenu.cs - CreateSceneSettingsContent()

```csharp
private void CreateSceneSettingsContent()
{
    CreateGravityAttractionGroup(sceneSettingsPanel);
    CreateHandsAttractionGroup(sceneSettingsPanel);
    CreateBoundaryDragGroup(sceneSettingsPanel);  // Added
    // ... rest ...
}
```

### InGameSettingsMenu.cs - MergeSceneSettings()

```csharp
// Boundary Drag settings
runtimeSettings.addedBoundaryDistance = loadedSettings.addedBoundaryDistance;
runtimeSettings.boundaryOutwardDrag = loadedSettings.boundaryOutwardDrag;
```

### InGameSettingsMenu.cs - CopySceneSettings()

```csharp
// Boundary Drag settings
destination.addedBoundaryDistance = source.addedBoundaryDistance;
destination.boundaryOutwardDrag = source.boundaryOutwardDrag;
```

### InGameSettingsMenu.cs - CopyPostProcessingSettings()

```csharp
// Zero out boundary drag settings
destination.addedBoundaryDistance = 0.0f;
destination.boundaryOutwardDrag = 0.0f;
```
