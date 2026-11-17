MMOnsterpatch Launcher v0.19.4 Avalonia Warning-Clean Embedded Payload Bulk Test
==================================================================

Purpose
-------
This build keeps the old Windows WPF launcher intact, adds the Avalonia cross-platform launcher, and restores the original player-facing expectation that the launcher carries the payload with it.

Release output
--------------
After running CLEAN_AND_BUILD_ALL.bat, launcher files are placed in:

  Releases\Launcher

Expected files:

  MMOnsterpatchLauncher.exe
    Current known-good Windows WPF launcher.

  MMOnsterpatchLauncher.Avalonia.exe
    Windows Avalonia test launcher.

  MMOnsterpatchLauncher-linux-x64
    Native Linux / Steam Deck Avalonia test launcher.

  RUN_LINUX_LAUNCHER_FROM_DECK.sh
    Helper for first run if Linux says the launcher is not executable or asks what to open it with.

  MMOnsterpatchLauncher.desktop
    Experimental desktop entry for Linux file managers.

Payload behavior
----------------
Avalonia payloads are baked into the launcher/Core assembly at publish time:

  Launcher\Payloads\win-x64
  Launcher\Payloads\linux-x64

These source folders are still present for building and inspection, but they are not copied into Releases\Launcher for players. At runtime the launcher extracts the correct embedded payload to its launcher cache, then stages it into the Monsterpatch game folder when Online Play is used.

Linux / Steam Deck note
-----------------------
Linux will not execute a copied/downloaded binary until the executable bit is set. If double-clicking MMOnsterpatchLauncher-linux-x64 asks what to open it with, run this once from the release folder:

  bash RUN_LINUX_LAUNCHER_FROM_DECK.sh

That helper runs chmod +x on the Linux launcher and then starts it. After that, normal double-click launching should work in typical Linux file managers.

Locked constants
----------------
Steam AppID: 2495320
Linux game executable: Monsterpatch.x86_64
Steam Deck online launch option: ./run_bepinex.sh %command%
