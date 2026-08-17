# Audit: `Assets/VFX/HandEffects.vfx` — Spatial-Unit Properties for World-Scale Changes

Context: the project multiplies all Kinect-derived positions by the `bodyScale`
setting; the VFX objects have identity transforms, so graph-local units ==
world units. This audit classifies every graph value by how it must change
when the world scale changes by factor `s` (e.g. bodyScale 5 → 1 means
`s = 1/5`). It is the checklist for a future auto-scaling mechanism.

Method note: several composite slots (`Sphere`, `OrientedBox`, `Transform`)
report a default literal at the block level even when a sub-field is driven by
a link; every block was cross-referenced against parameter/operator output
links to distinguish genuinely inline values from runtime-driven ones.

---

## 1. LENGTH (scale × s) — sizes, radii, distances, positions, spawn bounds

| Name | Location | Value | Rule |
|---|---|---|---|
| `spawnSphereRadius` (exposed) | Init(ctx1) block0 `PositionShape\|Sphere` `radius` | 0.06 | × s |
| `tdRadius` (exposed) | Update(ctx2) blocks 3/4 `ConformToSphere` `radius` (TD1/TD2) | 0.5 | × s |
| `tdStickDistance` (exposed) | Update(ctx2) blocks 3/4 `ConformToSphere` `stickDistance` (TD1/TD2) | 0.1 | × s |
| `saStickDistance` (exposed) | Update(ctx2) block5 `ConformToSphere` `stickDistance` | 0.1 | × s |
| `mainStickDistance` (exposed) | Update(ctx2) block6 `ConformToSDF` `stickDistance` | 0.1 | × s |
| `saMinRadius` (exposed) | op23 Lerp `y` → op54 Switch → Secondary-Attractor radius chain | 0 | × s |
| `noiseScale` (exposed, curl amplitude) | op94 `CurlNoise` `amplitude` → Output(ctx16) block0 `\|Add\|_Position` | 0.8 | × s |
| Secondary Attractor `Sphere.radius` (inline) | Update(ctx2) block5 | 1.0 | none — author-exempt, leave as is |
| Trigger sphere base `sphere.radius` (inline) | Update(ctx2) block9 `CollisionShape\|Trigger Sphere` (transform.scale is runtime-driven separately) | 0.5 | × s |
| `boundsPadding` (exposed, Vector3) | wired to the `boundsPadding` context slot of all four Init contexts (ctx1/ctx4/ctx8/ctx13) | (0,0,0) (inert) | × s (all components) |
| `cHatSize` (exposed) | Init(ctx4) block3 `\|Set\|_Size` | 0.025 | × s |
| `oHatSize` (exposed) | Init(ctx8) block1 `\|Set\|_Size` | 0.02 | × s |
| `snareSizeRange` (exposed, Vector2) | Init(ctx13) block1 `\|Multiply\|_Size\|Random Uniform` A/B | (0.08, 0.15) | × s (both components) |
| `sizeRange` (exposed, Vector2) | Output(ctx16) block1 `\|Set\|_Size\|Random Per Component` A/B | (0.04, 0.07) | × s (both components) |
| `snareRadiusRandRange` (exposed, Vector2) | Random range added to alpha → Init(ctx13) block0 `PositionShape\|Circle` `radius` | (−0.1, 0.2) | × s (both components) |
| `lifetimeRemapMaxDist` (exposed) | `Remap` `oldRangeMax` — hand↔sphere distance threshold (input = `Distance(handPos, vfxSphere.position)`) → Init(ctx1) `_Lifetime` | 3 | × s |
| `oHatNoiseAmp` (exposed) | `DF Noise Field 3D` `Amplitude` → Update(ctx9) block0 `\|Set\|_Position` | 0.04 | × s — carries the unit of the whole OHat offset chain (the former ×4 gain block was folded into this value) |

## 2. VELOCITY / ACCEL (scale × s) — speeds, attraction "forces" (accelerations), turbulence intensity

| Name | Location | Value | Rule |
|---|---|---|---|
| `spawnVeloSpread` (exposed) | op6 Lerp → Init(ctx1) block3 `_Velocity` | 0.5 | × s |
| `saAttractionSpeed` (exposed) | Update(ctx2) block5 `attractionSpeed` | 4.75 | × s |
| `saAttractionForce` (exposed) | Update(ctx2) block5 `attractionForce` | 50 | × s |
| `tdAttractionForce` (exposed) | op20 → Update(ctx2) blocks 3/4 `attractionForce` | 10 | × s |
| `tdAttractionSpeed` (exposed) | op75 → Update(ctx2) blocks 3/4 `attractionSpeed` | 200 | × s |
| `mainAttractionSpeed` (exposed) | Update(ctx2) block6 `ConformToSDF` `attractionSpeed` | 4.75 | × s |
| `mainAttractionForce` (exposed) | Update(ctx2) block6 `attractionForce` (also op48 Switch) | 215 | × s |
| `mainStickForce` (exposed) | Update(ctx2) block6 `stickForce` | 2 | × s |
| `seekStrength` (exposed) | op110 → op111 → Update(ctx2) block2 `\|Add\|_Velocity` | 5 | × s |
| `tdStickForce` (exposed) | Update(ctx2) blocks 3/4 `ConformToSphere` `stickForce` (TD1/TD2) | 5 | × s |
| `saStickForce` (exposed) | Update(ctx2) block5 `ConformToSphere` `stickForce` | 5 | × s |
| `turbulenceIntensity` (exposed) | Update(ctx2) block8 `Turbulence` `Intensity` | 5 | × s |
| `cHatNoiseAmp` (exposed) | Update(ctx5) block0 `DF Noise Turbulence` `Amplitude` | 5 | × s |
| `cHatNoiseYScroll` (exposed) | Update(ctx5) block0 `DF Noise Turbulence` `Scroll.y` | 0.4 | × s |
| `oHatNoiseYScroll` (exposed) | `DF Noise Field 3D` `Scroll.y` | 1 | × s |
| `cHatSpawnVeloSphereRadius` (exposed) | `Random Inside Sphere` `radius` → Init(ctx4) block4 `_Velocity` | 5 | × s |
| `snareSpawnVeloSphereRadius` (exposed) | `Random Inside Sphere` `radius` → Init(ctx13) block4 `_Velocity` | 1 | × s |

## 3. SPATIAL FREQUENCY (scale × 1/s)

| Name | Location | Value | Rule |
|---|---|---|---|
| `noiseFrequency` (exposed) | op94 `CurlNoise` `frequency` | 2.33 | × 1/s |
| `lengthScaler` (exposed) | multiplies velocity magnitude → `_Scale.y` stretch multiplier, Output(ctx16) block4 | 0.25 | × 1/s — units are time/length: converts a world velocity (×s) into the dimensionless `_Scale.y`; rendered stretch = `_Size × _Scale.y`, and `_Size` (`sizeRange`) already carries the ×s |
| `turbulenceFrequency` (exposed) | Update(ctx2) block8 | 1 | × 1/s |
| `cHatNoiseFreq` (exposed) | Update(ctx5) block0 `DF Noise Turbulence` `Frequency` | 4 | × 1/s |
| `oHatNoiseFreq` (exposed) | `DF Noise Field 3D` `Frequency` | 2 | × 1/s |
| Turbulence `lacunarity` | Update(ctx2) block8 | 2 | none — per-octave ratio, dimensionless |

## 4. SCALE-FREE — ratios, dampers, counts, times, bools, colors, normalized curves

| Name | Location | Value | Note |
|---|---|---|---|
| `flipVelo` | exposed | false | bool |
| `closeProgress` | exposed | 0–1 | normalized |
| `spawnRate` | exposed | 1500 | count/sec — author decision: never scaled or re-tuned across world scales |
| `vfxSphereCollisionScaleMult` | exposed | 0.93 | ratio on runtime `vfxSphere.scale` |
| `noiseRoughness` | exposed → CurlNoise | 0.75 | fractal gain |
| `noiseOctaves` | exposed → CurlNoise | 8 | count |
| `minStretchLength` (exposed) | `Maximum(velLen × lengthScaler, b)` `b` — floor on the `_Scale.y` stretch multiplier, Output(ctx16) block4 | 0.01 | none — `_Scale.y` is dimensionless (the ×s lives in `_Size` and `lengthScaler` ×1/s); scaling the floor too would give s² |
| `collisionDetectionScaleMult` | exposed | 1.25 | ratio on runtime `vfxSphere.scale` |
| `lifeRange` | exposed | {1,2} | **orphaned — no consumers found**; time-domain |
| `tangentialDamping` | op128 `× deltaTime` | 5 | per-second decay |
| `playerAuraBase` | exposed Gradient | — | color |
| Box collision `Bounce`/`Friction`/`LifetimeLoss` | Update(ctx2) block0 | 0/0/1 | dimensionless |
| SDF collision `Bounce`/`Friction`/`LifetimeLoss` | Update(ctx2) block7 | 0.1/0/0 | dimensionless |
| Turbulence `octaves`/`roughness` | Update(ctx2) block8 | 1 / 0.5 | count/ratio |
| CHat `Linear Drag` `dragCoefficient` | Update(ctx5) block1 | 4 | per-second |
| Lifetime randoms (ctx4: 0.1/0.2; ctx13: 0.1/0.3), burst Count/Delay (ctx7/11/12), exponential randoms (op60: 8,0,0.6; op71: 20,0,1) | various | — | time-domain/counts |
| Output blocks: `_Angle.Y=180`, Orient, Size-over-Life curves (ctx6/10/15/16), `alphaThreshold` 0.5 (ctx10), `flipBookSize {3,3}` (ctx16), colors/blends (ctx16 blocks 5–7) | Output contexts | — | normalized/color |
| OHat `\|Multiply\|_Position` driver (alpha) | Update(ctx9) block1 | — | none — dimensionless fade on the noise *offset* (handPos is added after, in block2); the unit lives in `oHatNoiseAmp` |
| OHat `\|Multiply\|_Size` driver (alpha) | Init(ctx8) block2 | — | dimensionless |
| `_Scale.XY` `x` | Output(ctx16) block4 | 0.2 | width:height ratio |
| Smoothstep/Compare thresholds (ops 28/54/55/84/106/107) | Heat-Seeking Steering etc. | — | normalized dot-products/ratios |

## 5. DRIVEN FROM C# — already world-scaled or handled by code; no graph change

`handPos`, `vfxSphere` (position/scale feed collision transforms in ctx2
blocks 0/7/9), `sdfScale` (→ ConformToSDF `FieldTransform.size` × 0.98),
`sdfTexture`, `zDepth` (ctx2 block0/block6 z + op92), `TD1`/`TD2`,
`isInBounds`, `vfxSphereVelocity` (tangential damping, op131).
Also runtime-composed — all three collision proxies are runtime-driven:
play-bounds box (block0) size = `sdfScale` × inline factor, center z =
`zDepth`; ball SDF collision (block7) size = `vfxSphere.scale ×
vfxSphereCollisionScaleMult`, center from `vfxSphere.position`; trigger
sphere (block9) transform scale = `vfxSphere.scale ×
collisionDetectionScaleMult`; tangential-damping threshold =
`vfxSphere.x × 0.5`.

---

## Resolved: hardcoded divisor `5` (op22) is intentional — DO NOT CHANGE

**Operator idx 22** (`Divide`, unnamed, Secondary Attractor group):
`a = vfxSphere.z ÷ b = 5 (inline literal)` → op23 Lerp → op54 Switch →
Secondary-Attractor radius/lerp chain. The audit originally flagged this as
a bodyScale-5 assumption. **The author reviewed it (2026-08-13) and confirmed
it works universally at ÷5 regardless of world scale — leave it as is.**

## Source-verified: Conform block ×s rules (2026-08-14)

The ×s rules for `attractionSpeed` / `attractionForce` / `stickDistance` /
`stickForce` on ConformToSphere and ConformToSDF were verified against the
VFX Graph 17.3 package HLSL (Editor/Models/Blocks/Implementations/Forces/):
the update is a velocity-target controller (`tgtSpeed = attractionSpeed ×
smoothstep(0, 2·stickDistance, |distToSurface|)`) with an acceleration clamp
(`deltaTime × lerp(stickForce, attractionForce, ratio) / mass`). All four are
homogeneous degree 1 → linear ×s gives exactly similar trajectories with
identical timing, provided mass is untouched and drag (a 1/T rate) is NOT
scaled. The SDF variant converts field-local distance to world via the
FieldTransform scale, so it inherits the same rules; its internal 0.01
gradient step is in normalized texture coords (scale-invariant).

## Source-verified: noise scaling rules (2026-08-14)

- **CurlNoise operator** (`noiseScale` → amplitude, `noiseFrequency` →
  frequency): confirmed against VFXNoise.hlsl. Amplitude is a pure linear
  post-multiply on the output (positional offset → ×s); frequency multiplies
  only the input coordinate (→ ×1/s); octaves/roughness/lacunarity
  dimensionless. Cosmetic caveat: hardcoded decorrelation offsets (+100/+200
  in coordinate space) mean a rescaled world samples a statistically
  identical but not literally identical noise realization.
- **Turbulence block**: the block applies its `FieldTransform` inverse to the
  sample position AND forward (incl. scale) to the output vector. Two valid
  scaling schemes — pick ONE:
  - *Exact:* scale the block's FieldTransform position+scale ×s; leave
    Intensity and frequency untouched (zero realization drift).
  - *Equivalent (currently plumbed via the exposed `turbulenceIntensity`/
    `turbulenceFrequency` params):* Intensity ×s, frequency ×1/s, transform
    fixed — statistically identical, cosmetic realization drift only.
  - **Never both** — that compounds to an s² error. The Relative-mode `Drag`
    input is a rate (1/T) and is never scaled.

(The Keijiro subgraph internals — `DF Noise Field 3D` and
`DF Noise Turbulence` — were subsequently opened and verified: Amplitude is
a pure final multiply, Frequency multiplies the coordinate, Scroll adds
coordinate-units/second pre-frequency, and the Turbulence block wraps the
field operator in a Force(Absolute), making its Amplitude an acceleration.
The tables' classifications are source-confirmed, not analogy.)

---

*Maintained against the live graph via `vfx_describe_graph`; last verified
2026-08-14. Caution for future auditors: compound slots (Sphere, Transform,
Vector2/3 params) report inline defaults with `hasLink: false` even when
their sub-slots ARE linked — always confirm from the linking side (operator/
parameter output links) or the asset YAML before calling a value "inline".*
