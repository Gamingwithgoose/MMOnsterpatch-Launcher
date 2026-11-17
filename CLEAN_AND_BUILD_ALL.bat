@echo off
setlocal
cd /d "%~dp0"
echo ============================================================
echo MMOnsterpatch Clean + Build All v0.19.6 ClipboardCompileFix
echo ============================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Clean_And_Build_All.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. See errors above.
  pause
  exit /b 1
)
echo.
echo Done.
echo.
echo Launcher files should be in:
echo   %~dp0Releases\Launcher
echo.
echo Included launcher files:
echo   MMOnsterpatchLauncher.exe
echo   MMOnsterpatchLauncher.Avalonia.exe
echo   MMOnsterpatchLauncher-linux-x64
echo   Avalonia payloads baked into launcher binaries
echo.
echo Server deployment folder should be:
echo   %~dp0Releases\Server
echo.
echo Client files copy should be in:
echo   %~dp0Releases\Client Files
echo.
echo IMPORTANT: Copy the entire Releases\Server folder to the server,
echo not just MMOnsterpatchRepackManager.exe.
echo.
pause
endlocal
