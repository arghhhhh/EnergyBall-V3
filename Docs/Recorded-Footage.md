# Recorded footage: energy balls on pre-recorded video

Drive the energy balls from a video instead of a Kinect: a clip's people get balls they push,
pull and grow with their hands, with the video as the backdrop, then record studio-quality
finals at the clip's aspect ratio (portrait reels: 1080x1920).

Two halves:

1. **Extraction (ComfyUI project, outside this repo).**
   `C:/Comfy/ComfyUI-Easy-Install/Projects/energy-ball-footage/RUNBOOK.md` is the
   authoritative how-to: clip requirements, the model stack (DWPose, MoGe-2, YOLO11x-seg,
   WiLoR + MANO, BiRefNet, MatAnyone 2), one command (`scripts/run_pipeline.sh <n>`), QA, and
   the rejected alternatives. It produces, per clip `<n>`:
   - `<n>_bodies3d.json`: per-frame camera-space skeleton (Kinect joint names and axes,
     metres) + per-hand open/closed, plus the clip's pinhole intrinsics.
   - `<n>_occluder.bin` (`EBOC`): per-frame metric depth + person body-index mask (360x640).
   - `<n>_matte.bin` (`EBMT`): per-frame soft person matte (720x1280).
2. **Playback and recording (this repo).** Below.

## Hand-off: files go in `Assets/StreamingAssets/RecordedFootage/`

```
Assets/StreamingAssets/RecordedFootage/
  <n>.mp4  <n>_bodies3d.json  <n>_occluder.bin  <n>_matte.bin
  <n>_frames/00000.png ...     (only needed for recording)
```

Extract the frame sequence (one PNG per video frame, numbered from 0):

```
cd Assets/StreamingAssets/RecordedFootage
mkdir <n>_frames
ffmpeg -i <n>.mp4 -fps_mode passthrough -start_number 0 <n>_frames/%05d.png
```

The PNG count must equal `frameCount` in `<n>_bodies3d.json`.

**This folder is gitignored and must stay that way.** The clips are third-party footage
(used with permission, not for redistribution) and the files are large. `Recordings/` (the
finals) is ignored too.

## The scene: `Assets/Testing/Recorded Footage.unity`

A copy of the main scene with **RecordedBodySource** on the Scene Controller GameObject.
Enabling that component is what switches a scene to recorded mode (`SceneController.UsesRecordedBodies`):
the Kinect isn't opened, players come from the bodies file, and the video is the backdrop.

On `RecordedBodySource`, set:

| Field | Value |
|---|---|
| Video File | `<n>.mp4` |
| Bodies File | `<n>_bodies3d.json` |
| Occluder File | `<n>_occluder.bin` (optional: body occlusion) |
| Matte File | `<n>_matte.bin` (optional: background dimming uses it; falls back to the occluder mask) |
| Frame Sequence Folder | empty = `<n>_frames` |
| Use Frame Sequence | off (the recorder switches it on; see below) |

Other controls: **Background** (dim / desaturate / tint everything but the people, via the
matte: brightness, saturation, tint, matte feather / grow), **Hand State Mode** (Recorded =
the open/closed baked into the file, already debounced; Curl Threshold = threshold the raw
curl with Closed Curl Above / Open Curl Below), **Match Camera Fov / Fit Feed Quad To
Camera** (render cameras get the clip's vertical FOV; the feed quad takes the clip's aspect).
Values changed on this component in Play Mode are lost when play stops (it isn't part of the
settings working set); copy / paste component values or set them outside play.

Keys in play (menu closed): **Space** pause / resume, **R** restart the clip.

Scene settings that differ from the Kinect scene and why:

- **Metaballs To SDF > Dimensions `64, 64, 64`** (the Kinect scene uses `64, 32, 64`). The
  grid is also the play boundary, and leaving it forces both hands closed
  (`HandEffects` out-of-bounds). Portrait footage with a low camera puts the ball high in
  the frame, so the short Kinect grid made the particle flow cut out on quick upward moves.
- **Max Distance From Camera** raised (people in reels stand ~2–3 m+ away; hands beyond it
  are forced closed).
- **Game view `1080x1920`** (portrait); set it in the Game view's resolution menu.

## Recording finals: EnergyBall > Recorded Footage Recorder

1. Press Play, tune the look (settings menu / Scene Controller / RecordedBodySource).
2. Open **EnergyBall > Recorded Footage Recorder**, set passes (default 3), output width
   (1080), quality (CRF 14 ≈ visually lossless), and **Record**.
3. It switches the clip to the frame sequence, waits for a ball to form, starts Unity
   Recorder (PNG, Game View, 30 fps), pre-rolls 15 frames, restarts the clip, and stops
   exactly when the last pass ends, then encodes `Recordings/EnergyBall_<n>_<passes>loops_<time>.mp4`
   with ffmpeg (H.264 High, yuv420p, BT.709). The PNG folder next to it is the lossless
   master (~2.3 MB/frame); delete it when happy. Expect ~6 captured frames/s (an 804-frame
   clip x3 takes ~7 min). Each recording gets a new timestamped name, nothing is overwritten.

Why it works this way (don't "simplify" these away):

- **Frame sequence, not the VideoPlayer.** Unity Recorder steps game time by exactly 1/30 s
  per captured frame. The VideoPlayer can't keep up with that clock, so the video drifts
  into slow motion with repeated frames. `useFrameSequence` shows frame N of the extracted
  PNGs exactly when game time reaches N / fps, so video, skeleton, masks and particles stay
  locked one frame per capture.
- **Cap FPS off.** The Recorder's Cap FPS option stalled the editor at ~1 fps.
- **PNG + ffmpeg, not the Recorder's MP4 encoder.** Its H.264 encoder backs up to ~1 fps at
  1080x1920 once its buffer fills.
- **Pre-roll.** The Recorder takes a few frames to start capturing; restarting the clip after
  it has started (and reading the Recorder's frame index at the restart) gives whole passes
  starting on clip frame 0.

## How it fits into the code

- `BodySource/TrackedBody.cs`: `TrackedBody` (managed body snapshot), `IBodySource` (bodies,
  feed frame count, camera point -> feed UV) and `IDepthFrameSource` (occluder depth + body
  index). `BodySourceManager` implements both for the Kinect; `RecordedBodySource` for a clip.
  `SceneController.BodySource` is whichever is active; gameplay never reads a native `Body`.
- `RecordedBodySource` projects joints through the clip's intrinsics into the video (Kinect
  camera space: +X toward the camera's left, +Y up, +Z away). It disables
  `KinectFeedDisplay` on the feed quad and draws the video with `EnergyBall/RecordedFeed`
  (background dimming). Its occluder grid covers the whole video, so the body occluder uses
  its depth-to-color lookup path, never the Kinect's depth-space feed mode.
- `Rendering/ParticleAlphaGradingFeature.cs` (URP renderer feature, all scenes): makes the
  particle layer opaque during post-processing so the particle look grades the additive
  particles fully, then restores alpha.
- `Editor/RecordedFootageRecorder.cs`: the recorder window.

## Not supported yet

- Moving cameras (the extraction assumes one fixed intrinsic and a static background).
- Clips that aren't 720x1280-compatible in the extraction export (see the RUNBOOK caveat).
- More than one person is handled by the format (body ids) but untested.
