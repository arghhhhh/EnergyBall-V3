# Adding New Settings to the In-Game Settings Menu

This guide explains how to add new settings fields to an existing tab in the in-game settings menu. For adding entirely new tabs with independent profile management, see [Adding-New-Settings-Menu-Tabs.md](Adding-New-Settings-Menu-Tabs.md).

## Overview

The settings menu system uses `RuntimeSceneSettings` as the central data class. The Scene tab is organized into **sections** (Space, Kinect, Ball, Hands, Particles, Debug — `SettingSections` in `SettingGroupAttribute.cs`), each holding collapsible **groups**; the menu shows one section at a time, picked from a sidebar. The Post Processing tab has groups only. The `SceneController` inspector shows the same sections, groups and labels (drawn by `Assets/Editor/SceneControllerEditor.cs` from `[SettingGroup]` attributes). Changes are persisted through JSON profile files.

**Working set.** Independently of profiles, every change (menu, profile load, inspector in play or edit mode) is written to a per-scene working-set file (`SettingsWorkingSet.cs`, under `Application.persistentDataPath`). It is restored at play start and copied back into the inspector when play stops, so the latest change always sticks; the menu marks the tab dirty (`Scene *`, "Unsaved changes") until you Save. New fields need nothing extra for this - the whole `RuntimeSceneSettings` object is serialized - but **a field missing from `CopySceneSettings` is invisible to dirty tracking and to profiles**, so steps 7-8 matter. Always go through `NotifySettingsChanged()` (never invoke `OnSettingsChanged` directly) so the working set and dirty state stay current.

**Base vs. effective values.** Everything the menu, the `SceneController` inspector and the JSON profiles hold is a _base_ value at `bodyScale = 1`. `SceneController.RebuildEffectiveSettings()` derives the object consumers read (`CurrentSettings` / `GetRuntimeSettings()`) as `base × bodyScale^exp` via `BodyScaling.CreateEffective`. So:

- **If the new setting has a dimension** (a length, velocity, per-frame displacement, rigidbody force, spatial frequency), put `[BodyScaled(exp)]` on the `RuntimeSceneSettings` field and store the **1× value**. Exponents: lengths / velocities / per-frame displacements `1`; rigidbody forces (`AddForce`) `2` (mass ∝ s); spatial frequencies `-1`. Time, ratios, rates, curves, bools and counts get no attribute. See `Docs/BodyScale.md` for the derivation and the VFX-specific rules.
- Consumers never rescale anything themselves — they just read the effective object.

**Post-processing settings are different.** The Post Processing tab edits one of two looks, `RuntimeSceneSettings.particlePostProcessing` or `feedPostProcessing` (both `PostProcessSettings`). The tab's **Particles / Camera Feed** switch picks which one. A PP profile file is just a serialized `PostProcessSettings`, so a new PP value needs only three things:

1. A field on `PostProcessSettings.cs` with a C# default. `DeepCopy()`, profile load/save, dirty tracking and the working set all go through JSON, so there's nothing else to copy.
2. A write in `VolumeController.VolumeTarget.Apply()`. If it's a new Volume override, also add a `TryGet` in `Resolve()` and add the override to both Volume Profile assets (`VolumeProfile.asset`, `VolumeProfile (Camera Feed).asset`).
3. A row in `CreatePostProcessingGroup()` bound to `ActivePP`, never `runtimeSettings`:

```csharp
CreateSliderField(bloomGroup, "Bloom Intensity", () => ActivePP.bloomIntensity, v => ActivePP.bloomIntensity = v, 0f, 3f);
```

Steps 1–8 below are for **scene** settings.

**Settings that need a feature.** If a setting only makes sense with a live Kinect, or with a camera feed in the scene, gate it on `SceneController.SceneFeature` rather than on a scene name:

- Menu: build the row inside `if (SceneSupports(SceneController.SceneFeature.Kinect)) { ... }` (or `CameraFeed`). The whole Kinect section is only built with a Kinect, and its Camera Feed group only with a camera feed, so a setting that belongs there needs no extra check.
- Inspector: give the SceneController twin `[ShowIf("HasKinect")]` / `[ShowIf("HasCameraFeed")]`. A section or group whose fields are all hidden is left out of the inspector. NaughtyAttributes allows one ShowIf/HideIf per field, so when the field already has a rule, combine them with `EConditionOperator.And`. If a rule needs negation, add a named condition property next to `ShowSkeletonColorField`.
- If a row depends on another setting's value rather than a scene feature (e.g. only while Draw Skeleton is on), call `ShowRowIf(group, "Label", () => runtimeSettings.x)` right after creating the field (`group` is the element `CreateGroup` returned; rows are looked up by group + label, since labels repeat across groups). The menu re-evaluates these on every settings change. It hides the row with the `condition-hidden` class, not an inline `style.display`, so it combines with the search filter's `search-hidden` (an inline display would override the class and un-hide filtered rows).
- Load, save and copy as usual. The value stays in profiles even where it's hidden, so profiles shared between scenes keep it.
- A new kind of feature means a new `SceneFeature` flag, set in `SceneController.Features`.

## Quick Reference

| Step | File                      | Action                                                           |
| ---- | ------------------------- | ---------------------------------------------------------------- |
| 1    | `RuntimeSceneSettings.cs` | Add the property                                                 |
| 2    | `RuntimeSceneSettings.cs` | Update `DeepCopy()` method                                       |
| 3    | `SceneController.cs`      | Add inspector field with `[SettingGroup(section, group)]`        |
| 4    | `SceneController.cs`      | Update `CopyInspectorToRuntime()` method                         |
| 5    | `SceneController.cs`      | Update `CopyRuntimeToInspector()` method                         |
| 6    | `InGameSettingsMenu.cs`   | Add UI field in appropriate group method                         |
| 7    | `InGameSettingsMenu.cs`   | Update `MergeSceneSettings()`                                    |
| 8    | `InGameSettingsMenu.cs`   | Update `CopySceneSettings()`                                     |

## Step-by-Step Guide

### 1. Add the Property to RuntimeSceneSettings

**File:** `Assets/Scripts/RuntimeSceneSettings.cs`

Add your new property. Field order here is only the JSON order; where the setting shows up is decided by `[SettingGroup]` on the inspector twin (step 3) and by the menu (step 6).

```csharp
[BodyScaled(1)]
public float addedBoundaryDistance = 1.5f;
[BodyScaled(1)]
public float boundaryOutwardDrag = 50f;
```

**Property Types Supported:**

- `float` - Use `CreateFloatField()` or `CreateSliderField()`
- `int` - Use `CreateIntField()`
- `Vector2` / `Vector3` - Use `CreateVector2Field()` / `CreateVector3Field()`
- `bool` - Use `CreateToggleField()`
- `float[]` - Use `CreateFloatArrayField()`
- `AnimationCurve` - Use `CreateCurveField()` (opens the runtime curve editor popup)
- `Color` / `Color[]` - Use `CreateColorField()` / `CreateColorArrayField()` (opens the runtime color picker)
- `Gradient` / `Gradient[]` - Use `CreateGradientField()` / `CreateGradientArrayField()` (opens the runtime gradient editor)

`[BodyScaled]` supports `float`, `Vector2` and `Vector3` fields.

**Nested groups.** `HandVfxSettings` is a `[Serializable]` class nested in `RuntimeSceneSettings` as `handVfx` (JSON: `"handVfx": { ... }`). Its fields carry `[BodyScaled]` / `[VfxProperty("graphName")]` and are pushed to the hand VFX graphs by `PlayerScaleApplier`. At every plumbing site the nested object is copied **as one object** (`target.handVfx = source.handVfx.DeepCopy()`), so adding a value to it only needs: the field in `HandVfxSettings` (+ tooltip, attributes) and a row in the matching Particles group method in the menu (`CreateGlowGroup`, `CreateBallAttractionGroup`, ...). Give the field `[SettingGroup(SettingSections.Particles, "<group>")]` (+ `[Label]` when the menu label differs) and declare it among that group's fields: the inspector flattens `handVfx` into the Particles groups in declaration order, and only does so while **every** `HandVfxSettings` field has a `[SettingGroup]`. To push a value to the graph, name the exposed property in `[VfxProperty]` — the applier discovers it by reflection and `Has*`-guards the write (`float`, `int`, `Vector2`, `Vector3`, `AnimationCurve`). Curves in `HandVfxSettings` must be copied by keys in its `DeepCopy()`.

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

Add your new property in the inspector region with the section and group it has in the menu. `SceneControllerEditor` draws the sections in `SettingSections.Order` and each group's fields in declaration order, so place the field next to the rest of its group. Add NaughtyAttributes `[Label]` when the menu label isn't the field's nicified name:

```csharp
[SettingGroup(SettingSections.Debug, "Visualizers")]
[Label("My New Setting")]
[Tooltip("Description of what this setting does.")]
public bool myNewSetting = false;
```

Fields without `[SettingGroup]` (scene references such as `playerPrefab`) are drawn above the sections. Don't use `[BoxGroup]` / `[Foldout]` on settings fields: the custom editor ignores them.

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
private void CreatePushGroup(SettingSection section)
{
    var group = CreateGroup("Push", section);

    // ... existing fields ...

    // Add your new field
    CreateFloatField(group, "My New Setting",
        () => runtimeSettings.myNewSetting,
        v => runtimeSettings.myNewSetting = v,
        tooltip: "One or two sentences on what it does and which direction is 'more'.");
}
```

**Hover tooltips.** Every `Create*Field` helper takes an optional trailing `string tooltip = null`. When set, hovering the setting's label shows a runtime popup (`AttachTooltip` / `ShowTooltip` in `InGameSettingsMenu.cs`, styled by `.setting-tooltip` in `Assets/UI/SettingsMenu.uss`). This is a custom popup because UI Toolkit's built-in `VisualElement.tooltip` only renders inside the Editor. Guidelines:

- Add one for any setting whose effect isn't obvious from the label — physics caps, thresholds, dampers, and especially **curves** (state what X = 0 / X = 1 mean, since several are inverted, e.g. `Force To Middle` X = 1 is "ball at target").
- Skip it for self-explanatory values (most Particles rows).
- If the field already has a `[Tooltip]` on its `SceneController` twin, reuse that text so the inspector and menu agree. For a long shared text, put it in a `const` and use it in both places (`SceneController.ReArmDelayTooltip`).
- Describe the behavior, not the body-scale units — those aren't shown to the user.

#### Option B: Create a New Group

If your settings deserve their own category, create a new group method. Name groups by what they control, and keep the section's name out of the title (the section's page title already says it). Row labels only need to be unique within their group: search matches the section title + group title + label, so a shared label like "Stick Force" is found per group ("trail stick").

```csharp
private void CreatePlayBoundaryGroup(SettingSection section)
{
    var group = CreateGroup("Play Boundary", section);

    CreateFloatField(group, "Margin",
        () => runtimeSettings.addedBoundaryDistance,
        v => runtimeSettings.addedBoundaryDistance = v,
        tooltip: "Margin added around the metaball grid to define the play boundary.");
    CreateFloatField(group, "Outward Drag",
        () => runtimeSettings.boundaryOutwardDrag,
        v => runtimeSettings.boundaryOutwardDrag = v,
        tooltip: "Drag opposing the ball while it is past the boundary and moving away from the hands. 0 disables.");
}
```

Then call it inside its section in `CreateSceneSettingsContent()` (or, for the Post Processing tab, with `CreateGroup(title, postProcessingPanel)` in `CreatePostProcessingGroup()`):

```csharp
private void CreateSceneSettingsContent()
{
    var panel = sceneSettingsPanel;

    var space = CreateSection(SettingSections.Space, panel, sceneSectionNav);
    CreateWorldGroup(space);
    CreatePlayBoundaryGroup(space);  // Add your new group
    // ... rest of the sections ...
}
```

A new section needs a constant in `SettingSections` and a place in `SettingSections.Order`, so the inspector draws it in the same position. `CreateSection` adds its sidebar entry; call it in that same order.

### 7. Update Merge Method for Loading

**File:** `Assets/Scripts/InGameSettingsMenu.cs`

Add your property to the appropriate merge method so it loads from profiles:

**For Scene settings** - Update `MergeSceneSettings()`:

```csharp
private void MergeSceneSettings(RuntimeSceneSettings loadedSettings)
{
    // ... existing properties ...

    // Play Boundary settings
    runtimeSettings.addedBoundaryDistance = loadedSettings.addedBoundaryDistance;
    runtimeSettings.boundaryOutwardDrag = loadedSettings.boundaryOutwardDrag;
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

    // Play Boundary settings
    destination.addedBoundaryDistance = source.addedBoundaryDistance;
    destination.boundaryOutwardDrag = source.boundaryOutwardDrag;

}
```

Post-processing values never pass through here. They live in `PostProcessSettings` and are saved separately (see the Overview), so there's no cross-tab zeroing to do.


## Available UI Field Types

All helpers accept an optional trailing `tooltip:` argument (see step 6). It is omitted below for brevity.

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

### Curve Field (AnimationCurve)

```csharp
CreateCurveField(group, "Label", () => runtimeSettings.curveProperty, v => runtimeSettings.curveProperty = v);
```

Renders an interactive thumbnail of the curve. Clicking the thumbnail opens the `RuntimeCurveEditorWindow` popup with full keyframe editing, tangent mode controls, and preset support.

### Color / Gradient Fields

```csharp
CreateColorField(group, "Label", () => runtimeSettings.color, v => runtimeSettings.color = v, hdr: false, showAlpha: true);
CreateGradientField(group, "Label", () => runtimeSettings.gradient, v => runtimeSettings.gradient = v, hdr: true);
CreateColorArrayField(group, "Label", () => runtimeSettings.colors, v => runtimeSettings.colors = v);
CreateGradientArrayField(group, "Label", () => runtimeSettings.gradients, v => runtimeSettings.gradients = v, hdr: true);
```

A swatch / gradient strip that opens the runtime ports of the editor's pickers (`Assets/Scripts/RuntimeColorEditor/`): `RuntimeColorPickerWindow` (hue ring, RGB 0-255 / RGB 0-1 / HSV sliders, alpha, hex; with `hdr` the Intensity slider and exposure swatches) and `RuntimeGradientEditorWindow` (blend mode, alpha and color keys, max 8 each; a color key opens the color picker on top). The gradient editor has the editor's Presets section (`RuntimeGradientPresets`, libraries in `StreamingAssets/GradientPresets/`): click applies, Alt+click deletes, New adds the current gradient, right-click offers Replace / Delete / Rename / Move To First, and the ⋮ menu switches Grid / List and the library. Pass `hdr: true` where the inspector field has `[ColorUsage(…, true)]` / `[GradientUsage(true)]`: HDR colors are linear, so the picker shows them gamma-encoded, as in the editor. The gradient editor edits the gradient in place, then calls the setter.

`Gradient` is a reference type: copy it with `ColorSettingsUtility.Clone()` at every plumbing site (DeepCopy, merge, copy, inspector both ways), like curves. The arrays have `Clone()` overloads too.

**Older files.** `JsonUtility` fills a key missing from a profile / working set with the C# default, which would replace real colors with white gradients. The existing colors are therefore grouped behind `hasStyleColors` (false in files written before them) and copied with `CopyStyleColorsFrom()`, which skips a source without colors. A new color setting belongs in that group: add it to `CopyStyleColorsFrom()`, `StyleColorsEqual()` (live players are recolored when it changes) and both `SceneController` copy methods.

## AnimationCurve Settings — Extra Steps

`AnimationCurve` properties require special handling in several places compared to scalar types. The differences are called out below.

### Declaration

Give curves a sensible default shape:

```csharp
public AnimationCurve myCurve = AnimationCurve.Linear(0, 0, 1, 1);
```

### DeepCopy()

Curves are reference types, so you must copy the keys into a new instance:

```csharp
copy.myCurve = new AnimationCurve(myCurve.keys);
```

A plain `copy.myCurve = myCurve;` would share the same object — edits in the menu would mutate the backup.

### MergeSceneSettings()

Guard against null or empty curves from older profile JSON files that may not contain the field:

```csharp
if (loadedSettings.myCurve != null && loadedSettings.myCurve.length > 0)
    runtimeSettings.myCurve = new AnimationCurve(loadedSettings.myCurve.keys);
```

### CopySceneSettings()

Copy the curve by keys, same as DeepCopy:

```csharp
destination.myCurve = new AnimationCurve(source.myCurve.keys);
```

### CopyInspectorToRuntime() / CopyRuntimeToInspector()

Copy by keys in both directions:

```csharp
// CopyInspectorToRuntime
target.myCurve = new AnimationCurve(myCurve.keys);

// CopyRuntimeToInspector
myCurve = new AnimationCurve(source.myCurve.keys);
```

### Int / Vector Fields

```csharp
CreateIntField(group, "Label", () => runtimeSettings.handVfx.spawnRate, v => runtimeSettings.handVfx.spawnRate = v);
CreateVector2Field(group, "Label", () => runtimeSettings.handVfx.sizeRange, v => runtimeSettings.handVfx.sizeRange = v);
```

### Testing Checklist (curve-specific)

In addition to the general checklist:

- [ ] Curve thumbnail renders in the settings menu and updates when the curve changes
- [ ] Clicking the thumbnail opens the curve editor popup
- [ ] Edits in the curve editor reflect back in the settings and take effect in the scene
- [ ] Saving a profile preserves the curve shape (including tangent modes)
- [ ] Loading a profile restores the curve correctly
- [ ] Opening/closing the menu doesn't corrupt curve data (DeepCopy works)

## Group Organization

The settings menu and the SceneController inspector share this structure (sections in `SettingSections.Order`):

**Scene Tab:**

- **Space** — World (Body Scale, Base Z Depth, Grid Scale) · Play Boundary (margin, outward drag, out-of-bounds reset)
- **Kinect** (only with a Kinect) — Tracking · Camera Feed (only with a feed) · Skeleton
- **Ball** — Size · Breathing (intrinsic pulsation) · Spawn (grow-in, spawn flash) · Gravity
- **Hands** — Activation · Push · Aim · One Hand · Grow & Shrink (movement-based scaling)
- **Particles** (the hand VFX; mostly `HandVfxSettings`) — Color · Glow · Emission · Lifetime · Stretch · Ball Attraction · Secondary Attractor · Trail Distorters · Noise & Turbulence · Hi-Hat Bursts · Snare Bursts
- **Debug** — Visualizers

Color Per Player (`individualColors`) lives in Particles > Color with the gradient / palette it switches between; the Skeleton Palette (Kinect > Skeleton) follows the same palette slots and only shows while it's on.

**Post-Processing Tab:**

- Bloom
- Screen Space Lens Flare
- Lens Distortion
- Color Adjustments
- White Balance

## Common Pitfalls

0. **Storing an effective value**: a dimensioned field without `[BodyScaled]`, or a `[BodyScaled]` field seeded with a 5×-tuned number, breaks the "change bodyScale, nothing else" invariant. Store the 1× value.

1. **Forgetting DeepCopy()**: Your setting won't be properly copied when backing up/restoring settings.

2. **Missing SceneController updates**: Forgetting to add the inspector field or update `CopyInspectorToRuntime()`/`CopyRuntimeToInspector()` means the setting won't sync between inspector and runtime.

3. **Missing Merge method update**: Your setting won't load from saved profiles.

4. **Missing Copy method update**: Your setting won't save to profiles.

5. **PP value on `RuntimeSceneSettings`**: post-processing values belong on `PostProcessSettings`, and the menu row must bind to `ActivePP`. Otherwise the Particles / Camera Feed switch, PP profiles and the Volumes never see the value.

6. **Wrong tab**: Adding a scene setting to the Post Processing tab or vice versa.

7. **Tooltip text drift**: if the field has a `[Tooltip]` on `RuntimeSceneSettings` / `SceneController`, keep the menu `tooltip:` in sync (or copy it verbatim) so the inspector and menu don't contradict each other.

## Testing Checklist

After adding your new setting:

- [ ] Setting appears in the SceneController inspector in the same section and group as in the menu
- [ ] Setting appears in the correct group in the in-game settings UI
- [ ] Hovering the label shows the tooltip (if one was given)
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
[BodyScaled(1)]
[Tooltip("Margin added around the metaball grid to define the play boundary.")]
public float addedBoundaryDistance = 1.5f;

[BodyScaled(1)]
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
[SettingGroup(SettingSections.Space, "Play Boundary")]
[Label("Margin")]
[Tooltip("Margin added around the metaball grid to define the play boundary.")]
public float addedBoundaryDistance = 1.5f;

[SettingGroup(SettingSections.Space, "Play Boundary")]
[Label("Outward Drag")]
[Tooltip("Drag applied when moving away from hands while past the boundary.")]
public float boundaryOutwardDrag = 50f;
```

### SceneController.cs - CopyInspectorToRuntime()

```csharp
// Play Boundary
target.addedBoundaryDistance = addedBoundaryDistance;
target.boundaryOutwardDrag = boundaryOutwardDrag;
```

### SceneController.cs - CopyRuntimeToInspector()

```csharp
// Play Boundary
addedBoundaryDistance = source.addedBoundaryDistance;
boundaryOutwardDrag = source.boundaryOutwardDrag;
```

### InGameSettingsMenu.cs - New Group Method

```csharp
private void CreatePlayBoundaryGroup(SettingSection section)
{
    var group = CreateGroup("Play Boundary", section);

    CreateFloatField(group, "Margin",
        () => runtimeSettings.addedBoundaryDistance,
        v => runtimeSettings.addedBoundaryDistance = v,
        tooltip: "Margin added around the metaball grid to define the play boundary.");
    CreateFloatField(group, "Outward Drag",
        () => runtimeSettings.boundaryOutwardDrag,
        v => runtimeSettings.boundaryOutwardDrag = v,
        tooltip: "Drag opposing the ball while it is past the boundary and moving away from the hands. 0 disables.");
}
```

### InGameSettingsMenu.cs - CreateSceneSettingsContent()

```csharp
private void CreateSceneSettingsContent()
{
    var panel = sceneSettingsPanel;

    var space = CreateSection(SettingSections.Space, panel, sceneSectionNav);
    CreateWorldGroup(space);
    CreatePlayBoundaryGroup(space);  // Added
    // ... rest ...
}
```

### InGameSettingsMenu.cs - MergeSceneSettings()

```csharp
// Play Boundary settings
runtimeSettings.addedBoundaryDistance = loadedSettings.addedBoundaryDistance;
runtimeSettings.boundaryOutwardDrag = loadedSettings.boundaryOutwardDrag;
```

### InGameSettingsMenu.cs - CopySceneSettings()

```csharp
// Play Boundary settings
destination.addedBoundaryDistance = source.addedBoundaryDistance;
destination.boundaryOutwardDrag = source.boundaryOutwardDrag;
```
