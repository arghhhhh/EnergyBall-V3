# Metaball Radius Animation Feature

## Overview

This feature implements an animation that smoothly transitions the player's metaball radius from a small starting size to the full sphere scale when the initialization condition is met (`timeSinceStateChange > settings.initializationResetDelay || justInitialized`).

---

## Files Modified

| File                      | Changes                                           |
| ------------------------- | ------------------------------------------------- |
| `PlayerConstructor.cs`    | Added animation state fields and helper methods   |
| `RuntimeSceneSettings.cs` | Added new configurable settings                   |
| `SceneController.cs`      | Added inspector fields and sync methods           |
| `HandEffects.cs`          | Trigger animation start/stop based on hand states |
| `InGameSettingsMenu.cs`   | Added UI fields and profile save/load support     |

---

## New Settings

```csharp
// In RuntimeSceneSettings.cs and SceneController.cs
public float metaballRadiusAnimationDuration = 2f;    // Duration of the animation in seconds
public float metaballRadiusAnimationStartSize = 0.1f; // Starting radius for the animation
public AnimationCurve metaballRadiusAnimationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // Animation easing curve
```

Note: The animation curve is controlled via the SceneController inspector and is NOT exposed in the in-game menu (feature planned for later).

---

## Implementation Details

### 1. Animation State (PlayerConstructor.cs)

```csharp
// Non-serialized to prevent Unity from persisting stale state
[System.NonSerialized] public bool metaballRadiusAnimating = false;
[System.NonSerialized] public float metaballRadiusAnimationStartTime = 0f;
[System.NonSerialized] public float metaballRadiusAtAnimationStart = 0f;
// Tracks when BOTH hands became closed - animation only triggers after this delay
[System.NonSerialized] public float bothHandsClosedSinceTime = 0f;
```

### 2. Helper Methods (PlayerConstructor.cs)

| Method                                   | Purpose                                                       |
| ---------------------------------------- | ------------------------------------------------------------- |
| `StartMetaballRadiusAnimation(settings)` | Starts animation; handles smooth restart if already animating |
| `StopMetaballRadiusAnimation()`          | Stops animation (called when hands close or out of bounds)    |
| `GetMetaballRadius(settings)`            | Returns current animated radius, ends animation when complete |
| `GetCurrentAnimatedRadius(settings)`     | Internal helper for smooth transitions                        |

### 3. Animation Trigger (HandEffects.cs)

**Animation starts when:**

-   Hand opens AND the other hand is currently closed, AND both hands have been closed for `initializationResetDelay`, OR
-   Player just initialized (`justInitialized = true`)

**Animation does NOT start when:**

-   Switching which single hand is open (e.g., O→P or P→O) - both hands weren't closed long enough
-   Going from one-hand-open to both-open (e.g., O→U or P→U) - the other hand was already open
-   This ensures animation ONLY plays when transitioning FROM the "both hands closed" state

**Note on realistic usage:** Users rarely open both hands at exactly the same time. The first hand to open (while the other is still closed) triggers the animation. When the second hand opens shortly after, no additional animation is triggered since the first hand is already open.

**Animation stops when:**

-   Both hands close
-   Player goes out of bounds

### 4. Radius Application (SceneController.cs)

```csharp
metaballsToSDF.SetMetaballRadius(
    playerConstructor.metaballIndex,
    playerConstructor.GetMetaballRadius(cachedCurrentSettings)
);
```

---

## Settings Integration

Followed the conventions in [Adding-New-Settings-To-Menu.md](In-Game%20Menu/Adding-New-Settings-To-Menu.md):

| Step | File                      | Method/Location                                     |
| ---- | ------------------------- | --------------------------------------------------- |
| 1    | `RuntimeSceneSettings.cs` | Added properties under `[Header("Animation")]`      |
| 2    | `RuntimeSceneSettings.cs` | Updated `DeepCopy()`                                |
| 3    | `SceneController.cs`      | Added inspector fields in `[BoxGroup("Animation")]` |
| 4    | `SceneController.cs`      | Updated `CopyInspectorToRuntime()`                  |
| 5    | `SceneController.cs`      | Updated `CopyRuntimeToInspector()`                  |
| 6    | `InGameSettingsMenu.cs`   | Added UI fields in `CreateAnimationGroup()`         |
| 7    | `InGameSettingsMenu.cs`   | Updated `MergeSceneSettings()`                      |
| 8    | `InGameSettingsMenu.cs`   | Updated `CopySceneSettings()`                       |
| 8b   | `InGameSettingsMenu.cs`   | Zeroed out in `CopyPostProcessingSettings()`        |

---

## Edge Cases Handled

| Scenario                                   | Behavior                                       |
| ------------------------------------------ | ---------------------------------------------- |
| Hand closes during animation               | Animation stops if both hands close            |
| Player goes out of bounds                  | Animation stops immediately                    |
| Hand reopens during ongoing animation      | Smooth restart from current radius             |
| Animation completes naturally              | `metaballRadiusAnimating` set to false         |
| Quickly switching single hand open (O↔P)   | Animation skipped (both hands weren't closed)  |
| One hand open → both open (O→U or P→U)     | Animation skipped (other hand already open)    |
| Both closed → one opens after delay        | Animation plays (first hand triggers it)       |
| Both closed → both open same frame (U key) | Left hand triggers animation (processed first) |

---

## How It Works

1. When the initialization condition is met in `HandEffects.ManageHandEffects()`, `StartMetaballRadiusAnimation()` is called
2. Each frame, `SceneController.UpdateOtherPlayerData()` calls `GetMetaballRadius()` which:
    - Returns the sphere's actual scale if not animating
    - Calculates linear `t` from 0→1 over the animation duration
    - Applies the animation curve: `curvedT = metaballRadiusAnimationCurve.Evaluate(t)`
    - Returns `Lerp(startSize, currentSphereScale, curvedT)` for smooth easing
3. The animation tracks the **current** sphere scale (not a fixed target), so pulsation effects are respected
4. When linear `t >= 1`, the animation ends automatically (curve doesn't affect timing, only progression)

---

## Testing

-   Settings appear in Unity Inspector under **SceneController → Animation**
-   Duration and start size appear in-game menu under **Scene Tab → Animation**
-   Animation curve is only editable in the Unity Inspector (in-game curve editing planned for later)
-   Changing values updates the animation behavior in real-time
-   Profiles save/load duration and start size (curve is excluded from JSON profiles)
