@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "MYSQL_EXE="
for %%P in ("C:\Program Files\MariaDB 12.3\bin\mysql.exe" "C:\Program Files\MariaDB 12.2\bin\mysql.exe" "C:\Program Files\MariaDB 12.1\bin\mysql.exe" "C:\Program Files\MariaDB 12.0\bin\mysql.exe" "C:\Program Files\MariaDB 11.8\bin\mysql.exe" "C:\Program Files\MariaDB 11.4\bin\mysql.exe" "C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe") do (
    if exist %%~P set "MYSQL_EXE=%%~P"
)
if not defined MYSQL_EXE (
    echo Could not auto-detect mysql.exe.
    echo Enter the full path to mysql.exe, for example:
    echo C:\Program Files\MariaDB 12.3\bin\mysql.exe
    set /p MYSQL_EXE=Path to mysql.exe: 
)
if not exist "%MYSQL_EXE%" (
    echo mysql.exe not found: %MYSQL_EXE%
    pause
    exit /b 1
)
set /p MMP_DB_PASS=Enter NEW password for MariaDB user mmonsterpatch: 
if "%MMP_DB_PASS%"=="" (
    echo Password cannot be empty.
    pause
    exit /b 1
)
set "TMP_SQL=%TEMP%\mmp_create_user_%RANDOM%.sql"
>"%TMP_SQL%" echo CREATE USER IF NOT EXISTS 'mmonsterpatch'@'localhost' IDENTIFIED BY '%MMP_DB_PASS%';
>>"%TMP_SQL%" echo CREATE USER IF NOT EXISTS 'mmonsterpatch'@'127.0.0.1' IDENTIFIED BY '%MMP_DB_PASS%';
>>"%TMP_SQL%" echo ALTER USER 'mmonsterpatch'@'localhost' IDENTIFIED BY '%MMP_DB_PASS%';
>>"%TMP_SQL%" echo ALTER USER 'mmonsterpatch'@'127.0.0.1' IDENTIFIED BY '%MMP_DB_PASS%';
>>"%TMP_SQL%" echo GRANT ALL PRIVILEGES ON mmonsterpatch_auth.* TO 'mmonsterpatch'@'localhost';
>>"%TMP_SQL%" echo GRANT ALL PRIVILEGES ON mmonsterpatch_tradingpost.* TO 'mmonsterpatch'@'localhost';
>>"%TMP_SQL%" echo GRANT ALL PRIVILEGES ON mmonsterpatch_social.* TO 'mmonsterpatch'@'localhost';
>>"%TMP_SQL%" echo GRANT ALL PRIVILEGES ON mmonsterpatch_files.* TO 'mmonsterpatch'@'localhost';
>>"%TMP_SQL%" echo GRANT ALL PRIVILEGES ON mmonsterpatch_auth.* TO 'mmonsterpatch'@'127.0.0.1';
>>"%TMP_SQL%" echo GRANT ALL PRIVILEGES ON mmonsterpatch_tradingpost.* TO 'mmonsterpatch'@'127.0.0.1';
>>"%TMP_SQL%" echo GRANT ALL PRIVILEGES ON mmonsterpatch_social.* TO 'mmonsterpatch'@'127.0.0.1';
>>"%TMP_SQL%" echo GRANT ALL PRIVILEGES ON mmonsterpatch_files.* TO 'mmonsterpatch'@'127.0.0.1';
>>"%TMP_SQL%" echo FLUSH PRIVILEGES;
echo.
echo Creating/updating MariaDB user mmonsterpatch on 127.0.0.1:3307.
echo You will be prompted for the MariaDB admin/root password.
"%MYSQL_EXE%" -h 127.0.0.1 -P 3307 -u root -p < "%TMP_SQL%"
if errorlevel 1 (
    del "%TMP_SQL%" >nul 2>nul
    echo Failed to create/update MariaDB user.
    pause
    exit /b 1
)
del "%TMP_SQL%" >nul 2>nul
set "CONFIG=%~dp0Server\Config\database.ini"
if not exist "%CONFIG%" set "CONFIG=%~dp0Config\database.ini"
if exist "%CONFIG%" (
    >"%CONFIG%" echo [Database]
    >>"%CONFIG%" echo Provider=MariaDB
    >>"%CONFIG%" echo Host=127.0.0.1
    >>"%CONFIG%" echo Port=3307
    >>"%CONFIG%" echo User=mmonsterpatch
    >>"%CONFIG%" echo Password=%MMP_DB_PASS%
    >>"%CONFIG%" echo AuthDatabase=mmonsterpatch_auth
    >>"%CONFIG%" echo TradingPostDatabase=mmonsterpatch_tradingpost
    >>"%CONFIG%" echo SocialDatabase=mmonsterpatch_social
    >>"%CONFIG%" echo FilesDatabase=mmonsterpatch_files
    echo Updated: %CONFIG%
) else (
    echo Could not find Server\Config\database.ini beside this BAT.
    echo Manually set User=mmonsterpatch, Password=%MMP_DB_PASS%, Port=3307.
)
echo.
echo Testing login as mmonsterpatch...
"%MYSQL_EXE%" -h 127.0.0.1 -P 3307 -u mmonsterpatch -p%MMP_DB_PASS% -e "SHOW DATABASES;"
echo.
echo Done.
pause
endlocal
