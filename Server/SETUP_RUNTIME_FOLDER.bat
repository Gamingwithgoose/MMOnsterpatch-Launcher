@echo off
setlocal

set "RUNTIME=C:\[MMOnsterpatch] UnOfficial Server Repack"

echo.
echo MMOnsterpatch Repack Runtime Folder Setup
echo ----------------------------------------
echo This will create:
echo %RUNTIME%
echo.
pause

mkdir "%RUNTIME%" 2>nul
mkdir "%RUNTIME%\RepackManager" 2>nul
mkdir "%RUNTIME%\AuthServer" 2>nul
mkdir "%RUNTIME%\WorldServer" 2>nul
mkdir "%RUNTIME%\Website" 2>nul
mkdir "%RUNTIME%\Config" 2>nul
mkdir "%RUNTIME%\Data" 2>nul
mkdir "%RUNTIME%\Data\Catalog" 2>nul
mkdir "%RUNTIME%\Data\Manifests" 2>nul
mkdir "%RUNTIME%\PatchPayload" 2>nul
mkdir "%RUNTIME%\PatchPayload\files" 2>nul
mkdir "%RUNTIME%\PatchPayload\files\BepInEx" 2>nul
mkdir "%RUNTIME%\PatchPayload\files\BepInEx\patchers" 2>nul
mkdir "%RUNTIME%\PatchPayload\files\BepInEx\plugins" 2>nul
mkdir "%RUNTIME%\PatchPayload\files\BepInEx\config" 2>nul
mkdir "%RUNTIME%\PatchPayload\manifest_backups" 2>nul
mkdir "%RUNTIME%\SQL" 2>nul
mkdir "%RUNTIME%\SQL\base" 2>nul
mkdir "%RUNTIME%\SQL\updates" 2>nul
mkdir "%RUNTIME%\Logs" 2>nul
mkdir "%RUNTIME%\Logs\Auth" 2>nul
mkdir "%RUNTIME%\Logs\World" 2>nul
mkdir "%RUNTIME%\Logs\Website" 2>nul
mkdir "%RUNTIME%\Logs\Manager" 2>nul
mkdir "%RUNTIME%\Backups" 2>nul
mkdir "%RUNTIME%\Tools" 2>nul

echo.
echo Copying runtime config/data folders...
xcopy "%~dp0Config" "%RUNTIME%\Config" /E /I /Y
xcopy "%~dp0Data" "%RUNTIME%\Data" /E /I /Y
xcopy "%~dp0PatchPayload" "%RUNTIME%\PatchPayload" /E /I /Y
xcopy "%~dp0SQL" "%RUNTIME%\SQL" /E /I /Y
xcopy "%~dp0Logs" "%RUNTIME%\Logs" /E /I /Y
xcopy "%~dp0Backups" "%RUNTIME%\Backups" /E /I /Y
xcopy "%~dp0Tools" "%RUNTIME%\Tools" /E /I /Y

echo.
echo Runtime folder is ready:
echo %RUNTIME%
echo.
echo Next:
echo 1. Publish RepackManager to %RUNTIME%\RepackManager
echo 2. Publish AuthServer to %RUNTIME%\AuthServer
echo 3. Publish WorldServer to %RUNTIME%\WorldServer
echo 4. Publish AccountWebsite to %RUNTIME%\Website
echo.
pause
