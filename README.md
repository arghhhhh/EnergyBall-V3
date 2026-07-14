# Energy Ball V3

Create, shape, and fling energy balls with your body using a Kinect V2 camera. Built in Unity with real-time metaball fluid rendering and VFX Graph.

Compared to [V2](https://github.com/arghhhhh/EnergyBall-V2), this project adds the following notable enhancements:
* Real-time metaball rendering for smoother energy ball merging
* Improved particle animations, physics, and force interactions
* In-game settings menu with custom profile support for adjusting properties in builds
* Optional live camera feed rendered behind the particles and tracked bodies

## Preview
[v3_prev_3.webm](https://github.com/user-attachments/assets/4a3e4f32-90ac-4084-b775-feb7333421b7)

## Requirements
- Kinect V2 Camera
- [Kinect SDK](http://www.microsoft.com/en-us/download/details.aspx?id=44561)
- **Important:** Import the missing Kinect DLLs from the [Kinect V2 Plugin for Unity](http://go.microsoft.com/fwlink/?LinkID=513177) into this project's assets folder. Only the DLLs from the Unity package file are needed; nothing else is required from it.
- [Unity 6000.3.19f1](https://unity.com/releases/editor/archive) (or matching 6000.3 LTS)

## Getting Started
1. Clone the repo and open the folder in Unity Hub with the version above.
2. Import the Kinect DLLs (see Requirements).
3. Open `Assets/Energy Ball V3.unity`.
4. Plug in the Kinect V2, install the SDK, and press Play.

No Kinect handy? Open `Assets/Testing/Dummy Scene.unity` to experiment with the particles and metaballs without any tracking hardware.

## How It Works
- **Kinect body tracking** feeds joint positions for each tracked player into the scene.
- **Hands** act as force sources that push metaball density around a 3D field.
- **A compute shader** builds the metaball scalar field every frame; **marching cubes** (also on the GPU) turns it into a watertight mesh.
- **VFX Graph** layers particle effects on top for the energy look.

## Tech Stack
- **Engine:** Unity 6 (6000.3 LTS)
- **Rendering:** Universal Render Pipeline (URP)
- **Compute:** HLSL compute shaders (metaball field + marching cubes)
- **VFX:** Unity Visual Effect Graph, plus [Keijiro](https://github.com/keijiro) tooling
- **Language:** C#

## License
See [`LICENSE.txt`](LICENSE.txt). Free to use, modify, and distribute — if you use it at an event or in an art piece, send some clips!
