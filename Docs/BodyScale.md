# `bodyScale`: why settings scale the way they do

The reference for the base → effective settings system: the invariant, how the
scaling exponents were derived, the VFX-specific scaling rules, and the traps
that have bitten before. For the *how-to* of adding a setting (fields, menu
rows, profiles) see `Docs/In-Game Menu/Adding-New-Settings-To-Menu.md`. For the
moving parts (`BodyScaling`, `PlayerScaleApplier`, the working set) see the
Settings section of `CLAUDE.md`.

## The invariant

> Profiles, the `SceneController` inspector and the in-game menu store **base
> values at bodyScale = 1**. Every scale-dependent field carries an exponent.
> Effective values are derived once per settings change as `base × bodyScale^exp`.
> Changing `bodyScale` — and nothing else — leaves gameplay and look identical
> relative to body size, for newly spawned players *and* for players already alive.

| Kind of value | `[BodyScaled(exp)]` |
|---|---|
| Lengths, distances, velocities, per-frame displacements | `1` |
| Rigidbody forces fed to `AddForce` | `2` |
| Spatial frequencies (noise frequency) | `-1` |
| Hand-VFX "forces" (Conform / Turbulence intensities) | `1` (see below) |
| Time, durations, per-second rates, ratios, curves, bools, counts, colors | none |

`bodyScale` itself is never scaled; zero or negative is treated as 1.

## Where the exponents come from

Every rule below was derived from the code, not assumed. Use the same reasoning
when a new value doesn't obviously fit a row of the table.

- **Positions** are `joint.Position × bodyScale` (`SceneController.GetVector3FromJoint`).
  The camera sits at the origin, so the world scales about the camera and
  perspective is unchanged. Everything a player sees is a length → exp 1.
- **Time is not scaled.** Delays, durations, curve inputs, per-second rates and
  animation speeds → exp 0. Velocities are length / time → exp 1.
- **Rigidbody mass scales with the sphere**: `PlayerConstructor.SetMass()` sets
  `sphere.mass = avg(sphere.localScale)`, so mass ∝ s. For identical timing we
  need acceleration ∝ s, so every force fed to `AddForce` must scale as
  `F = m·a ∝ s²` → exp 2 (`pushForce`, `maxTowardsForce`, `maxAwayFromForce`, …).
- **Gravity** `F = g·m₁m₂/r²` with m ∝ s and r ∝ s gives F ∝ g. For F ∝ s² we
  need `g` at exp 2.
- **Unity `linearDamping`** (`minDrag` / `maxDrag`) is a mass-independent
  per-second decay `v *= 1/(1 + drag·dt)` → exp 0.
- **`boundaryOutwardDrag`** is *not* Unity drag: it is `AddForce(-v × k)`. With
  v ∝ s and m ∝ s, `a = -k·v/m ∝ k`, but we need a ∝ s → exp 1. Same-named
  things can scale differently; check what the code does with the number.
- **`stopVelocity`** compares a relative velocity → exp 1. The stop force it gates
  is `-m·Δv/dt`, already ∝ s², so no setting is involved.
- **Metaball field**: `influence = (radius − d)/radius` is dimensionless and the
  voxel count is fixed, so `gridScale` (world size of a voxel) is a length →
  exp 1. `targetValue` and the SDF resolution are scale-free.
- **Hand VFX "forces"** (`*AttractionForce`, `*StickForce`, `turbulenceIntensity`,
  `cHatNoiseAmp`) are exp 1, not 2: the Conform and Turbulence blocks work in
  velocity / acceleration units on unit-mass particles.
- **Ratios on runtime-scaled inputs** need no exponent. `vfxSphereCollisionScaleMult`,
  `collisionDetectionScaleMult` and `tangentialDampingFade` multiply the runtime
  `vfxSphere.scale`, which already carries the body scale; giving them an
  exponent too would scale twice.

## HandEffects.vfx scaling rules (source-verified against VFX Graph 17.3)

The graph's VisualEffect objects have identity transforms, so graph-local units
equal world units. Values that are driven from C# (`handPos`, `vfxSphere`,
`sdfScale`, `sdfTexture`, `zDepth`, `TD1`/`TD2`, `isInBounds`,
`vfxSphereVelocity`) arrive already world-scaled and need nothing in the graph.
The three collision proxies (play-bounds box, ball SDF collision, trigger
sphere) are composed at runtime from `sdfScale` / `vfxSphere` and the ratio
settings above.

- **Conform To Sphere / SDF** (`attractionSpeed`, `attractionForce`,
  `stickDistance`, `stickForce`): the block is a velocity-target controller
  (`tgtSpeed = attractionSpeed × smoothstep(0, 2·stickDistance, |distToSurface|)`)
  with an acceleration clamp (`dt × lerp(stickForce, attractionForce, ratio) / mass`).
  All four are homogeneous degree 1, so linear ×s gives exactly similar
  trajectories with identical timing, provided mass is untouched and drag (a 1/T
  rate) is not scaled. The SDF variant converts field-local distance to world via
  the FieldTransform scale and inherits the same rules; its internal 0.01 gradient
  step is in normalized texture coordinates and is scale-invariant.
- **Curl noise** (`noiseScale` → amplitude, `noiseFrequency` → frequency):
  amplitude is a pure linear post-multiply on the output → ×s; frequency
  multiplies only the input coordinate → ×1/s; octaves, roughness and lacunarity
  are dimensionless. The hardcoded decorrelation offsets in `VFXNoise.hlsl`
  mean a rescaled world samples a statistically identical but not literally
  identical noise realization; accepted as cosmetic.
- **Turbulence block**: it applies its FieldTransform inverse to the sample
  position and forward (including scale) to the output vector. Two valid scaling
  schemes exist; the project uses the second. **Never combine them**, that
  compounds to an s² error.
  - *Exact:* scale the block's FieldTransform position and scale ×s; leave
    intensity and frequency alone (zero realization drift).
  - *Equivalent (what is plumbed, via the exposed `turbulenceIntensity` /
    `turbulenceFrequency`):* intensity ×s, frequency ×1/s, transform fixed.
  - The Relative-mode `Drag` input is a rate (1/T) and is never scaled.
- **Keijiro subgraphs** (`DF Noise Field 3D`, `DF Noise Turbulence`) were opened
  and verified: Amplitude is a final multiply, Frequency multiplies the
  coordinate, Scroll adds coordinate-units per second before the frequency, and
  the Turbulence block wraps the field in a Force (Absolute), making its
  Amplitude an acceleration.
- **`lengthScaler`** is ×1/s even though it looks like a multiplier: it converts a
  world velocity (×s) into the dimensionless `_Scale.y` stretch. `minStretchLength`,
  the floor on that stretch, stays unscaled (the ×s already lives in `_Size`).
- **Secondary Attractor `Divide` by literal 5** (`vfxSphere.z ÷ 5`, feeding the
  radius / lerp chain): looks like a bodyScale-5 assumption but is not. Reviewed
  by the author on 2026-08-13 and confirmed to work at any world scale. **Do not
  change it.**

Auditing the graph with `vfx_describe_graph`: compound slots (Sphere,
Transform, Vector2/3 parameters) report their inline default with
`hasLink: false` even when their sub-slots *are* linked. Confirm from the
linking side (operator / parameter output links) or the asset YAML before
calling a value "inline".

## Gotchas

- Consumers must read `SceneController.CurrentSettings` / `GetRuntimeSettings()`
  (effective). `settingsMenu.GetCurrentSettings()` is the **base** object.
  Nothing writes into the effective object; mutate the base and let
  `OnSettingsChanged` rebuild. `CurrentSettings` rebuilds lazily if read before
  `Awake` finished.
- The rebuild allocates (`DeepCopy` of curves and arrays) once per settings
  change. The runtime curve editor fires `OnSettingsChanged` on every drag step,
  so dragging a curve rebuilds per step; this stays well under a millisecond and
  was accepted rather than adding a per-frame path.
- `InGameSettingsMenu.UpdateSettingsFromInspector` replaces the menu's
  `runtimeSettings` with a `DeepCopy`; re-fetch anything held by reference after
  `SyncInspectorToRuntime`.
- JsonUtility: missing keys keep the C# defaults, which must therefore be base
  values. Nested `[Serializable]` classes and `Vector2/3` serialize fine. It cannot
  write `null` for a class field, so post-processing profiles carry a default
  `handVfx` block that `MergePostProcessingSettings` ignores.
- `VisualEffect.Set*` on a name the compiled graph lacks logs an error, so always
  `Has*`-guard (the applier does). After commit-jumping across `.vfx` / prefab
  changes: reimport the VFX assets, run `Edit/VFX/Rebuild And Save All VFX Graphs`,
  and reimport `Assets/Prefabs/Player/*.prefab`.
- `PlayerScaleApplier.ApplyTransforms` scales TD1/TD2, the SA debug spheres and
  the hand `Collider` child as unit-scale siblings under the hand (TD1/TD2 sit
  under a unit-scale "Trail Distorters" container). None compounds with another;
  keep it that way. The hand prefab's `BoxCollider.size` is authored at 1×
  (`(1, 1, 0.02)`) and the collider transform's `localScale` carries `bodyScale`.
- `PlayerScaleApplier.RescaleLiveState(ratio)` runs when the effective bodyScale
  changes while players exist: `unscaledSize`, `sphere.position`,
  `linearVelocity`, hand previous positions and `metaballRadiusAtAnimationStart`
  all follow (the world scales about the origin / camera, exactly like Kinect
  joints). Without it the ball keeps its old world size and position while the
  body shrinks around it.
- Play-mode verification through unity-cli: the game loop only advances while
  the Editor window is focused (`Run In Background` is off). `set_ui_element_value`
  can't drive `FloatField`s; set `SceneController` inspector fields with
  `set_component_field … runtime:true` (goes through `OnValidate` → rebuild) and
  drive the profile `DropdownField`s by name.
- Legacy profiles: `settingsVersion` 0 files hold effective values from the
  bodyScale-5 era and are auto-converted on Scene load;
  `EnergyBall/Migrate Scene Profiles To Base` rewrites them on disk. Saves stamp
  version 1.
