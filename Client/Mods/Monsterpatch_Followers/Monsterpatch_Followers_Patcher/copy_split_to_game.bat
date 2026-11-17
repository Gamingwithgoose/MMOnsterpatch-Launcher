@echo off
setlocal
set "GAME_ROOT=%~1"
if "%GAME_ROOT%"=="" (
  set /p GAME_ROOT=Enter your Monsterpatch game folder path: 
)

set "PATCHER_DLL=%~dp0src\bin\Release\net472\Monsterpatch_Followers_Patcher.dll"
set "RUNTIME_DLL=%~dp0..\Monsterpatch_Followers_Runtime\src\bin\Release\net472\Monsterpatch_Followers_Runtime.dll"

set "PATCHERS=%GAME_ROOT%\BepInEx\patchers"
set "PLUGINS=%GAME_ROOT%\BepInEx\plugins"

if not exist "%PATCHER_DLL%" (
  echo Patcher DLL not found: %PATCHER_DLL%
  echo Run C:\Monsterpatch_Mods\build_all_mods_click_me.bat first.
  pause
  exit /b 1
)
if not exist "%RUNTIME_DLL%" (
  echo Runtime DLL not found: %RUNTIME_DLL%
  echo Run C:\Monsterpatch_Mods\build_all_mods_click_me.bat first.
  pause
  exit /b 1
)

if not exist "%PATCHERS%" mkdir "%PATCHERS%"
if not exist "%PLUGINS%" mkdir "%PLUGINS%"
copy /Y "%PATCHER_DLL%" "%PATCHERS%\Monsterpatch_Followers_Patcher.dll"
copy /Y "%RUNTIME_DLL%" "%PLUGINS%\Monsterpatch_Followers_Runtime.dll"

echo.
echo Copied patcher to: %PATCHERS%\Monsterpatch_Followers_Patcher.dll
echo Copied runtime to: %PLUGINS%\Monsterpatch_Followers_Runtime.dll
echo.
echo Remove old follower plugin/patcher DLLs before testing.
pause
