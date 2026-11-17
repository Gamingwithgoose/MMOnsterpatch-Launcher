@echo off
setlocal
cd /d "%~dp0.."
set STAMP=%DATE:~-4%%DATE:~4,2%%DATE:~7,2%_%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%
set STAMP=%STAMP: =0%
set DEST=Backups\sqlite_%STAMP%
mkdir "%DEST%" 2>nul
for %%F in (bank.db social.db social.db-wal social.db-shm) do (
  if exist "OfficialServer\data\%%F" copy /Y "OfficialServer\data\%%F" "%DEST%\%%F" >nul
)
echo Backed up current SQLite DB files to %CD%\%DEST%
pause
endlocal
