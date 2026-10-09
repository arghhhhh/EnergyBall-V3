# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository.

## Project Overview

EnergyBall-V3 is a Unity installation piece. A Kinect v2 sensor tracks people's
bodies; each tracked person becomes a floating "energy ball" (a metaball rendered
as a marching-cubes isosurface) that they push, pull, grow, and shrink with their
hands. Multiple players' balls attract each other under a custom gravity model.
It runs full-screen on Windows against a physical Kinect, but can also be driven
by "dummy" players for development without hardware.

## Environment

- **Unity**: `6000.3.25f1` (see `ProjectSettings/ProjectVersion.txt`). Unity 6.3.
- **Render pipeline**: URP 17.3 (`com.unity.render-pipelines.universal`).
- **Main scene**: `Assets/Energy Ball V3.unity`.
- **Platform**: Windows only (Kinect SDK v2 native plugins). Requires a physical
  Kinect v2 to track real bodies; use dummy players otherwise (see below).
- **Solution**: `EnergyBall-V3.sln`. Editor: Visual Studio (`.vscode/settings.json`).
- **Code style**: 4-space indent, format-on-save (`.editorconfig`, VS Code settings).

## Dependencies (how they're vendored)

Not everything comes from Package Manager — check the right place before assuming a
package is missing:

- **Package Manager** (`Packages/manifest.json`): URP, VFX Graph 17.3, Input System
  1.19, Timeline, Collections, Newtonsoft JSON, and Keijiro packages from the
  `jp.keijiro` scoped npm registry (`klak.motion`, `klutter-tools`, `metamesh`,
  `metawire`, `noiseshader`, `shadergraphassets`, `vfxgraphassets`).
- **Git dependency**: `com.maligan.unity-zed` (from GitHub).
- **Kinect SDK v2**: vendored in `Assets/Scripts/Kinect Standard Assets/`
  (`Windows.Kinect` namespace). Native plugins live in `Assets/Plugins/`
  (`Metro/`, `x86/`, `x86_64/`).
- **NaughtyAttributes**: vendored in `Assets/Added Packages/NaughtyAttributes/`
  (inspector decorators — `[BoxGroup]`, `[Foldout]`, etc. — used throughout).
- **unity-cli bridge**: embedded at `Packages/unity-cli-bridge/` (a VFX-Graph-capable
  fork). This is what backs the unity-cli automation tooling for this repo.

## Architecture

All gameplay code is in `Assets/Scripts/`. The metaball/mesh code sits in
`Assets/Scripts/Metaballs/` and `Assets/Scripts/MarchingCubes/` (NOT top-level
`Assets/`).

### Coordinator: `SceneController.cs`
Singleton (`SceneController.Instance`), `[DefaultExecutionOrder(-200)]`,
`[RequireComponent(typeof(MetaballsToSDF))]`. This is the spine of the app. Its
`FixedUpdate`:
1. Pulls Kinect bodies via `bodySourceManager.GetData()`.
2. Diffs tracked `TrackingId`s against the `Players` dictionary — creates a player
   for each new tracked body, removes players whose body is gone.
3. Updates each player's Kinect-driven data, then runs the per-player gameplay
   logic.
4. Runs `gravityForceController.ManageGravity()` for inter-player attraction.

Mouse input handled here: **left-click** deletes all bodies, **right-click**
reloads the scene. Honors `dummyOnlyMode` to skip Kinect entirely.

### Kinect input
- `BodySourceManager.cs` (`[RequireComponent(typeof(SceneController))]`): opens the
  Kinect sensor, color + body frame readers, exposes `Body[] GetData()`.
- `KinectManager.cs`: a simpler standalone sensor reader (color + body).

### Per-player entity: `PlayerConstructor.cs`
`[DefaultExecutionOrder(100)]`. One per tracked/dummy person. Holds the `Rigidbody`
sphere, hand objects/colliders, per-hand VFX (`leftHandVfx`/`rightHandVfx`), the
`metaballIndex` into the shared metaball field, hand states, and initialization
state. Players "activate" via a pray-to-activate gesture (hands brought together).

### Gameplay logic ("force" classes)
Plain C# classes (NOT MonoBehaviours) that grab `SceneController.Instance` and are
invoked each frame from the controller. Put new per-frame gameplay behavior here,
following the existing pattern:
- `HandForce.cs` — translates hand open/closed states into pushing/pulling the ball.
- `HandEffects.cs` — activation gesture, in-bounds handling, hand VFX flags.
- `GravityForce.cs` — pairwise attraction between all players' spheres.
- `PlayerScaler.cs` — grows/shrinks the ball based on hand-vs-body distances.
- `BoundaryForce.cs` / `BoundaryGizmos.cs` — keeps balls inside the play volume.

### Metaballs → SDF pipeline
- `Metaballs/MetaballsToSDF.cs` (`[RequireComponent]` MeshFilter+MeshRenderer):
  owns the `List<Metaball>` (position + radius) and the volume `ComputeShader`
  `Metaballs/MetaballsGenerator.compute` (default grid `64x32x64`, `gridScale`,
  `targetValue 0.26`, `triangleBudget` 65536).
- **SDF source** (`_sdfSource`, default `Analytic`): one dispatch of the
  `MetaballSdfGenerator` kernel writes the signed distance to the isosurface
  straight into a 3D RenderTexture (Newton step on the field, normalised like
  `MeshToSDFBaker`: distance ÷ largest box extent, negative inside). This is what
  the hand VFX graphs' ConformToSDF consumes. `MeshBake` is the legacy path
  (marching cubes → mesh → VFX `MeshToSDFBaker`), kept only for A/B comparison and
  far more expensive.
- `MarchingCubes/MeshBuilder.cs` + `MarchingCubes/MarchingCubes.compute` +
  `TriangleTable.cs`: triangulate the isosurface into a `Mesh`. Built lazily and
  only when consumed: the `showMetaballMesh` debug setting or `MeshBake` mode.
  The GPU triangle counter is read back asynchronously (callback form, lands
  ~2 frames later) and the submesh is trimmed to that count plus a margin, so
  the renderer and the baker only touch real triangles instead of the full
  `triangleBudget`; the clear kernel zeroes just the slack inside that range.
- GPU work is dirty-flagged: it only re-runs when a metaball moved/resized, a
  player joined/left, `gridScale` changed, or the debug mesh was toggled on.
  Metaball data is written from `FixedUpdate`, so extra rendered frames and idle
  time with no players cost nothing here. Hand graphs are bound to the SDF texture
  once per player (re-bound only if the texture, box size or `baseZDepth` change).
- Each player owns one metaball index; hands/body movement move the metaballs.

### VFX & post-processing
- VFX Graph assets in `Assets/VFX/` (`BodyEffects.vfx`, `HandEffects.vfx`,
  `Subgraphs/`), driven from `PlayerConstructor` via exposed properties/bools.
- Hand-VFX tuning lives in `HandVfxSettings.cs` (nested in the scene profile as
  `handVfx`, one `[VfxProperty("graphName")]` field per exposed value) and is pushed
  to both hand graphs by `PlayerScaleApplier.cs` on spawn and on every settings
  change. `bodySpawnSize` (top-level setting) drives `BodyEffects.vfx` the same way.
- **Split post-processing (main scene).** The particle layer and the camera feed each
  get their own look:
  - `Main Camera` (base, layers 0–5) draws the Kinect feed quad with PP on and Volume
    Mask `PP Feed` (layer 8) → `Feed Volume` (`VolumeProfile (Camera Feed).asset`).
  - `Main Camera/Particles Camera` (base, depth −1, layer 6 `KinectOverlay` only, clear
    transparent) has PP on with Volume Mask `PP Particles` (layer 7) → `Particles
    Volume` (`VolumeProfile.asset`). `ParticleLayerCompositor.cs` renders it into a
    screen-sized RenderTexture (with depth, for the body occluder) shown full screen
    by the `Particle Layer Composite` canvas (Screen Space Overlay, sort −1, drawn after
    the Main Camera's PP) using the premultiplied `EnergyBall/UIPremultipliedComposite`
    shader.
  - Not a camera stack on purpose: an overlay camera's PP runs on the whole stack
    (feed included), so it can't separate the looks.
  - Needs URP **Alpha Processing** (`m_AllowPostProcessAlphaOutput`). It's project
    wide and masks PP by alpha on every camera, so any camera that outputs to the
    screen must clear with alpha 1 (Main Camera, Dummy Scene camera). Under it, bloom
    glow still spreads into alpha-0 areas, but color grading / vignette don't touch
    those pixels.
  - Anything that must depth-interact with the body occluder stays on layer 6.
- `VolumeController.cs` (on `Particles Volume`): pushes
  `RuntimeSceneSettings.particlePostProcessing` into `particleVolume` and
  `feedPostProcessing` into `feedVolume` (Bloom, ChromaticAberration, LensDistortion,
  ColorAdjustments, WhiteBalance, ScreenSpaceLensFlare). The Dummy Scene has no feed
  pass: `feedVolume` is empty and only the particle look applies.
- Instagram banner: `Banner Canvas` (Screen Space Overlay, Scale With Screen Size,
  1920×1080 reference, match height), so it keeps the same share of the screen at any
  output resolution.

### Settings system (base → effective)
- `RuntimeSceneSettings.cs` — the `[Serializable]` settings class. Profiles, the
  `SceneController` inspector twins and the in-game menu all hold **base values at
  `bodyScale = 1`**. Every dimensioned field carries `[BodyScaled(exp)]` (lengths /
  velocities 1, rigidbody forces 2, spatial frequencies −1; time/ratios/curves none).
- `BodyScaling.cs` — builds a reflection table of `[BodyScaled]` fields once and
  derives the **effective** object (`base × bodyScale^exp`) via `CreateEffective`.
  `SceneController.RebuildEffectiveSettings()` runs it on every settings change;
  consumers read `SceneController.CurrentSettings` / `GetRuntimeSettings()` (effective,
  cached, never mutate it). Data flows one way: menu/inspector → base → effective.
- `PlayerScaleApplier.cs` — the per-player scale step (hand/body VFX values, TD debug
  spheres + BrownianMotion wander, hand collider scale) and the live-state rescale
  when `bodyScale` changes while players exist.
- `settingsVersion` on the settings class: 0 = legacy effective-value profile
  (auto-converted on load for Scene profiles), 1 = base-at-1×. Saves stamp 1.
  `EnergyBall/Migrate Scene Profiles To Base` (Editor menu) rewrites v0 files.
- `InGameSettingsMenu.cs` / `SettingsMenuSetup.cs` — live in-game tuning UI (base
  values at `bodyScale = 1`). UI Toolkit:
  `Assets/UI/SettingsMenu.uxml` / `.uss`, scaled by `Assets/UI Toolkit/PanelSettings.asset`
  (Scale With Screen Size, 1280×720 reference, match height, so it keeps the same share of
  the screen at any resolution). Groups collapse from their header (state in PlayerPrefs
  `SettingsMenuCollapsedGroups`); each tab has a search field that filters rows by group
  title + label. The UI is rebuilt on every open / load, so both are re-applied after
  `CreateSettingsUI()`.
- **Feature-gated settings**: `SceneController.SceneFeature` (`Kinect` = not
  `dummyOnlyMode`; `CameraFeed` = Kinect + `cameraFeedQuad` assigned). The menu builds
  such rows only inside `if (SceneSupports(SceneFeature.X))`. Their SceneController twins
  use NaughtyAttributes `[ShowIf("HasKinect")]` / `[ShowIf("HasCameraFeed")]`, or a named
  condition property when combined with another rule (NaughtyAttributes allows one
  ShowIf/HideIf per field). Hidden settings still load and save with profiles. Kinect:
  `drawSkeleton` and the `cameraFeedQuad` slot. That slot is what enables CameraFeed, so
  it can't be gated on it. Kinect + `drawSkeleton` (`ShowSkeletonSettings`): line
  material, tracking-state colors, the single skeleton color. CameraFeed:
  `showCameraFeed` (toggles the feed quad's renderer in play mode),
  `projectiveAlignment`, `renderCameraTransform`. Individual colors: `particleColors` is
  the palette (its length is the slot count). Each player keeps its
  `PlayerConstructor.paletteSlot`, and `skeletonColors[slot]` is optional (wraps,
  falls back to `skeletonColor`), so it shows only when bones use it.
- Menu rows that depend on another setting's value (not on a scene feature) use
  `ShowRowIf(label, condition)` right after the field is created. It's re-evaluated on
  every settings change, e.g. Use Tracking State Colors shows only while Draw Skeleton is
  on.
- Post-processing values live in `PostProcessSettings.cs`, held twice on the settings
  (`particlePostProcessing`, `feedPostProcessing`). PP profile files are serialized
  straight from `PostProcessSettings`, and older files with flat keys load unchanged.
  The menu's Post Processing tab has a **Particles / Camera Feed** target switch: each
  target has its own profile, dirty flag and last-used key
  (`LastUsedPostProcessingProfile_<scene>` / `LastUsedFeedPostProcessingProfile_<scene>`),
  and any PP profile can be loaded into either. With no last-used feed profile, the feed
  loads the same profile as the particles. The switch only shows while the feed target
  is available (`IsFeedTargetAvailable`: feed volume + CameraFeed feature + Show Camera
  Feed on). Otherwise the tab falls back to Particles, and the feed's unsaved changes
  don't count toward `Post Processing *`; its values and profile are kept.
- Persistence: JSON profiles in `Assets/StreamingAssets/SettingsProfiles/`,
  gradient preset libraries in `Assets/StreamingAssets/GradientPresets/` (one ordered JSON
  file per library, the runtime counterpart of the editor's `.gradients` libraries; Default
  and VFXGradients were converted from the editor's preference folder),
  animation-curve presets in `Assets/StreamingAssets/CurvePresets/`, edited via the
  `Assets/Scripts/RuntimeCurveEditor/` runtime curve editor. Colors and gradients
  (skeleton color(s), particle gradient / palette) are edited with the runtime ports of the
  editor's color picker and gradient editor in `Assets/Scripts/RuntimeColorEditor/` (IMGUI
  popups like the curve editor; HDR colors are linear and shown gamma-encoded). They live
  behind `RuntimeSceneSettings.hasStyleColors`, so an older profile or working set without
  them keeps the current colors instead of loading white defaults.
- **Working set** (`SettingsWorkingSet.cs`): the single latest copy of the settings,
  per scene, at `Application.persistentDataPath/SettingsWorkingSet/<scene>.json`.
  Every change writes it (menu edits, profile loads, inspector edits in play AND edit
  mode), so the most recent change always wins: play start restores it instead of
  auto-loading the last-used profile, and on play exit `SceneController`'s editor hook
  copies it back into the inspector twins (dirtying the scene) and both Volume Profiles.
  It records `postProcessingProfileName` (particles) and
  `feedPostProcessingProfileName`. A pre-split file (flat PP keys) loads its one look
  into both targets. The menu shows "Unsaved changes" / `Scene *` when the working set differs
  from its profile and asks before a load discards it. No SessionState anywhere.
- Why the exponents are what they are, the HandEffects.vfx scaling rules (Conform,
  noise, Turbulence, the intentional `÷ 5`) and the gotchas: `Docs/BodyScale.md`.
  How to add a setting end to end: `Docs/In-Game Menu/Adding-New-Settings-To-Menu.md`.

### Dummy players (dev without a Kinect)
`DummySceneControl.cs`, `DummyHandController.cs`, `DummyTransformer.cs` plus
`dummyOnlyMode` let you spawn and puppet players without a sensor. Use these to test
metaballs, gravity, scaling, and VFX from the editor. Test scenes live in
`Assets/Testing/` (e.g. `Dummy Scene.unity`, VFX experiments, Kinect webcam output).

## Working in this repo

- **Unity automation**: the unity-cli bridge is installed (embedded package). Use
  the `unity` agent / unity-cli skills for scene inspection, GameObject/component
  edits, C# navigation, and play-mode testing. For `.vfx` graph work use the VFX
  Graph bridge skill — the general unity-cli skills don't cover VFX Graph.
- **No test framework** — verification is done in Play Mode. Prefer dummy players
  over requiring the physical Kinect when reproducing/verifying behavior.
- **Adding gameplay behavior**: mirror the existing "force class" pattern (plain
  class pulling `SceneController.Instance`, called per-frame from the controller)
  rather than adding new MonoBehaviours, unless the behavior genuinely needs one.
- **Performance**: heavy math (metaball field, marching cubes) runs on compute
  shaders; the controller runs on `FixedUpdate`. Keep per-frame allocations down.
