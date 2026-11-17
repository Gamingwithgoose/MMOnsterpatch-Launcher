param(
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'

$launcherRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $launcherRoot 'MMOnsterpatchLauncher.Avalonia\MMOnsterpatchLauncher.Avalonia.csproj'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $launcherRoot 'ReleasePack_AvaloniaCrossPlatform'
}
$tmpRoot = Join-Path $launcherRoot '.publish_avalonia_tmp'

function Remove-FolderIfExists([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Ensure-Dir([string]$Path) {
    [System.IO.Directory]::CreateDirectory($Path) | Out-Null
}

function Publish-FlatSingleFile([string]$Rid, [string]$ExpectedName, [string]$DestinationName) {
    $ridOut = Join-Path $tmpRoot $Rid
    Remove-FolderIfExists $ridOut
    Ensure-Dir $ridOut

    Write-Host "Publishing Avalonia launcher for $Rid..." -ForegroundColor Cyan
    dotnet publish $project `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $ridOut

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for Avalonia launcher RID $Rid"
    }

    $published = Join-Path $ridOut $ExpectedName
    if (!(Test-Path -LiteralPath $published)) {
        throw "Publish succeeded but expected launcher was not found: $published"
    }

    Copy-Item -LiteralPath $published -Destination (Join-Path $OutputRoot $DestinationName) -Force
}

Remove-FolderIfExists $OutputRoot
Remove-FolderIfExists $tmpRoot
Ensure-Dir $OutputRoot
Ensure-Dir $tmpRoot

# Keep the test Avalonia Windows launcher from overwriting the known-good WPF launcher name.
Publish-FlatSingleFile -Rid 'win-x64' -ExpectedName 'MMOnsterpatchLauncher.exe' -DestinationName 'MMOnsterpatchLauncher.Avalonia.exe'

# Put the Linux launcher in the exact same release folder as the Windows launchers.
Publish-FlatSingleFile -Rid 'linux-x64' -ExpectedName 'MMOnsterpatchLauncher' -DestinationName 'MMOnsterpatchLauncher-linux-x64'

$deckHelper = @'
#!/usr/bin/env bash
set -e
cd "$(dirname "$0")"
chmod +x ./MMOnsterpatchLauncher-linux-x64 || true
./MMOnsterpatchLauncher-linux-x64 "$@"
'@
Set-Content -LiteralPath (Join-Path $OutputRoot 'RUN_LINUX_LAUNCHER_FROM_DECK.sh') -Value $deckHelper -Encoding UTF8

$desktopEntry = @'
[Desktop Entry]
Type=Application
Name=MMOnsterpatch Launcher
Comment=Launch MMOnsterpatch online/offline setup
Exec=sh -c 'cd "$(dirname "$1")"; chmod +x ./MMOnsterpatchLauncher-linux-x64; exec ./MMOnsterpatchLauncher-linux-x64' dummy %k
Terminal=false
Categories=Game;
'@
Set-Content -LiteralPath (Join-Path $OutputRoot 'MMOnsterpatchLauncher.desktop') -Value $desktopEntry -Encoding UTF8

$readme = @'
MMOnsterpatch Avalonia Cross-Platform Launcher Bulk Test
=======================================================

This folder is intentionally flat so bulk testing is easier.

Windows / current known-good launcher:
  Run MMOnsterpatchLauncher.exe

Windows / Avalonia test launcher:
  Run MMOnsterpatchLauncher.Avalonia.exe
  Payload: baked into the launcher. No external Payloads folder is required.
  Game executable: Monsterpatch.exe

Linux / Steam Deck:
  Preferred after permissions are fixed:
    Double-click MMOnsterpatchLauncher-linux-x64

  First run / if Linux asks what to open with:
    bash RUN_LINUX_LAUNCHER_FROM_DECK.sh

  Or manually:
    chmod +x ./MMOnsterpatchLauncher-linux-x64
    ./MMOnsterpatchLauncher-linux-x64

  Payload: baked into the launcher. No external Payloads folder is required.
  Game executable: Monsterpatch.x86_64
  Online launch: launcher directly runs staged ./run_bepinex.sh after staging

Notes:
  - Linux will not run a downloaded/copied binary until it has the executable bit.
    The helper script fixes that once. After that, double-clicking the launcher should work in normal Linux file managers.
  - PowerShell execution policy is bypassed automatically by CLEAN_AND_BUILD_ALL.bat and Build_Avalonia_Launcher_Pack.bat.
  - If you call the .ps1 directly and Windows blocks it, use:
      powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build_Avalonia_Launcher_Pack.ps1
'@
Set-Content -LiteralPath (Join-Path $OutputRoot 'README_AVALONIA_CROSS_PLATFORM.txt') -Value $readme -Encoding UTF8

Remove-FolderIfExists $tmpRoot

Write-Host "Done: $OutputRoot" -ForegroundColor Green
Write-Host 'Flat launcher folder contains:' -ForegroundColor Green
Write-Host '  MMOnsterpatchLauncher.Avalonia.exe'
Write-Host '  MMOnsterpatchLauncher-linux-x64'
Write-Host '  payloads baked into the Avalonia launcher binaries'
