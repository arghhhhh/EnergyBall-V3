# Cursor Unity Setup Guide

This guide explains how to configure Cursor (VS Code-based IDE) to work with Unity projects, providing IntelliSense, syntax error checking, and code completion for C# scripts.

## Prerequisites

- Unity Editor with your project open
- Cursor IDE installed
- Windows 10/11 (this guide is Windows-specific)

## Step 1: Install .NET SDK

OmniSharp (the C# language server) requires a .NET SDK to function properly.

1. Download and install **.NET 8.0 SDK** (LTS version recommended):
   - Go to: https://dotnet.microsoft.com/download/dotnet/8.0
   - Download the **.NET 8.0 SDK** (not just the runtime)
   - Run the installer and follow the prompts

2. Verify installation:
   - Open PowerShell/Command Prompt
   - Run: `dotnet --version`
   - You should see a version like `8.0.417`

## Step 2: Install Visual Studio Build Tools

OmniSharp needs MSBuild to analyze and load Unity projects. Visual Studio Build Tools provides MSBuild without requiring the full Visual Studio IDE.

1. Download Visual Studio Build Tools:
   - Go to: https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022
   - Download "Build Tools for Visual Studio 2022"

2. Install MSBuild:
   - Run the installer
   - Select **"MSBuild"** component (under Individual components)
   - Or select **"Desktop development with C++"** workload (includes MSBuild)
   - Click Install

3. Restart your computer after installation to refresh environment variables

## Step 3: Install C# Extension in Cursor

1. Open Cursor
2. Press `Ctrl+Shift+X` to open Extensions
3. Search for and install:
   - **C#** (by Microsoft) - Extension ID: `ms-dotnettools.csharp`
   - **Visual Studio Tools for Unity** (by Microsoft) - Extension ID: `visualstudiotoolsforunity.vstuc`

## Step 4: Configure Cursor Settings

Your project already has the correct settings configured in `.vscode/settings.json`. The key settings are:

```json
{
  "dotnet.server.useOmnisharp": true,
  "dotnet.preferCSharpExtension": false,
  "omnisharp.useModernNet": true,
  "omnisharp.autoStart": true,
  "omnisharp.dotNetCliPaths": ["C:\\Program Files\\dotnet\\dotnet.exe"]
}
```

**Important**: The `omnisharp.useModernNet: true` setting is crucial - it makes OmniSharp use the .NET SDK's MSBuild instead of Visual Studio's MSBuild, which fixes compatibility issues.

## Step 5: Create global.json (Optional but Recommended)

A `global.json` file helps MSBuild locate the correct .NET SDK version. This file is already created in your project root:

```json
{
  "sdk": {
    "version": "8.0.417",
    "rollForward": "latestMinor"
  }
}
```

Update the version number to match your installed .NET SDK version (check with `dotnet --version`).

## Step 6: Verify Setup

1. **Restart Cursor** completely (close all windows and reopen)

2. **Open your Unity project** in Cursor

3. **Open a C# script** (e.g., any file in `Assets/Scripts/`)

4. **Check OmniSharp status**:
   - Look at the bottom-right status bar - you should see an OmniSharp flame icon or "OmniSharp" text
   - Open the Output panel (`Ctrl+Shift+U`)
   - Select "OmniSharp Log" from the dropdown
   - You should see:
     - `Located 2 MSBuild instance(s)` (or more)
     - `Successfully loaded project file` for each `.csproj`
     - `Solution initialized -> queue all documents for code analysis`

5. **Test IntelliSense**:
   - Type some Unity code like `GameObject.` - you should see autocomplete suggestions
   - Type invalid code - you should see red squiggly error lines

## Troubleshooting

### OmniSharp Not Starting

- **Check Output panel**: Look for errors in "OmniSharp Log"
- **Restart OmniSharp**: `Ctrl+Shift+P` → `OmniSharp: Restart OmniSharp`
- **Verify .NET SDK**: Run `dotnet --list-sdks` in terminal - should show installed SDKs
- **Check MSBuild**: Run `where msbuild` - should find MSBuild executable

### Projects Not Loading

- **Check global.json**: Ensure the SDK version matches your installed version
- **Regenerate Unity project files**: In Unity, go to `Edit > Preferences > External Tools` and click "Regenerate project files"
- **Check OmniSharp Log**: Look for specific error messages about missing SDKs or MSBuild issues

### MissingMethodException Errors

If you see `MissingMethodException` errors in the OmniSharp log:
- Ensure `omnisharp.useModernNet` is set to `true` in `.vscode/settings.json`
- Restart OmniSharp after changing settings
- This error indicates OmniSharp is using an incompatible MSBuild version

### No IntelliSense or Error Checking

- Wait 30-60 seconds after opening a file - OmniSharp needs time to analyze the project
- Check the OmniSharp Log for any errors
- Ensure the C# extension is enabled (not disabled)
- Try reloading the window: `Ctrl+Shift+P` → `Developer: Reload Window`

## What's Working Now

Once set up correctly, you should have:

✅ **IntelliSense/Autocomplete** - Code suggestions as you type  
✅ **Syntax Error Detection** - Red squiggly lines for errors  
✅ **Go to Definition** - `F12` to jump to method/class definitions  
✅ **Find References** - `Shift+F12` to find all usages  
✅ **Code Formatting** - Auto-format on save (configured)  
✅ **Unity API Support** - Full IntelliSense for Unity classes and methods  

## Additional Notes

- **Unity Editor must be running** for best results (though not strictly required for IntelliSense)
- **Project files (.csproj, .sln)** are auto-generated by Unity - don't edit them manually
- **Settings are project-specific** - stored in `.vscode/settings.json` in your project root
- **OmniSharp uses .NET SDK's MSBuild** - not Visual Studio's MSBuild (thanks to `useModernNet: true`)

## Summary

The key to getting Unity + Cursor working is:
1. Install .NET 8 SDK
2. Install Visual Studio Build Tools (for MSBuild)
3. Set `omnisharp.useModernNet: true` in settings
4. Restart everything and wait for OmniSharp to initialize

Your project is now configured and ready to use!
