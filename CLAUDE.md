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

- **Unity**: `6000.3.19f1` (see `ProjectSettings/ProjectVersion.txt`). Unity 6.3.
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

### Metaballs → SDF → mesh pipeline
- `Metaballs/MetaballsToSDF.cs` (`[RequireComponent]` MeshFilter+MeshRenderer):
  owns the `List<Metaball>` (position + radius), a volume `ComputeShader`
  (default grid `64x32x64`, `gridScale`, `targetValue 0.26`, `triangleBudget`
  65536). Runs `Metaballs/MetaballsGenerator.compute` to build a scalar field.
- `MarchingCubes/MeshBuilder.cs` + `MarchingCubes/MarchingCubes.compute` +
  `TriangleTable.cs`: triangulate the isosurface into a `Mesh` each frame.
- Each player owns one metaball index; hands/body movement move the metaballs.

### VFX & post-processing
- VFX Graph assets in `Assets/VFX/` (`BodyEffects.vfx`, `HandEffects.vfx`,
  `Subgraphs/`), driven from `PlayerConstructor` via exposed properties/bools.
- `VolumeController.cs`: manages the URP post-processing `Volume` (Bloom, Vignette,
  ChromaticAberration, LensDistortion, ColorAdjustments, WhiteBalance,
  ScreenSpaceLensFlare) and persists edits via `SessionState`.

### Settings system (two layers)
- `SceneSettingsSO.cs` — a `ScriptableObject` asset for authoring defaults in the
  inspector.
- `RuntimeSceneSettings.cs` — a `[Serializable]` runtime class the game actually
  reads (`SceneController.CurrentSettings` / `GetRuntimeSettings()`). The controller
  copies inspector ↔ runtime.
- `InGameSettingsMenu.cs` / `SettingsMenuSetup.cs` — live in-game tuning UI.
- Persistence: JSON profiles in `Assets/StreamingAssets/SettingsProfiles/`,
  animation-curve presets in `Assets/StreamingAssets/CurvePresets/`, edited via the
  `Assets/Scripts/RuntimeCurveEditor/` runtime curve editor.

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
