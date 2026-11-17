# MMOnsterpatch Launcher Payloads

- `win-x64`: Windows BepInEx/Doorstop payload. Root files include `winhttp.dll`, `.doorstop_version`, and `doorstop_config.ini`.
- `linux-x64`: Linux BepInEx 5.4.23.5 payload. Root files include `run_bepinex.sh`, `libdoorstop.so`, and `.doorstop_version`.

The Linux payload copies MMOnsterpatch mod/config folders from the Windows payload, but the uploaded baseline did not include `BepInEx/patchers/MMOnsterpatchOfficialServerPatcher.dll`; it only included the `.pdb`. Run the normal client patcher build/import step before real online gameplay testing.
