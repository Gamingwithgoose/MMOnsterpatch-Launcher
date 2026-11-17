@echo off
setlocal
set "ROOT=%~dp0"

echo Building Monsterpatch Followers Patcher...
dotnet build "%ROOT%Monsterpatch_Followers_Patcher\src\Monsterpatch_Followers_Patcher.csproj" -c Release
if errorlevel 1 goto fail

echo.
echo Building Monsterpatch Followers Runtime...
dotnet build "%ROOT%Monsterpatch_Followers_Runtime\src\Monsterpatch_Followers_Runtime.csproj" -c Release
if errorlevel 1 goto fail

echo.
echo Build complete.
echo Patcher: %ROOT%Monsterpatch_Followers_Patcher\src\bin\Release\net472\Monsterpatch_Followers_Patcher.dll
echo Runtime: %ROOT%Monsterpatch_Followers_Runtime\src\bin\Release\net472\Monsterpatch_Followers_Runtime.dll
echo.
pause
exit /b 0

:fail
echo.
echo Build failed.
pause
exit /b 1
