@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish_All_SingleExe.ps1"
if errorlevel 1 (
  echo.
  echo Publish failed. See errors above.
  pause
  exit /b 1
)
echo.
echo Done. Server release folder should be at:
echo   %~dp0..\Releases\Server
echo.
pause
endlocal
