@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "CLIENT_DIR=%~dp0"
for %%I in ("%CLIENT_DIR%..") do set "ROOT=%%~fI"
set "LIB=%CLIENT_DIR%lib"
if not exist "%LIB%" mkdir "%LIB%"

echo Preparing MMOnsterpatch client build reference lib...

REM Copy BepInEx/Harmony/Cecil refs from the bundled launcher payload.
set "BEPINEX_CORE=%ROOT%\Launcher\MMOnsterpatchLauncher.Wpf\Payload\BepInEx\core"
if exist "%BEPINEX_CORE%\BepInEx.dll" copy /Y "%BEPINEX_CORE%\BepInEx.dll" "%LIB%\BepInEx.dll" >nul
if exist "%BEPINEX_CORE%\0Harmony.dll" copy /Y "%BEPINEX_CORE%\0Harmony.dll" "%LIB%\0Harmony.dll" >nul
if exist "%BEPINEX_CORE%\Mono.Cecil.dll" copy /Y "%BEPINEX_CORE%\Mono.Cecil.dll" "%LIB%\Mono.Cecil.dll" >nul
if exist "%BEPINEX_CORE%\Mono.Cecil.Mdb.dll" copy /Y "%BEPINEX_CORE%\Mono.Cecil.Mdb.dll" "%LIB%\Mono.Cecil.Mdb.dll" >nul 2>nul
if exist "%BEPINEX_CORE%\Mono.Cecil.Pdb.dll" copy /Y "%BEPINEX_CORE%\Mono.Cecil.Pdb.dll" "%LIB%\Mono.Cecil.Pdb.dll" >nul 2>nul
if exist "%BEPINEX_CORE%\Mono.Cecil.Rocks.dll" copy /Y "%BEPINEX_CORE%\Mono.Cecil.Rocks.dll" "%LIB%\Mono.Cecil.Rocks.dll" >nul 2>nul

set "MANAGED="
if not "%MONSTERPATCH_GAME_DIR%"=="" (
  if exist "%MONSTERPATCH_GAME_DIR%\Monsterpatch_Data\Managed\Assembly-CSharp.dll" set "MANAGED=%MONSTERPATCH_GAME_DIR%\Monsterpatch_Data\Managed"
  if not defined MANAGED if exist "%MONSTERPATCH_GAME_DIR%\Assembly-CSharp.dll" set "MANAGED=%MONSTERPATCH_GAME_DIR%"
)
if not defined MANAGED if exist "%ProgramFiles(x86)%\Steam\steamapps\common\Monsterpatch\Monsterpatch_Data\Managed\Assembly-CSharp.dll" set "MANAGED=%ProgramFiles(x86)%\Steam\steamapps\common\Monsterpatch\Monsterpatch_Data\Managed"
if not defined MANAGED if exist "%ProgramFiles%\Steam\steamapps\common\Monsterpatch\Monsterpatch_Data\Managed\Assembly-CSharp.dll" set "MANAGED=%ProgramFiles%\Steam\steamapps\common\Monsterpatch\Monsterpatch_Data\Managed"
if not defined MANAGED if exist "%USERPROFILE%\Desktop\Monsterpatch\Monsterpatch_Data\Managed\Assembly-CSharp.dll" set "MANAGED=%USERPROFILE%\Desktop\Monsterpatch\Monsterpatch_Data\Managed"
if not defined MANAGED if exist "%ROOT%\Monsterpatch_Data\Managed\Assembly-CSharp.dll" set "MANAGED=%ROOT%\Monsterpatch_Data\Managed"

if not defined MANAGED (
  echo.
  echo ERROR: Could not find Monsterpatch_Data\Managed automatically.
  echo Set MONSTERPATCH_GAME_DIR to your Monsterpatch game folder, then rerun CLEAN_AND_BUILD_ALL.bat.
  echo Example:
  echo   set MONSTERPATCH_GAME_DIR=C:\Program Files ^(x86^)\Steam\steamapps\common\Monsterpatch
  echo   CLEAN_AND_BUILD_ALL.bat
  echo.
  exit /b 1
)

echo Using game Managed references from:
echo   %MANAGED%

set "MISSING="
for %%F in (
  Assembly-CSharp.dll
  UnityEngine.dll
  UnityEngine.CoreModule.dll
  UnityEngine.AnimationModule.dll
  UnityEngine.AudioModule.dll
  UnityEngine.Physics2DModule.dll
  UnityEngine.TextRenderingModule.dll
  UnityEngine.InputLegacyModule.dll
  UnityEngine.IMGUIModule.dll
  UnityEngine.UI.dll
  UnityEngine.UIModule.dll
  Unity.TextMeshPro.dll
) do (
  if exist "%MANAGED%\%%F" (
    copy /Y "%MANAGED%\%%F" "%LIB%\%%F" >nul
  ) else (
    echo Missing from Managed: %%F
    set "MISSING=1"
  )
)

for %%F in (
  Assembly-CSharp.dll
  BepInEx.dll
  0Harmony.dll
  Mono.Cecil.dll
  UnityEngine.dll
  UnityEngine.CoreModule.dll
  UnityEngine.AnimationModule.dll
  UnityEngine.AudioModule.dll
  UnityEngine.Physics2DModule.dll
  UnityEngine.TextRenderingModule.dll
  UnityEngine.InputLegacyModule.dll
  UnityEngine.IMGUIModule.dll
  UnityEngine.UI.dll
  UnityEngine.UIModule.dll
  Unity.TextMeshPro.dll
) do (
  if not exist "%LIB%\%%F" (
    echo Missing in Client\lib after prepare: %%F
    set "MISSING=1"
  )
)

if defined MISSING (
  echo.
  echo ERROR: Client lib preparation is incomplete.
  exit /b 1
)

echo.
echo Client lib is ready:
echo   %LIB%
endlocal
exit /b 0
