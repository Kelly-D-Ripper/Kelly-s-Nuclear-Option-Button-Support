# Contributing

Bug reports and controller compatibility results are welcome.

Please include the controller's exact Windows name, model, native/split mode,
Nuclear Option version, and relevant `BepInEx\LogOutput.log` lines.

For code changes:

1. Keep DirectInput switching and controller assignment opt-in.
2. Do not bundle Nuclear Option, Unity, Rewired, or BepInEx assemblies.
3. Build in Release configuration.
4. Test with at least one high-button flight controller and one ordinary
   gamepad when changing detection logic.
5. Update `CHANGELOG.md`.
