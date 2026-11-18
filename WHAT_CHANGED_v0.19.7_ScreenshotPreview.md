# Screenshot Preview v0.19.7

MMOnsterpatch v0.19.7 - Screenshot Preview

Launch MMOnsterpatchLauncher.exe normally, choose Online Play, and enter your save.
The movable PHOTO button appears during online gameplay.

Controls
- Click PHOTO, or press F8, to capture the game.
- Drag PHOTO to move it. Drag its bottom-right // corner to resize it.
- The preview title bar moves the preview; its bottom-right // corner resizes it.
- Retake captures again. Save locally writes a PNG to Pictures/MMOnsterpatch Screenshots.
- If Pictures is unavailable, the fallback is LocalApplicationData/MMOnsterpatch Screenshots.
- Close or Escape discards an unsaved preview. Saving does not close the preview.
- F9 resets both layouts. Shortcuts can be changed in goose.monsterpatch.screenshots.cfg.
- Layout is saved and carried across launcher sessions by the existing config persistence.
- The camera button and screenshot preview are hidden during capture. Other game UI remains visible.
- No images are uploaded. Community/Facebook/X sharing is future work.

Player checks
1. Take a screenshot and confirm the preview contains the expected game scene, without PHOTO or the previous preview.
2. Move and resize PHOTO, then capture again. Dragging/resizing should not take a picture.
3. Move/resize the preview and use Retake and Save locally.
4. Check the saved PNG and its displayed file path.
5. Close and reopen the game through the launcher; confirm layout persists. Test F9 recovery.

Build verification
Client and Windows launcher compiled successfully. Package checks confirm the launcher embeds the updated client DLL and all launcher payload copies match.
The game was not launched; visual/input behavior still needs your in-game test.

Source/build notes
Launcher/Publish_Launcher_SingleExe.ps1 now builds the client before publishing to Releases/Launcher.
The client's automatic WPF payload-copy target was corrected to the repository layout.
The root CLEAN_AND_BUILD_ALL flow remains available for the complete project.
Server startup, gameplay, database configuration, DNS, and website code were not changed.
