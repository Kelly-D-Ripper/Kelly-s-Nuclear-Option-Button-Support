# Kelly's KNOBS

<p align="center">
  <img src="assets/kellys-knobs-logo.png" alt="Kelly's KNOBS logo" width="420">
</p>

**K**elly's **N**uclear **O**ption **B**utton **S**upport

> Because 32 buttons just aren't enough knobs.

A client-side BepInEx plugin for **Nuclear Option** that detects flight
controllers with more than 32 buttons and reports their real Windows/Rewired
names in-game.

The plugin was created for controllers such as the WinWing URSA Minor, but its
detection is manufacturer-independent.

## Features

- Automatically detects every joystick exposing more than 32 buttons.
- Displays each controller's actual name, axis count, and button count.
- Recognizes common split-device layouts such as WinWing 4x32 mode.
- Excludes common gamepads from the 32-button flight-controller heuristic.
- Can optionally retry suspicious 32-button devices through DirectInput.
- Can optionally assign detected high-button controllers to player 0.
- Does not transmit data or modify game files.

## Safe defaults

Detection and reporting are automatic. The settings that change Rewired state
are deliberately disabled by default:

- `TryDirectInputWhenExactly32Buttons = false`
- `AutoAssignHighButtonControllers = false`

DirectInput is a global Rewired setting. Enabling the fallback can change
controller identities and may require rebinding controls. Only enable it when a
controller that should have more than 32 buttons is being truncated.

## Installation

Requirements:

- Nuclear Option with BepInEx 5 installed.
- Windows.

Steps:

1. Exit Nuclear Option.
2. Download the latest release archive.
3. Copy `KellysKNOBS.dll` to:

   `Nuclear Option\BepInEx\plugins\`

4. If upgrading from a pre-release build, delete
   `WinWing128ButtonFix.dll` and `HighButtonControllerFix.dll`.
5. Start Nuclear Option.

The startup banner and `BepInEx\LogOutput.log` show every detected
high-button controller by its real name.

## Controller modes

If the banner reports several split devices with the same name, select the
manufacturer's native, single-device, or high-button mode and reconnect the
controller.

For WinWing hardware, disable **4x32 Button Mode** in SimApp Pro. Changing
between split and native modes changes device identities, so bindings made
against split devices generally need to be rebound once.

## Configuration

After the first run:

`BepInEx\config\com.kellys.nuclearoption.knobs.cfg`

| Setting | Default | Purpose |
| --- | ---: | --- |
| `Enabled` | `true` | Enables detection and reporting. |
| `MinimumButtonCount` | `33` | Threshold for a high-button controller. |
| `TryDirectInputWhenExactly32Buttons` | `false` | Opts into the global DirectInput fallback. |
| `AutoAssignHighButtonControllers` | `false` | Opts into assignment to player 0. |
| `StatusSeconds` | `15` | Controls how long the status banner appears. |

## Compatibility

Tested with:

- Nuclear Option 0.34.0
- Rewired 1.1.62.7
- BepInEx 5
- WinWing URSA Minor Combat Joystick R in native mode
- Saitek X-55 Rhino Throttle

The plugin uses public Rewired APIs and does not patch game methods. Future game
updates may still require a compatibility update.

## Building

Install the .NET SDK, then provide the Nuclear Option installation directory:

```powershell
dotnet build .\KellysKNOBS.csproj -c Release `
  -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option"
```

Alternatively, set the `NUCLEAR_OPTION_DIR` environment variable or run
`build-release.ps1`.

Game and Rewired assemblies are referenced from the local installation and are
not redistributed.

## Support

When reporting a problem, include:

- Controller manufacturer and exact model.
- Native or split-device mode.
- Nuclear Option version.
- Relevant lines from `BepInEx\LogOutput.log`.

## Licence

MIT. See [LICENSE](LICENSE).
