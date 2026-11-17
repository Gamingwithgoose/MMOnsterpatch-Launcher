@echo off
setlocal EnableExtensions
cd /d "%~dp0"

REM v0.15.0 MariaDB official mode. This reads ..\Config\database.ini and
REM passes the MariaDB connection to the Python server modules.
set "DBINI=%~dp0..\Config\database.ini"
set "MMONSTERPATCH_DB_HOST=127.0.0.1"
set "MMONSTERPATCH_DB_PORT=3307"
set "MMONSTERPATCH_DB_USER=root"
set "MMONSTERPATCH_DB_PASSWORD="
set "MMONSTERPATCH_DB_AUTH=mmonsterpatch_auth"
set "MMONSTERPATCH_DB_TRADINGPOST=mmonsterpatch_tradingpost"
set "MMONSTERPATCH_DB_SOCIAL=mmonsterpatch_social"

if exist "%DBINI%" (
  for /f "usebackq tokens=1,* delims==" %%A in ("%DBINI%") do (
    if /I "%%A"=="Host" set "MMONSTERPATCH_DB_HOST=%%B"
    if /I "%%A"=="Port" set "MMONSTERPATCH_DB_PORT=%%B"
    if /I "%%A"=="User" set "MMONSTERPATCH_DB_USER=%%B"
    if /I "%%A"=="Password" set "MMONSTERPATCH_DB_PASSWORD=%%B"
    if /I "%%A"=="AuthDatabase" set "MMONSTERPATCH_DB_AUTH=%%B"
    if /I "%%A"=="TradingPostDatabase" set "MMONSTERPATCH_DB_TRADINGPOST=%%B"
    if /I "%%A"=="SocialDatabase" set "MMONSTERPATCH_DB_SOCIAL=%%B"
  )
)

echo Starting current MMOnsterpatch Official Server using MariaDB %MMONSTERPATCH_DB_HOST%:%MMONSTERPATCH_DB_PORT%...
echo If this fails with PyMySQL missing, run: py -m pip install PyMySQL
python -u "MMOnsterpatchServer.py" --config "configs\worldserver.ini"
pause
endlocal
