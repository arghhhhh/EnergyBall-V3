# Audit: C#-side scene settings vs. `bodyScale`

Companion to `Docs/HandEffects-scale-audit.md` (which covers the HandEffects
graph). This one classifies every **`RuntimeSceneSettings` field** and every
**scale-dependent constant outside the settings** by how it must change when the
world scale changes by factor `s`, so that "change `bodyScale`, nothing else"
leaves gameplay and look identical relative to body size.

Convention used everywhere below: `effective = base × bodyScale^exp`, where
`base` is the value at bodyScale = 1 (real Kinect meters / seconds).

## 0. Why the exponents are what they are (derived from the code)

- **Positions** are `joint.Position × bodyScale` (`SceneController.
  GetVector3FromJoint`); the camera sits at the origin, so the world scales
  about the camera and perspective is unchanged. Everything a player sees is
  a length → `exp 1`.
- **Time is not scaled.** Delays, durations, curve inputs, per-second rates,
  animation speeds → `exp 0`. Velocities are length/time → `exp 1`.
- **Rigidbody mass scales with the sphere**: `PlayerConstructor.SetMass()` sets
  `sphere.mass = avg(sphere.localScale)` → mass ∝ s. For identical timing we
  need acceleration ∝ s, so every **force** fed to `AddForce` must scale as
  `F = m·a ∝ s²` → `exp 2`. This is why the hand-made `Default_Scaled.json`
  divides `g`, `pushForce`, `maxTowardsForce`, `maxAwayFromForce` by 25, not 5
  — and it is correct.
- **Gravity** `F = g·m₁m₂/r²` with m ∝ s and r ∝ s ⇒ F ∝ g·s²/s² = g ⇒ for
  F ∝ s² we need **`g` exp 2** (matches `Default_Scaled`).
- **Rigidbody `linearDamping`** (`minDrag`/`maxDrag`) is Unity's per-second
  velocity decay `v *= 1/(1 + drag·dt)` — mass-independent rate → `exp 0`.
- **`boundaryOutwardDrag`** is NOT Unity drag: `AddForce(-v × k)`. With v ∝ s
  and m ∝ s, `a = -k·v/m ∝ k` — but we need a ∝ s ⇒ **`k` exp 1**.
  `Default_Scaled.json` left it at 20 (same as `Default`) — that is a bug in
  the hand conversion (over-brakes 5× at 1×). Migration must divide it by 5.
- **`stopVelocity`** compares a relative velocity → exp 1. The stop force it
  gates is `-m·Δv/dt` (already ∝ s²) — no setting involved.
- **Metaball field** `influence = (radius − d)/radius` is dimensionless and the
  voxel count is fixed, so `gridScale` (world size of a voxel) is a length →
  exp 1; `targetValue` / SDF resolution are scale-free.
- **Hand VFX**: see the HandEffects audit; summary in §3.

## 1. `RuntimeSceneSettings` fields (scene profile)

`Default.json` values are the current 5×-tuned effective values; "base @1×" is
what the migrated profile stores (= Default ÷ 5^exp). Verified against the
consumer code named in the last column.

### exp 1 — lengths, distances, velocities, per-frame displacements

| Field | Default (5×) | base @1× | Consumer / role |
|---|---|---|---|
| `stopGravityDistance` | 0.12 | 0.024 | `GravityForce` distance gate |
| `stopMovingDistance` | 0.05 | 0.01 | `GravityForce` distance gate |
| `stopVelocity` | 0.5 | 0.1 | `GravityForce` relative-velocity gate |
| `addedBoundaryDistance` | 1.3 | 0.26 | `BoundaryForce` — added to grid extents (world units) |
| `boundaryOutwardDrag` | 20 | **4** | `BoundaryForce` `AddForce(-v·k)` — see §0 (hand conversion had this wrong) |
| `torsoMaxForwardOffset` | 1.0 | 0.2 | `HandForce.ApplyTorsoForwardOffset` (z offset) |
| `torsoOffsetFalloffDistance` | 2.0 | 0.4 | `HandForce.ApplyTorsoForwardOffset` (z distance) |
| `alignmentVectorStrengthScaler` | 0.35 | 0.07 | `HandForce` — normalized vector × scaler → offset length |
| `prayToActivateDistance` | 0.7 | 0.14 | `HandEffects` hand-to-hand distance |
| `minimumUnscaledSize` | 1.5 | 0.3 | `PlayerScaler` clamp on `unscaledSize` (sphere scale) |
| `maximumUnscaledSize` | 3.0 | 0.6 | `PlayerScaler` clamp |
| `minHandDisplacementPerFrame` | 0.05 | 0.01 | `PlayerScaler` length/frame gate |
| `maxDistanceBetweenHands` | 8.0 | 1.6 | `HandForce` (InverseLerp range, drag remap), `PlayerScaler` |
| `baseZDepth` | 10 | 2 | `MetaballsToSDF` volume z, `BoundaryForce` centre, VFX `zDepth`, `PlayerConstructor` bounds, `DummyTransformer` |
| `gridScale` | 0.3 | 0.06 | `MetaballsToSDF` voxel size (fixed 64×32×64 voxels) |
| `defaultUnscaledSize` | 2.5 | 0.5 | initial sphere scale |
| `maxDistanceFromCamera` | 13 | 2.6 | currently only in commented-out code; keep exp 1 for when it's used |
| `metaballRadiusAnimationStartSize` | 0.1 | 0.02 | `PlayerConstructor` metaball radius lerp start (world radius) |

### exp 2 — forces (mass ∝ s, see §0)

| Field | Default (5×) | base @1× | Consumer |
|---|---|---|---|
| `g` | 12 | 0.48 | `GravityForce` `g·m₁m₂/r²` |
| `maxTowardsForce` | 10 | 0.4 | `GravityForce` clamp |
| `maxAwayFromForce` | 25 | 1.0 | `GravityForce` clamp |
| `pushForce` | 70 | 2.8 | `HandForce` `AddForce(pushForce·damper·dir)` — its companion `handPushScaler` is a plain multiplier (exp 0, §below); scale only `pushForce` |

### exp 0 — dimensionless, time-domain, rates, bools, curves, counts

| Field | Value | Why scale-free |
|---|---|---|
| `gravityForceDamper` | 0.95 | multiplier on the towards-clamp |
| `attractionRadiusMultiplier` | 3.5 | multiplies sphere scale (already a length) |
| `singleHandOpenForceDamper` | 0.75 | force multiplier |
| `handPushScaler` | 10 | force multiplier |
| `minDrag`, `maxDrag` | 0.25 / 1.0 | Unity `linearDamping` (1/T rate) |
| `forceToMiddle`, `alignmentVectorStrength`, `distanceDamper`, `metaballRadiusAnimationCurve` | curves | inputs are normalized 0–1 |
| `outOfBoundsResetDelay`, `particleInitializationDelay`, `initializationResetDelay`, `singleHandOpenThreshold`, `singleHandForceLerpDuration`, `metaballRadiusAnimationDuration` | seconds | time |
| `initializationSpeed` | 0.05 | animator speed multiplier |
| `pulseAmount` | 2 | `avg(unscaledSize)·pulseAmount/10` — multiplier on a length |
| `pulseSpeed`, `pulseFreqs`, `graphLimit` | 3 / [0.3,0.8,2,3] / 3.883 | temporal frequencies / normalization |
| `pulseScaleDamper` | 0.3 | multiplier on a displacement (already ∝ s) |
| `mergeSizeScalerDamper` | 0.15 | multiplier on a length lerp |
| `singleHandScaling`, `prayToActivate`, `dummyOnlyMode`, `drawSkeleton`, `customColors`, `useTrackingStateColors`, all `show*` debug bools | bools | — |
| `bodyScale` | 5 | the knob itself |
| Bloom / lens flare / lens distortion / colour / white-balance fields | — | post-processing, screen-space |

## 2. Scale-dependent things that are NOT settings today (must be fixed or the invariant breaks)

| Where | What | Rule | Proposed fix |
|---|---|---|---|
| `PlayerScaler.ScaleSetup` | `leftHandVelocity.magnitude < 15f` (×4 sites) — hand-velocity sanity gate, tuned at 5× | exp 1 | new setting `maxHandVelocity` (base 3.0) |
| `PlayerConstructor.ResetSphereToHandMidpoint` | `Random.Range(-0.5f, 0.5f)` jitter | exp 1 | new setting `sphereResetJitter` (base 0.1) or `0.1f × bodyScale` |
| `Hand_L.prefab` → `Collider` child `BoxCollider.size = (5, 5, 0.1)` (Hand_R inherits) | hand raycast target used by `PlayerScaler.CheckRaycast`; `GenerateScaleVectorFromHand` also reads `BoxCollider.size.y` directly | exp 1 | prefab size → (1, 1, 0.02) and set the collider transform's `localScale = bodyScale` on spawn (per-player scale step); change the `size.y` read to `bounds.extents`/`size.y × lossyScale.y` |
| `Hand_L.prefab` → `SA` child (Secondary Attractor debug sphere, localScale 0.1), `TD1`/`TD2` (0.1) | debug meshes only | exp 1 | scale with bodyScale in the same per-player step (TD: `tdRadius × 2 × bodyScale`, matches the conform radius) |
| `Hand_L.prefab` → `TD1`/`TD2` `BrownianMotion.positionAmount` (0.3) | wander amplitude (length) | exp 1 | setting `tdWanderAmount` (base 0.06), written to the four components; `rotationAmount`/`frequency` stay |
| `Hand_L.prefab` → `VFX` property-sheet overrides (`noiseScale 0.1`, `noiseFrequency 0.9`, `noiseRoughness 0.5`, `noiseOctaves 1`) | second source of truth for graph values | — | **clear them**; the settings become the sole authority (seed the settings from these effective values, incl. `noiseOctaves = 1`) |
| `Joint.prefab` (localScale 0.1), skeleton `LineRenderer` width | skeleton debug visuals | exp 1 | optional: scale in the per-player step when `drawSkeleton` |
| `BodyEffects.vfx` Init `Set _Size = 1.0` (single-burst spawn flash on `VFX_Body`) | inline length | exp 1 | expose as `bodySpawnSize` (graph: convert inline → exposed float) and drive like the hand VFX values, or accept the drift (it's a 0.2 s flash) |
| `BodyDepthOccluder.depthBias` (component field, world units; `minDepth` is Kinect meters, pre-scale) | occluder z bias | exp 1 | multiply by `bodyScale` in `BodyDepthOccluder` (it already reads `bodyScale`) |
| Camera near/far clip | — | — | 1× world at z≈2 is inside default clip planes; no change |
| `Assets/Testing/Dummy Scene.unity` dummy positions, `DummyHandController.speed` (per-frame step `speed/100`) | dev-only, but the acceptance test runs on dummies | exp 1 | **in scope** (handoff §5b): multiply the dummy hand step by `bodyScale`; author dummy positions at 1× and scale at runtime; `DummyTransformer` already follows `baseZDepth` |

## 3. Hand VFX values (from `HandEffects-scale-audit.md`, restated as fields)

All rules were source-verified in that doc; only the storage changes (typed
fields, base at 1×). Seed = current effective value ÷ 5^exp, where "current
effective" is the graph default **except** the four prefab-overridden noise
values (use the override).

| exp 1 (float) | base @1× |
|---|---|
| `spawnSphereRadius` 0.06→0.012 · `tdRadius` 0.5→0.1 · `tdStickDistance` 0.1→0.02 · `saStickDistance` 0.1→0.02 · `mainStickDistance` 0.1→0.02 · `saMinRadius` 0→0 · `noiseScale` **0.1**→0.02 · `cHatSize` 0.025→0.005 · `oHatSize` 0.02→0.004 · `oHatNoiseAmp` 0.04→0.008 · `lifetimeRemapMaxDist` 3→0.6 · `spawnVeloSpread` 0.5→0.1 · `saAttractionSpeed` 4.75→0.95 · `saAttractionForce` 50→10 · `tdAttractionForce` 10→2 · `tdAttractionSpeed` 200→40 · `mainAttractionSpeed` 4.75→0.95 · `mainAttractionForce` 215→43 · `mainStickForce` 2→0.4 · `seekStrength` 5→1 · `tdStickForce` 5→1 · `saStickForce` 5→1 · `turbulenceIntensity` 5→1 · `cHatNoiseAmp` 5→1 · `cHatNoiseYScroll` 0.4→0.08 · `oHatNoiseYScroll` 1→0.2 · `cHatSpawnVeloSphereRadius` 5→1 · `snareSpawnVeloSphereRadius` 1→0.2 · `tdWanderAmount` 0.3→0.06 | |
| **exp 1 (Vector2)**: `sizeRange` (0.04,0.07)→(0.008,0.014) · `snareSizeRange` (0.08,0.15)→(0.016,0.03) · `snareRadiusRandRange` (−0.1,0.2)→(−0.02,0.04); **(Vector3)** `boundsPadding` (0,0,0) | |
| **exp −1**: `noiseFrequency` **0.9**→4.5 · `turbulenceFrequency` 1→5 · `cHatNoiseFreq` 4→20 · `oHatNoiseFreq` 2→10 · `lengthScaler` 0.25→1.25 | |
| **exp 0**: `spawnRate` 1500 (int) · `noiseRoughness` **0.5** · `noiseOctaves` **1** (int) · `minStretchLength` 0.01 | |

Note the VFX "forces" (`*AttractionForce`, `*StickForce`, `turbulenceIntensity`,
`cHatNoiseAmp`) are exp 1, not 2: the Conform/Turbulence blocks work in
velocity/acceleration units on unit-mass particles (verified in the HandEffects
audit), unlike the rigidbody forces in §1.

## 4. Sanity check against `Default_Scaled.json`

Every field the hand conversion touched agrees with this table (0.2× for exp 1,
0.04× for exp 2), except `boundaryOutwardDrag` (left at 20, should be 4).
Fields it left alone are all exp 0 here. So `Default_Scaled` is a trustworthy
1× reference apart from that one value, and the migration can be validated by
diffing the migrated `Default.json` (bodyScale 5) against `Default_Scaled.json`
(bodyScale 1): all fields except `bodyScale` and `boundaryOutwardDrag` must
match to float precision.

*Derived from the code on 2026-08-16 (`GravityForce`, `HandForce`,
`BoundaryForce`, `PlayerScaler`, `HandEffects`, `PlayerConstructor`,
`MetaballsToSDF`, `MetaballsGenerator.compute`, `BodyDepthOccluder`,
prefabs under `Assets/Prefabs/Player/`, `BodyEffects.vfx`).*
