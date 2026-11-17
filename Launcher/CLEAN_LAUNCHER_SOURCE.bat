@echo off
setlocal
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0Clean_Launcher_Build_Output.ps1"
pause
