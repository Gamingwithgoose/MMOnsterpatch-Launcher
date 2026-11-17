@echo off
setlocal
set "ROOT=%~dp0"
set "GAME_ROOT=%~1"
if "%GAME_ROOT%"=="" (
  set /p GAME_ROOT=Enter your Monsterpatch game folder path: 
)

set "PATCHER_DLL=%ROOT%Monsterpatch_Followers_Patcher\src\bin\Release\net472\Monsterpatch_Followers_Patcher.dll"
set "RUNTIME_DLL=%ROOT%Monsterpatch_Followers_Runtime\src\bin\Release\net472\Monsterpatch_Followers_Runtime.dll"
set "PATCHERS=%GAME_ROOT%\BepInEx\patchers"
set "PLUGINS=%GAME_ROOT%\BepInEx\plugins"
set "CONFIG=%GAME_ROOT%\BepInEx\config"

if not exist "%PATCHER_DLL%" (
  echo Patcher DLL not found: %PATCHER_DLL%
  echo Run build_all.bat first.
  pause
  exit /b 1
)
if not exist "%RUNTIME_DLL%" (
  echo Runtime DLL not found: %RUNTIME_DLL%
  echo Run build_all.bat first.
  pause
  exit /b 1
)

if not exist "%PATCHERS%" mkdir "%PATCHERS%"
if not exist "%PLUGINS%" mkdir "%PLUGINS%"
if not exist "%CONFIG%" mkdir "%CONFIG%"

copy /Y "%PATCHER_DLL%" "%PATCHERS%\Monsterpatch_Followers_Patcher.dll"
copy /Y "%RUNTIME_DLL%" "%PLUGINS%\Monsterpatch_Followers_Runtime.dll"

if not exist "%CONFIG%\goose.monsterpatch.followersruntime.cfg" (
  copy /Y "%ROOT%config_template\goose.monsterpatch.followersruntime.cfg" "%CONFIG%\goose.monsterpatch.followersruntime.cfg"
  echo Created config template: %CONFIG%\goose.monsterpatch.followersruntime.cfg
) else (
  echo Existing config preserved: %CONFIG%\goose.monsterpatch.followersruntime.cfg
)

echo.
echo Installed followers split-runtime v0.4.7.
echo Patcher: %PATCHERS%\Monsterpatch_Followers_Patcher.dll
echo Runtime: %PLUGINS%\Monsterpatch_Followers_Runtime.dll
echo.
echo Remove old duplicate follower DLLs if you have any renamed copies in patchers/plugins.
pause
