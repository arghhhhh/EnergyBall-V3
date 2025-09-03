# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

EnergyBall-V3 is a Unity 3D interactive experience that uses Kinect motion tracking and metaballs rendering for immersive body-based interactions. The project features real-time 3D fluid simulations using marching cubes algorithm and compute shaders for performant metaballs generation.

## Development Commands

### Unity Editor
- **Open Project**: Launch Unity Hub and open the project folder
- **Unity Version**: 6000.2.2f1 (specified in ProjectSettings/ProjectVersion.txt:1)
- **Main Scene**: Assets/Energy Ball V3.unity

### C# Development
- **Solution File**: EnergyBall-V3.sln
- **Default IDE**: Visual Studio (configured in .vscode/settings.json:54)
- **Format Code**: Handled automatically via .editorconfig and VS Code settings
- **C# Formatting**: Uses ms-dotnettools.csharp with 4-space indentation (configured in .vscode/settings.json:63-70)

### Package Management
- **Unity Package Manager**: Packages managed via Packages/manifest.json
- **Key Dependencies**: 
  - Keijiro packages for VFX and motion tools (jp.keijiro.*)
  - Unity Visual Effect Graph for particle systems
  - NaughtyAttributes for enhanced inspector UI

## Architecture Overview

### Core Systems
- **SceneController** (Assets/Scripts/SceneController.cs:10): Singleton pattern coordinator managing the entire application state, Kinect body tracking, and player lifecycle
- **PlayerConstructor** (Assets/Scripts/PlayerConstructor.cs:10): Individual player entity management with metaball integration and visual effects
- **SceneSettingsSO**: ScriptableObject for configuration data

### Kinect Integration
- **BodySourceManager** (Assets/Scripts/BodySourceManager.cs): Kinect SDK v2 integration for body tracking
- **HandForce & HandEffects**: Hand gesture recognition and physics interaction systems
- **Player Scaling**: Dynamic player size adjustment based on tracking data

### Metaballs & Rendering
- **MetaballsToSDF** (Assets/Metaballs/MetaballsToSDF.cs): Converts metaball data to signed distance fields
- **MarchingCubes** (Assets/MarchingCubes/): Compute shader-based mesh generation from SDF data
- **Compute Shaders**:
  - MetaballsGenerator.compute: Real-time metaball field calculation
  - MarchingCubes.compute: Mesh triangulation

### Physics & Effects
- **GravityForce**: Custom physics controller for metaball interactions
- **VFX Integration**: Visual Effect Graph for particle systems and effects

## Project Structure

### Key Directories
- **Assets/Scripts/**: Core C# gameplay logic (220+ scripts total)
- **Assets/Metaballs/**: Metaballs generation and SDF conversion
- **Assets/MarchingCubes/**: Marching cubes algorithm implementation
- **Assets/VFX/**: Visual effects and particle systems
- **Assets/Config/**: Runtime configuration data
- **Assets/Prefabs/**: Reusable game objects and components

### External Dependencies
- **Windows Kinect SDK v2**: Body tracking via Windows.Kinect namespace
- **Keijiro Tool Suite**: Professional VFX and animation utilities
- **NaughtyAttributes**: Enhanced Unity Inspector functionality
- **Universal Render Pipeline (URP)**: Modern rendering pipeline

## Development Workflow

### Code Style
- **Indentation**: 4 spaces (enforced by .editorconfig and VS Code settings)
- **Formatting**: Auto-format on save enabled for all file types
- **C# Conventions**: Uses Unity coding standards with NaughtyAttributes decorators

### Performance Considerations
- Compute shaders handle intensive mathematical operations (metaballs, marching cubes)
- Singleton patterns used for critical managers (SceneController)
- Object pooling implemented for player entities via dictionary tracking

### Testing & Building
- No specific test framework configured - testing done in Unity Play Mode
- Build configurations available through Unity Editor Build Settings
- Platform target: Windows with Kinect SDK dependency