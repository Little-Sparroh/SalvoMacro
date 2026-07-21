# Salvo Macro

A BepInEx mod for Mycopunk that adds configurable toggle auto-fire for the wingsuit rocket salvo, with optional targeting model suppression.

Split from EnhancedCooldowns as a standalone salvo-focused mod.

## Features

- **Salvo activation modes**:
  - **None**: Default behavior (manual firing)
  - **Toggle**: Toggle auto-fire on/off; automatically fires salvo when recharged and enabled
- **Visual options**:
  - Optionally suppress the 3D salvo launcher targeting model to reduce screen clutter
  - Suppress HUD notifications during automatic salvo firing
- **Live config reload**: Settings reload automatically when the config file changes

## Getting Started

### Dependencies

* Mycopunk (base game)
* [BepInEx](https://github.com/BepInEx/BepInEx) - Version 5.4.2403 or compatible
* .NET Framework 4.8
* [HarmonyLib](https://github.com/pardeike/Harmony) (included via NuGet)

### Building/Compiling

1. Clone this repository
2. Open the solution file in Visual Studio, Rider, or your preferred C# IDE
3. Build the project in Release mode to generate the .dll file

Alternatively, use dotnet CLI:
```bash
dotnet build --configuration Release
```

### Installing

**Via Thunderstore (Recommended)**:
1. Download and install via Thunderstore Mod Manager
2. The mod will be automatically installed to the correct directory

**Manual Installation**:
1. Place the built `SalvoMacro.dll` in your `<Mycopunk Directory>/BepInEx/plugins/` folder

### Executing program

The mod loads automatically through BepInEx when the game starts. Check the BepInEx console for loading confirmation messages.

## Configuration

Access mod settings through the BepInEx configuration file at `<Mycopunk Directory>/BepInEx/config/sparroh.salvomacro.cfg`:

| Setting | Default | Description |
|---------|---------|-------------|
| SalvoActivationMode | `Toggle` | Activation mode for wingsuit salvo (None/Toggle) |
| SuppressSalvoModelAlways | `false` | Always hide the 3D salvo launcher targeting model |

## Usage

- **Toggle mode**: Press the salvo button to toggle auto-fire on/off; salvo fires automatically when recharged while enabled
- **None mode**: Default game behavior

Configuration changes take effect immediately without restarting the game.

## Help

* **Mod not loading?** Verify BepInEx is installed correctly and check console logs for errors
* **Settings not applying?** Check the config file syntax and ensure the game has write permissions
* **Conflicts?** This is a client-side mod and should not affect multiplayer. Do not run alongside EnhancedCooldowns if both patch the same salvo systems.

## Authors

- Sparroh
- funlennysub (BepInEx template)
- [@DomPizzie](https://twitter.com/dompizzie) (README template)

## License

This project is licensed under the MIT License - see the LICENSE file for details
