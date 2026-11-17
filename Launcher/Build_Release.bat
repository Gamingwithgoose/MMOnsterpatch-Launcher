@echo off
setlocal
cd /d "%~dp0"
dotnet build "MMOnsterpatchLauncher.Wpf\MMOnsterpatchLauncher.Wpf.csproj" -c Release
pause
