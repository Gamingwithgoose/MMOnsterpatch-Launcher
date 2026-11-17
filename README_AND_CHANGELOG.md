# MMOnsterpatch Launcher v0.19.6 ClipboardCompileFix

- Linux Online Play now stages the baked-in Linux BepInEx payload, writes the online/session config, validates required files, then directly launches `run_bepinex.sh` from the game folder. This mirrors the original Windows launcher flow instead of relying on Steam launch-option mutation.
- Linux Steam launch-option editing is disabled by default. The old Steam option manager remains in code but is not the player-facing path for this test build.
- The launcher log is now selectable via the log TextBox, with Copy and Open buttons.
- Offline/Vanilla on Linux restores any launcher-staged online runtime before launching.

# v0.18.7 NativeShooAwayPostfixFix

Built from v0.18.6.

## Purpose

Fix prize/server-owned spawn ShooAway NullReferenceException by matching vanilla Shoo behavior.

## Changes

- `GameScript.ShooAway` patch no longer hides/clears the interacted mon in Prefix.
- Prefix only captures the active server spawn id.
- Vanilla `ShooAway()` is allowed to run normally with `curInteractingMonObj` intact.
- Postfix reports/removes the server-owned spawn after vanilla finishes.
- Preserves v0.18.6 claim/cancel/menu watchdog behavior otherwise.

## Preserved

- v0.18.3 no-collider battle request fix.
- busy overworld spawn lock.
- idle/no-disconnect fix.
- remote follower tint.
- PvP action queue/actionSeq/damage/targeting untouched.


## v0.19.1 - AvaloniaBuildAllFlatLinuxLauncher

- Integrated the Avalonia cross-platform launcher publish into the normal root `CLEAN_AND_BUILD_ALL.bat` flow.
- The Linux launcher is now placed in the same `Releases\Launcher` folder as the Windows launcher output for easier bulk transfer testing.
- `Releases\Launcher` now includes `MMOnsterpatchLauncher.exe`, `MMOnsterpatchLauncher.Avalonia.exe`, `MMOnsterpatchLauncher-linux-x64`, `Payloads\win-x64`, `Payloads\linux-x64`, and a Steam Deck helper script.
- Added `Launcher\Build_Avalonia_Launcher_Pack.bat` to bypass PowerShell execution policy when building only the Avalonia launcher pack.
- The built client patcher DLL/PDB is now copied into WPF, Avalonia Windows, and Avalonia Linux payloads.


## v0.19.4 launcher notes

- Avalonia payloads are embedded/baked into the launcher/Core assembly at publish time. The player release folder no longer needs visible `Payloads/win-x64` or `Payloads/linux-x64` folders for the Avalonia launchers.
- The Windows WPF launcher remains preserved as the known-good Windows baseline.
- `MMOnsterpatchLauncher-linux-x64` is still emitted beside the Windows launcher for bulk transfer testing.
- `RUN_LINUX_LAUNCHER_FROM_DECK.sh` remains included as a first-run helper because Linux may require `chmod +x` before a copied binary can be double-clicked.
- Experimental `MMOnsterpatchLauncher.desktop` is emitted for Linux file managers.

## v0.19.4 - WarningCleanTarFix

- Fixed Linux/Steam Deck Steam launch-option editing. The previous draft could write an ignored capital `Apps` block in `localconfig.vdf`, which meant Steam could still launch vanilla even after Online Mode was selected.
- The launcher now targets Steam's lower-case `apps` block, prefers the `localconfig.vdf` containing the Monsterpatch app block, backs the file up, writes `./run_bepinex.sh %command%`, and reads it back for verification before launching.
- Online staging now hard-fails if `BepInEx/patchers/MMOnsterpatchOfficialServerPatcher.dll` is missing instead of warning and launching a likely-vanilla game.
- Online staging now writes `MMOnsterpatch_STAGING_DIAGNOSTICS.txt` into the game folder after successful validation.
- Linux `run_bepinex.sh` patching now tolerates leading whitespace before `executable_name=`.
- Root-level historical `WHAT_CHANGED_*`, `PATCH_NOTES_*`, handoff JSON, and diff files were moved into `Docs/Archive/RootNotes/` to reduce clutter.

Test note: if Steam is already running on Deck and Online Mode still starts vanilla after launch options are written, fully exit Steam once and launch Online Mode again so Steam reloads `localconfig.vdf`.
