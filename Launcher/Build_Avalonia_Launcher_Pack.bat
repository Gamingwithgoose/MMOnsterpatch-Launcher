@echo off
setlocal
cd /d "%~dp0"
echo ============================================================
echo MMOnsterpatch Avalonia Cross-Platform Launcher Build
echo ============================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build_Avalonia_Launcher_Pack.ps1"
if errorlevel 1 (
  echo.
  echo Avalonia launcher build failed. See errors above.
  pause
  exit /b 1
)
echo.
echo Done.
echo.
echo Output folder:
echo   %~dp0ReleasePack_AvaloniaCrossPlatform
echo.
pause
endlocal
