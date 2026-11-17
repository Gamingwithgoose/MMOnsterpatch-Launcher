@echo off
setlocal
cd /d "%~dp0"
echo ============================================================
echo MMOnsterpatch v0.18.4 PrizeCrystalOwnerCleanupFix Official Build
echo ============================================================
echo This builds the source workspace. MariaDB user/config setup is done on the SERVER machine.
echo.
call CLEAN_AND_BUILD_ALL.bat
endlocal
