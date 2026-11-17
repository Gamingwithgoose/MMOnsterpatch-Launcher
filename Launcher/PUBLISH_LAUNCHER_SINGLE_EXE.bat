@echo off
setlocal
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0Publish_Launcher_SingleExe.ps1"
pause
