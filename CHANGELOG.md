# Changelog

All notable changes are documented here.

## 1.0.0 - 2026-07-27

First public release of **Kelly's KNOBS** — Kelly's Nuclear Option Button
Support.

### Added

- Manufacturer-independent high-button controller detection.
- Real controller names in the status banner and log.
- Split-device layout detection.
- Optional DirectInput fallback for suspicious 32-button flight controls.
- Optional player 0 assignment.
- Portable `GameDir` and `NUCLEAR_OPTION_DIR` build configuration.
- Repeatable release packaging script.

### Changed

- Branded the public plugin, assembly, and repository as Kelly's KNOBS.
- Made DirectInput switching opt-in.
- Made automatic controller assignment opt-in.
- Limited the plugin to `NuclearOption.exe`.

### Tested

- Nuclear Option 0.34.0 with Rewired 1.1.62.7 and BepInEx 5.
- WinWing URSA Minor Combat Joystick R: 144 Rewired buttons through RawInput.
- Saitek X-55 Rhino Throttle: 43 Rewired buttons through RawInput.
