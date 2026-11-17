@echo off
setlocal
cd /d "%~dp0"

echo Publishing MMOnsterpatchLauncher.exe to:
echo C:\[MMOnsterpatch] UnOfficial Launcher Releases\Release\
echo.

dotnet publish ".\MMOnsterpatchLauncher.Wpf\MMOnsterpatchLauncher.Wpf.csproj" /p:PublishProfile=Release-SingleExe-win-x64

pause
