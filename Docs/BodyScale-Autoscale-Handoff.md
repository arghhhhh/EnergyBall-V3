# `bodyScale` auto-scaling: base settings → effective settings

Status: **implemented (2026-08-17)**. This page describes what exists and the
decisions behind it. The exponent derivation lives in
`Docs/BodyScale-settings-audit.md` (C# settings and constants) and
`Docs/HandEffects-scale-audit.md` (graph values). Menu plumbing for new fields:
`Docs/In-Game Menu/Adding-New-Settings-To-Menu.md`.

## The invariant

> Profiles, the `SceneController` inspector and the in-game menu store **base
> values at bodyScale = 1**. Every scale-dependent field carries an exponent.
> Effective values are derived once per settings change as `base × bodyScale^exp`.
> Changing `bodyScale` — and nothing else — leaves gameplay and look identical
> relative to body size, for newly spawned players *and* for players already alive.

Exponents: lengths / velocities / per-frame displacements `1`; rigidbody forces
`2` (mass ∝ s); spatial frequencies `−1`; time, ratios, rates, curves, bools,
counts `0` (no attribute). Hand-VFX "forces" act on unit-mass particles → `1`.

## Where things live

| Piece | File | Role |
|---|---|---|
| `[BodyScaled(exp)]` | `Assets/Scripts/BodyScaledAttribute.cs` | Marks a `float` / `Vector2` / `Vector3` settings field as scale-dependent. |
| `[VfxProperty("name")]` | `Assets/Scripts/VfxPropertyAttribute.cs` | Names the exposed HandEffects.vfx property a `HandVfxSettings` field is pushed to. |
| `HandVfxSettings` | `Assets/Scripts/HandVfxSettings.cs` | Typed hand-VFX values (base at 1×), `[Header]` groups = menu groups, `DeepCopy()`. Nested in the scene profile as `"handVfx": {…}`. |
| `BodyScaling` | `Assets/Scripts/BodyScaling.cs` | Static reflection table (built once) of every `[BodyScaled]` field of `RuntimeSceneSettings`, recursing one level into `[Serializable]` member classes. `CreateEffective(base)` = `DeepCopy()` + multiply. `ConvertLegacyProfileInPlace()` for v0 files. |
| `PlayerScaleApplier` | `Assets/Scripts/PlayerScaleApplier.cs` | The one per-player scale step (force-class style, owned by `SceneController`). |
| `RuntimeSceneSettings` | `Assets/Scripts/RuntimeSceneSettings.cs` | Same class as before + attributes, `settingsVersion`, `handVfx`, `maxHandVelocity`, `sphereResetJitter`, `bodySpawnSize`. C# defaults are base values. |
| `SceneController` | `Assets/Scripts/SceneController.cs` | `runtimeSettings` = base, `cachedCurrentSettings` = effective, `RebuildEffectiveSettings()`. Inspector twins for the new fields (`[BoxGroup("Hand VFX")] handVfx`). |
| `InGameSettingsMenu` | `Assets/Scripts/InGameSettingsMenu.cs` | Base values with unit hints, Hand VFX groups in the Scene tab, `CreateIntField` / `CreateVector2Field` / `CreateVector3Field`, legacy auto-convert on Scene load, `settingsVersion` stamping on save. |
| Migration | `Assets/Editor/MigrateSceneProfilesToBase.cs` | Menu item `EnergyBall/Migrate Scene Profiles To Base`. |

## Data flow (one direction)

```
menu / inspector / profile  ──►  runtimeSettings (BASE)
                                      │  RebuildEffectiveSettings()  [Awake, SyncInspectorToRuntime, OnRuntimeSettingsChanged]
                                      ▼
                            cachedCurrentSettings (EFFECTIVE) = BodyScaling.CreateEffective(base)
                                      │
            ┌─────────────────────────┼─────────────────────────────┐
            ▼                         ▼                             ▼
   consumers read CurrentSettings /   PlayerScaleApplier.ApplyToAll  PlayerScaleApplier.RescaleLiveStateAll
   GetRuntimeSettings() per frame     (hand/body VFX, TD, collider)  (only when effective bodyScale changed)
```

- Consumers (`GravityForce`, `HandForce`, `BoundaryForce`, `PlayerScaler`,
  `HandEffects`, `PlayerConstructor`, `MetaballsToSDF`, `MetaballBoundsVisualizer`,
  `BodyDepthOccluder`, `DummyTransformer`, `DummyHandController`) read the
  effective object and never rescale anything themselves. **Nothing writes into
  the effective object**; mutate the base and let `OnSettingsChanged` rebuild.
- `CurrentSettings` rebuilds lazily if read before `Awake` finished (there is no
  other fallback path any more).
- The rebuild allocates (`DeepCopy` of curves/arrays) — by design, per settings
  change only. The runtime curve editor fires `OnSettingsChanged` on every drag
  step, so dragging a curve rebuilds per step; curves are exp 0 and this stays
  well under a millisecond, accepted rather than adding any per-frame path.
- `bodyScale` itself is never scaled; zero/negative bodyScale is treated as 1.

## `PlayerScaleApplier` (called from `InitializeNewPlayer`, `InitializeNewDummy`, `RebuildEffectiveSettings`)

- `ApplyHandVfx`: iterates the static `[VfxProperty]` table (FieldInfo + graph name
  + cached `Shader.PropertyToID`) and pushes to `leftHandVfx` / `rightHandVfx` with
  `HasFloat/HasInt(+UInt)/HasVector2/HasVector3` guards. Editor-only, one warning
  per missing name per play session (`[RuntimeInitializeOnLoadMethod]` reset).
- `ApplyBodyVfx`: `bodySpawnSize` → `PlayerConstructor.bodyVfx` (`VFX_Body`,
  `BodyEffects.vfx`, exposed float `bodySpawnSize` linked to the Init `Set _Size`).
- `ApplyTransforms` (explicit lines): TD1/TD2 `localScale = handVfx.tdRadius × 2`;
  the four `BrownianMotion.positionAmount = handVfx.tdWanderAmount`; hand `Collider`
  child `localScale = bodyScale` (prefab `BoxCollider.size` is authored at 1× =
  `(1, 1, 0.02)`); SA debug spheres `0.02 × bodyScale`. TD1/TD2 (under a unit-scale
  "Trail Distorters" container), SA and Collider are unit-scale siblings under the
  hand — none compounds with another; keep it that way.
- `RescaleLiveState(ratio)`: when the effective bodyScale changes while players
  exist, length-valued *state* follows — `unscaledSize`, `sphere.position` and
  `linearVelocity` (world scales about the origin/camera, exactly like Kinect
  joints), hand prev-positions, `metaballRadiusAtAnimationStart`. Without this the
  ball would keep its old world size/position while the body shrank around it.

## Settings added / changed by this work

- New: `maxHandVelocity` (exp 1, base 3.0; replaces four `< 15f` literals in
  `PlayerScaler`), `sphereResetJitter` (exp 1, base 0.1; replaces
  `Random.Range(-0.5f, 0.5f)`), `bodySpawnSize` (exp 1, base 0.2; was the inline
  `Set _Size 1.0` in `BodyEffects.vfx`), `handVfx` (`HandVfxSettings`),
  `settingsVersion`.
- Menu rows added: the above, `Maximum Unscaled Size` (was missing), and eight
  `Hand VFX - …` groups after Animation. `Boundary Distance Multiplier` was
  relabelled `Added Boundary Distance (×s)` (it is added, not multiplied).
- `pushForce` is exp 2; its companion `handPushScaler` is a plain multiplier
  (exp 0). Only `pushForce` scales.
- `boundaryOutwardDrag` is exp 1 (`AddForce(-v·k)`, see audit §0). The old
  hand-made `Default_Scaled.json` had this one wrong; the migration got it right.

## Migration (done — kept for reference and for stray v0 files)

- `RuntimeSceneSettings.settingsVersion` defaults to `0` (a legacy JSON without
  the key must read as legacy). `CopyInspectorToRuntime`, `MergeSceneSettings`,
  `CopySceneSettings` and `CopyPostProcessingSettings` stamp `1`.
- `EnergyBall/Migrate Scene Profiles To Base`: for every
  `Assets/StreamingAssets/SettingsProfiles/Scene/*.json` at version 0, divide each
  tabled field present in the file by `bodyScale^exp`
  (`BodyScaling.ConvertLegacyProfileInPlace` — keys the old file lacks already hold
  base C# defaults and are left alone), stamp version 1, write back. Then rewrite
  the `SceneController` inspector of `Assets/Energy Ball V3.unity` from the migrated
  `Default` and of `Assets/Testing/Dummy Scene.unity` from `Default_Dummy`
  (`CopyRuntimeToInspector`, save scene). PP profiles are never touched.
- `InGameSettingsMenu.LoadProfile` auto-converts a version-0 **Scene** profile in
  memory (logs once, does not write) so a stray legacy file can't load 5× values as
  base. Saving it persists v1.
- Validation performed: migrated `Default.json` ≡ the old hand-made
  `Default_Scaled.json` on every field except `bodyScale` and `boundaryOutwardDrag`
  (expected). `Default_Dummy` vs `Default_Dummy_Scaled` additionally differed on
  `torsoMaxForwardOffset` / `torsoOffsetFalloffDistance` (hand file had left them at
  C# defaults) and `baseZDepth` (hand file used 0) — the migrated values follow the
  audit and are the ones kept. Both `_Scaled` files were deleted (redundant: they
  were "Default with bodyScale = 1"). All eight Scene profiles were re-saved by
  the migration; nothing else was.

## Prefab / graph / scene changes

- `Hand_L.prefab`: VFX property-sheet overrides `noiseScale`, `noiseFrequency`,
  `noiseRoughness`, `noiseOctaves` cleared (settings are the sole authority; seeds
  came from those overrides). `Collider` child `BoxCollider.size` `(5,5,0.1)` →
  `(1,1,0.02)`; `PlayerScaler.GenerateScaleVectorFromHand` now uses
  `size.y × lossyScale.y`. Hand_R inherits. Unity re-serialised the stale
  hand-VFX property-sheet overrides in `Dummy Scene.unity` when the array
  shrank — those names no longer exist in the graph and are inert.
- `Player.prefab`: `PlayerConstructor.bodyVfx` wired to `VFX_Body`.
- `BodyEffects.vfx`: new exposed float `bodySpawnSize` (default 1.0) linked to Init
  block 2 `Set _Size`.
- `Dummy.prefab` / `Dummy Scene.unity`: `DummyTransformer.positionOffset` /
  `spaceBetweenHands` and `DummyHandController.speed` are now authored at 1×
  (÷5: offset y ±0.55, space 2, speed 2 in the scene; prefab space 1, speed 1).
  `DummyTransformer` lays hands out at `offset × bodyScale` with the depth axis
  at effective `baseZDepth` on `Start` (it now uses `SceneController.Instance`;
  the old `GetComponent<SceneController>()` was always null) and, when
  bodyScale/baseZDepth change at runtime, rescales the hands' *current* positions
  proportionally instead of snapping them back. `DummyHandController` multiplies
  its per-frame step by bodyScale.
- `BodyDepthOccluder.depthBias` is multiplied by bodyScale (`minDepth` is Kinect
  metres, untouched). Scene value is 0.
- Deleted: `SceneSettingsSO.cs` and `Assets/Config/*.asset` (nothing else
  referenced the script GUID).
- Left alone on purpose: HandEffects op22 `÷5`, the Secondary-Attractor inline
  `Sphere.radius`, the Turbulence `FieldTransform` (scaling it *and* intensity /
  frequency would compound to s²), `lifeRange` (orphaned).

## Verified (Dummy Scene, play mode, 2026-08-17)

Load `Default_Dummy` (bodyScale 5) → TD1 `localScale` 1.0, `positionAmount` 0.3,
hand `Collider` 5, dummy hands `(±5, 2.75, 5)`, metaball volume at z = 5. Set
`bodyScale = 1` (nothing else) → TD1 0.2, `positionAmount` 0.06, `Collider` 1, SA
0.02, hands `(±1, 0.55, 1)`, volume z = 1, `unscaledSize` ÷5; back to 5 restores
everything. No console errors/warnings (every `[VfxProperty]` name resolved).
Legacy (v0) profile loads converted; save writes v1 with `handVfx` and matches the
migration output byte-for-value; PP profile loads leave scene values untouched.

## Gotchas (carry these)

- Consumers must read `SceneController.CurrentSettings` / `GetRuntimeSettings()`
  (effective). `settingsMenu.GetCurrentSettings()` is the **base** object.
- `InGameSettingsMenu.UpdateSettingsFromInspector` replaces the menu's
  `runtimeSettings` with a `DeepCopy` — re-fetch anything held by reference after
  `SyncInspectorToRuntime`.
- JsonUtility: missing keys keep C# defaults (which must therefore be base values);
  nested `[Serializable]` classes and `Vector2/3` serialise fine; it can't write
  `null` for a class field, so PP profiles carry a default `handVfx` block that
  `MergePostProcessingSettings` ignores.
- `VisualEffect.Set*` on a name the compiled graph lacks logs an error → always
  `Has*`-guard (the applier does). After commit-jumping across `.vfx`/prefab
  changes: reimport the VFX assets, `Edit/VFX/Rebuild And Save All VFX Graphs`,
  reimport `Assets/Prefabs/Player/*.prefab`.
- Play-mode verification through unity-cli: the game loop only advances while the
  Editor window is focused (`Run In Background` is off) — focus it (e.g.
  `AppActivate`) before reading frame-driven values; `set_ui_element_value` can't
  drive `FloatField`s — set `SceneController` inspector fields with
  `set_component_field … runtime:true` (goes through `OnValidate` → rebuild) and
  drive the profile `DropdownField`s by name.
