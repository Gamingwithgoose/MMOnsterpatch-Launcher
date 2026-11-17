$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$LauncherProject = Join-Path $Root 'Launcher\MMOnsterpatchLauncher.Wpf\MMOnsterpatchLauncher.Wpf.csproj'
$AvaloniaLauncherProject = Join-Path $Root 'Launcher\MMOnsterpatchLauncher.Avalonia\MMOnsterpatchLauncher.Avalonia.csproj'
$AvaloniaCoreProject = Join-Path $Root 'Launcher\MMOnsterpatchLauncher.Core\MMOnsterpatchLauncher.Core.csproj'
$ServerManagerProject = Join-Path $Root 'Server\RepackManager\MMOnsterpatch.RepackManager.csproj'
$WebsiteProject = Join-Path $Root 'Server\AccountWebsite\MMOnsterpatch.AccountWebsite.csproj'
$ClientProject = Join-Path $Root 'Client\MMOnsterpatchOfficialServerPatcher\MMOnsterpatchAIOPatcher.csproj'
$ClientLib = Join-Path $Root 'Client\lib'
$ClientPrepareBat = Join-Path $Root 'Client\prepare_lib_from_game.bat'
$LauncherRelease = Join-Path $Root 'Releases\Launcher'
$ServerRelease = Join-Path $Root 'Releases\Server'
$ClientFilesRelease = Join-Path $Root 'Releases\Client Files'
$LauncherPayload = Join-Path $Root 'Launcher\MMOnsterpatchLauncher.Wpf\Payload'
$CrossPlatformPayloadRoot = Join-Path $Root 'Launcher\Payloads'
$PatchPayloadFiles = Join-Path $Root 'Server\PatchPayload\files'
$ServerSourceRoot = Join-Path $Root 'Server'

function Remove-FolderIfExists([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Ensure-Dir([string]$Path) {
    [System.IO.Directory]::CreateDirectory($Path) | Out-Null
}

function Clean-ProjectFolders([string]$ProjectDir) {
    Remove-FolderIfExists (Join-Path $ProjectDir 'bin')
    Remove-FolderIfExists (Join-Path $ProjectDir 'obj')
}

function Copy-FolderIfExists([string]$Source, [string]$Dest) {
    if (Test-Path -LiteralPath $Source) {
        Ensure-Dir $Dest
        Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $Dest -Recurse -Force
        }
    }
}

function Copy-ClientPatcherToPayload([string]$PayloadRoot, [string]$BuiltDll, [string]$BuiltPdb) {
    if ([string]::IsNullOrWhiteSpace($PayloadRoot)) { return }

    $destPatchers = Join-Path $PayloadRoot 'BepInEx\patchers'
    $destPlugins = Join-Path $PayloadRoot 'BepInEx\plugins'
    $destConfig = Join-Path $PayloadRoot 'BepInEx\config'
    Ensure-Dir $destPatchers
    Ensure-Dir $destPlugins
    Ensure-Dir $destConfig

    # Remove old standalone follower artifacts so this payload cannot accidentally use stale split-mod code.
    Remove-Item -LiteralPath (Join-Path $destPatchers 'Monsterpatch_Followers_Patcher.dll') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $destPlugins 'Monsterpatch_Followers_Runtime.dll') -Force -ErrorAction SilentlyContinue

    Copy-Item -LiteralPath $BuiltDll -Destination (Join-Path $destPatchers 'MMOnsterpatchOfficialServerPatcher.dll') -Force
    if (Test-Path -LiteralPath $BuiltPdb) {
        Copy-Item -LiteralPath $BuiltPdb -Destination (Join-Path $destPatchers 'MMOnsterpatchOfficialServerPatcher.pdb') -Force
    }
}


function Build-ClientPatcher() {
    if (!(Test-Path -LiteralPath $ClientProject)) {
        throw "Client patcher source is missing: $ClientProject"
    }
    if (!(Test-Path -LiteralPath $ClientPrepareBat)) {
        throw "Client reference prep script is missing: $ClientPrepareBat"
    }

    # Force a fresh Client\lib every build.  The old build flow always prepared refs first;
    # keeping stale/partial refs here caused MSBuild to miss Assembly-CSharp, Unity, BepInEx, Harmony, and Cecil.
    Write-Host 'Preparing client build reference lib from Monsterpatch install...' -ForegroundColor Cyan
    Write-Host "Client prep script: $ClientPrepareBat" -ForegroundColor DarkCyan
    Ensure-Dir $ClientLib
    Get-ChildItem -LiteralPath $ClientLib -Filter '*.dll' -File -ErrorAction SilentlyContinue | Remove-Item -Force
    & $ClientPrepareBat
    if ($LASTEXITCODE -ne 0) {
        throw 'Client reference lib preparation failed. Set MONSTERPATCH_GAME_DIR to your Monsterpatch game folder, then rerun CLEAN_AND_BUILD_ALL.bat.'
    }

    $requiredRefs = @(
        'Assembly-CSharp.dll',
        'BepInEx.dll',
        '0Harmony.dll',
        'Mono.Cecil.dll',
        'UnityEngine.dll',
        'UnityEngine.CoreModule.dll',
        'UnityEngine.AnimationModule.dll',
        'UnityEngine.AudioModule.dll',
        'UnityEngine.Physics2DModule.dll',
        'UnityEngine.TextRenderingModule.dll',
        'UnityEngine.InputLegacyModule.dll',
        'UnityEngine.IMGUIModule.dll',
        'UnityEngine.UI.dll',
        'UnityEngine.UIModule.dll',
        'Unity.TextMeshPro.dll'
    )
    $missingRefs = @()
    foreach ($refName in $requiredRefs) {
        if (!(Test-Path -LiteralPath (Join-Path $ClientLib $refName))) { $missingRefs += $refName }
    }
    if ($missingRefs.Count -gt 0) {
        throw ("Client\lib is incomplete after prepare. Missing: " + ($missingRefs -join ', '))
    }

    Write-Host "Building client patcher with Client\lib references: $ClientLib" -ForegroundColor Cyan
    # Do not pass MONSTERPATCH_LIB_DIR on the command line here.  A trailing backslash plus the
    # bracketed workspace path caused MSBuild reference resolution problems.  The csproj now uses
    # $(MSBuildProjectDirectory)\..\lib directly.
    dotnet build $ClientProject -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Client patcher build failed. This build will not fall back to an old DLL.' }

    $clientProjectDir = [System.IO.Path]::GetDirectoryName($ClientProject)
    $builtDll = Join-Path $clientProjectDir 'bin\Release\net472\MMOnsterpatchOfficialServerPatcher.dll'
    $builtPdb = Join-Path $clientProjectDir 'bin\Release\net472\MMOnsterpatchOfficialServerPatcher.pdb'
    if (!(Test-Path -LiteralPath $builtDll)) {
        throw "Client patcher build succeeded but DLL was not found: $builtDll"
    }

    Copy-ClientPatcherToPayload -PayloadRoot $LauncherPayload -BuiltDll $builtDll -BuiltPdb $builtPdb
    Copy-ClientPatcherToPayload -PayloadRoot (Join-Path $CrossPlatformPayloadRoot 'win-x64') -BuiltDll $builtDll -BuiltPdb $builtPdb
    Copy-ClientPatcherToPayload -PayloadRoot (Join-Path $CrossPlatformPayloadRoot 'linux-x64') -BuiltDll $builtDll -BuiltPdb $builtPdb
    Write-Host "Fresh client patcher copied into WPF, Windows Avalonia, and Linux Avalonia payloads." -ForegroundColor Green
}

function Sync-ClientPayloadCopies() {
    if (!(Test-Path -LiteralPath $LauncherPayload)) {
        throw "Launcher payload missing: $LauncherPayload"
    }

    Remove-FolderIfExists $PatchPayloadFiles
    Ensure-Dir $PatchPayloadFiles
    Copy-FolderIfExists $LauncherPayload $PatchPayloadFiles

    Remove-FolderIfExists $ClientFilesRelease
    Ensure-Dir $ClientFilesRelease
    Copy-FolderIfExists $LauncherPayload $ClientFilesRelease

    $clientReadme = @'
MMOnsterpatch Client Files

This folder is an extra copy of the exact client payload staged by the launcher for Online Mode.
It is provided for server/update payload work and manual inspection.
The launcher staging behavior is unchanged.

Included paths mirror the staged runtime:
- BepInEx\core
- BepInEx\patchers
- BepInEx\plugins
- Root\winhttp.dll
- Root\doorstop_config.ini
'@
    Set-Content -LiteralPath (Join-Path $ClientFilesRelease 'README_CLIENT_FILES.txt') -Value $clientReadme -Encoding UTF8

    Write-Host "Synced Launcher payload to Server\PatchPayload\files and Releases\Client Files." -ForegroundColor Green
}

function Publish-SingleExe([string]$Project, [string]$OutDir) {
    Ensure-Dir $OutDir
    dotnet publish $Project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $OutDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Project"
    }

    # The projects are configured for single-file output. Keep the release folder clean.
    Get-ChildItem -LiteralPath $OutDir -File | Where-Object { $_.Extension -ne '.exe' } | Remove-Item -Force
}

function Publish-AvaloniaLaunchersFlat([string]$Project, [string]$OutDir) {
    if (!(Test-Path -LiteralPath $Project)) {
        throw "Avalonia launcher project is missing: $Project"
    }

    $tmpRoot = Join-Path $Root 'Launcher\.publish_avalonia_tmp'
    Remove-FolderIfExists $tmpRoot
    Ensure-Dir $tmpRoot

    function Publish-OneAvaloniaRid([string]$Rid, [string]$ExpectedName, [string]$DestName) {
        $ridOut = Join-Path $tmpRoot $Rid
        Remove-FolderIfExists $ridOut
        Ensure-Dir $ridOut

        dotnet publish $Project `
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
            throw "Avalonia publish succeeded but expected launcher was not found: $published"
        }
        Copy-Item -LiteralPath $published -Destination (Join-Path $OutDir $DestName) -Force
    }

    Write-Host 'Publishing Avalonia Windows launcher beside the normal Windows launcher...' -ForegroundColor Cyan
    Publish-OneAvaloniaRid -Rid 'win-x64' -ExpectedName 'MMOnsterpatchLauncher.exe' -DestName 'MMOnsterpatchLauncher.Avalonia.exe'

    Write-Host 'Publishing Avalonia Linux launcher into the same Releases\Launcher folder...' -ForegroundColor Cyan
    Publish-OneAvaloniaRid -Rid 'linux-x64' -ExpectedName 'MMOnsterpatchLauncher' -DestName 'MMOnsterpatchLauncher-linux-x64'

    Write-Host 'Avalonia payloads are embedded into the launcher binaries; no external Payloads folder will be shipped.' -ForegroundColor Cyan
    Remove-FolderIfExists (Join-Path $OutDir 'Payloads')

    $deckHelper = @'
#!/usr/bin/env bash
set -e
cd "$(dirname "$0")"
chmod +x ./MMOnsterpatchLauncher-linux-x64 || true
./MMOnsterpatchLauncher-linux-x64 "$@"
'@
    Set-Content -LiteralPath (Join-Path $OutDir 'RUN_LINUX_LAUNCHER_FROM_DECK.sh') -Value $deckHelper -Encoding UTF8

    $desktopEntry = @'
[Desktop Entry]
Type=Application
Name=MMOnsterpatch Launcher
Comment=Launch MMOnsterpatch online/offline setup
Exec=sh -c 'cd "$(dirname "$1")"; chmod +x ./MMOnsterpatchLauncher-linux-x64; exec ./MMOnsterpatchLauncher-linux-x64' dummy %k
Terminal=false
Categories=Game;
'@
    Set-Content -LiteralPath (Join-Path $OutDir 'MMOnsterpatchLauncher.desktop') -Value $desktopEntry -Encoding UTF8

    $readme = @'
MMOnsterpatch Launcher Release Folder
====================================

Windows stable/current launcher:
  MMOnsterpatchLauncher.exe

Windows Avalonia cross-platform test launcher:
  MMOnsterpatchLauncher.Avalonia.exe

Linux / Steam Deck Avalonia test launcher:
  Preferred after permissions are fixed:
    Double-click MMOnsterpatchLauncher-linux-x64

  First run / if Linux asks what to open with:
    bash RUN_LINUX_LAUNCHER_FROM_DECK.sh

The Linux launcher is intentionally placed in this same Releases/Launcher folder for bulk transfer testing.

Payloads:
  Baked into the Avalonia launcher. No external Payloads folder is required for players.

Linux details:
  Game executable: Monsterpatch.x86_64
  Steam AppID: 2495320
  Online Steam launch option: ./run_bepinex.sh %command%

Linux note:
  Linux will not run a downloaded/copied binary until it has the executable bit.
  The helper script fixes that once. After that, double-clicking the launcher should work in normal Linux file managers.
'@
    Set-Content -LiteralPath (Join-Path $OutDir 'README_LAUNCHER_RELEASE.txt') -Value $readme -Encoding UTF8

    try {
        Write-Host 'Creating Steam Deck tar.gz with Linux executable permissions preserved...' -ForegroundColor Cyan
        $tarBuilder = Join-Path $Root 'Launcher\.tarbuilder'
        Remove-FolderIfExists $tarBuilder
        Ensure-Dir $tarBuilder

        $tarCsproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@
        Set-Content -LiteralPath (Join-Path $tarBuilder 'TarBuilder.csproj') -Value $tarCsproj -Encoding UTF8

        $tarProgram = @'
using System.Formats.Tar;
using System.IO.Compression;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: TarBuilder <release-folder> <output.tar.gz>");
    return 2;
}

string release = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
string baseName = "MMOnsterpatchLauncher";

if (File.Exists(output))
    File.Delete(output);

await using FileStream fileOut = File.Create(output);
await using GZipStream gzip = new(fileOut, CompressionLevel.SmallestSize);
using TarWriter writer = new(gzip, TarEntryFormat.Pax, leaveOpen: false);

foreach (string file in Directory.EnumerateFiles(release, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
{
    if (Path.GetFullPath(file).Equals(output, StringComparison.OrdinalIgnoreCase))
        continue;

    string rel = Path.GetRelativePath(release, file).Replace('\\', '/');
    string name = baseName + "/" + rel;
    bool executable = rel.Equals("MMOnsterpatchLauncher-linux-x64", StringComparison.Ordinal)
        || rel.Equals("RUN_LINUX_LAUNCHER_FROM_DECK.sh", StringComparison.Ordinal)
        || rel.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase);

    await using FileStream input = File.OpenRead(file);
    PaxTarEntry entry = new(TarEntryType.RegularFile, name)
    {
        DataStream = input,
        Mode = executable ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                          : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
        ModificationTime = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero)
    };
    writer.WriteEntry(entry);
}

Console.WriteLine(output);
return 0;
'@
        Set-Content -LiteralPath (Join-Path $tarBuilder 'Program.cs') -Value $tarProgram -Encoding UTF8

        $deckTar = Join-Path $OutDir 'MMOnsterpatchLauncher_Deck.tar.gz'
        dotnet run --project (Join-Path $tarBuilder 'TarBuilder.csproj') -c Release -- $OutDir $deckTar
        if ($LASTEXITCODE -ne 0) { throw 'Deck tar.gz builder failed.' }
        Remove-FolderIfExists $tarBuilder
    }
    catch {
        Write-Host ("WARNING: Could not create Deck tar.gz with executable permissions: " + $_.Exception.Message) -ForegroundColor Yellow
        Write-Host 'The flat release folder is still valid; run bash RUN_LINUX_LAUNCHER_FROM_DECK.sh once on Deck if needed.' -ForegroundColor Yellow
    }

    Remove-FolderIfExists $tmpRoot
}

Write-Host '=== MMOnsterpatch Clean + Build All v0.19.6 ClipboardCompileFix ===' -ForegroundColor Magenta
Write-Host "Workspace root: $Root"
Write-Host ''
Write-Host 'Release layout after this script:' -ForegroundColor Cyan
Write-Host "  $LauncherRelease"
Write-Host "    MMOnsterpatchLauncher.exe"
Write-Host "    MMOnsterpatchLauncher.Avalonia.exe"
Write-Host "    MMOnsterpatchLauncher-linux-x64"
Write-Host "    payloads baked into Avalonia launchers"
Write-Host "  $ServerRelease"
Write-Host "    MMOnsterpatchRepackManager.exe"
Write-Host "    Config\ OfficialServer\ PatchPayload\ Logs\ Backups\ AccountWebsite\"
Write-Host "  $ClientFilesRelease"
Write-Host "    BepInEx\ Root\ README_CLIENT_FILES.txt"
Write-Host ''

Write-Host 'Cleaning source project bin/obj folders...' -ForegroundColor Cyan
Clean-ProjectFolders (Split-Path -Parent $LauncherProject)
Clean-ProjectFolders (Split-Path -Parent $AvaloniaLauncherProject)
Clean-ProjectFolders (Split-Path -Parent $AvaloniaCoreProject)
Clean-ProjectFolders (Split-Path -Parent $ServerManagerProject)
Clean-ProjectFolders (Split-Path -Parent $WebsiteProject)
Clean-ProjectFolders (Split-Path -Parent $ClientProject)

Write-Host 'Cleaning release output folders...' -ForegroundColor Cyan
Remove-FolderIfExists $LauncherRelease
Remove-FolderIfExists $ServerRelease
Remove-FolderIfExists $ClientFilesRelease
Ensure-Dir $LauncherRelease
Ensure-Dir $ServerRelease

Write-Host 'Building required client patcher before launcher publish...' -ForegroundColor Cyan
Build-ClientPatcher
Write-Host 'Syncing client payload copies for server updates and manual inspection...' -ForegroundColor Cyan
Sync-ClientPayloadCopies

Write-Host 'Publishing launcher single EXE...' -ForegroundColor Cyan
Publish-SingleExe $LauncherProject $LauncherRelease

Write-Host 'Publishing Avalonia cross-platform launchers into the same Launcher release folder...' -ForegroundColor Cyan
Publish-AvaloniaLaunchersFlat $AvaloniaLauncherProject $LauncherRelease

Write-Host 'Publishing server manager single EXE to the root of Releases\Server...' -ForegroundColor Cyan
Publish-SingleExe $ServerManagerProject $ServerRelease

Write-Host 'Publishing Account Website single EXE beside the manager...' -ForegroundColor Cyan
Publish-SingleExe $WebsiteProject (Join-Path $ServerRelease 'AccountWebsite')

Write-Host 'Copying server runtime folders into Releases\Server...' -ForegroundColor Cyan
Copy-FolderIfExists (Join-Path $ServerSourceRoot 'Config')        (Join-Path $ServerRelease 'Config')
Copy-FolderIfExists (Join-Path $ServerSourceRoot 'OfficialServer') (Join-Path $ServerRelease 'OfficialServer')
Copy-FolderIfExists (Join-Path $ServerSourceRoot 'PatchPayload')  (Join-Path $ServerRelease 'PatchPayload')
Copy-FolderIfExists (Join-Path $ServerSourceRoot 'Backups')       (Join-Path $ServerRelease 'Backups')
Copy-FolderIfExists (Join-Path $ServerSourceRoot 'Logs')          (Join-Path $ServerRelease 'Logs')

# MariaDB-native runtime should not ship stale SQLite data, archived characters, old server logs, or Python cache.
Remove-FolderIfExists (Join-Path $ServerRelease 'OfficialServer\data')
Remove-FolderIfExists (Join-Path $ServerRelease 'OfficialServer\logs')
Get-ChildItem -LiteralPath (Join-Path $ServerRelease 'OfficialServer') -Directory -Recurse -Filter '__pycache__' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Ensure-Dir (Join-Path $ServerRelease 'OfficialServer\data')
Ensure-Dir (Join-Path $ServerRelease 'OfficialServer\logs')

Ensure-Dir (Join-Path $ServerRelease 'Logs\Manager')
Ensure-Dir (Join-Path $ServerRelease 'Logs\Auth')
Ensure-Dir (Join-Path $ServerRelease 'Logs\World')
Ensure-Dir (Join-Path $ServerRelease 'Logs\Website')
Ensure-Dir (Join-Path $ServerRelease 'Backups')

Copy-Item -LiteralPath (Join-Path $ServerSourceRoot 'README.md') -Destination (Join-Path $ServerRelease 'README.md') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $ServerSourceRoot 'CHANGELOG.md') -Destination (Join-Path $ServerRelease 'CHANGELOG.md') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $ServerSourceRoot 'chatreadme.txt') -Destination (Join-Path $ServerRelease 'chatreadme.txt') -Force -ErrorAction SilentlyContinue

Copy-Item -LiteralPath (Join-Path $Root 'README.md') -Destination (Join-Path $ServerRelease 'README_WORKSPACE.md') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $Root 'CHANGELOG.md') -Destination (Join-Path $ServerRelease 'CHANGELOG_WORKSPACE.md') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $Root 'DEPLOYMENT.md') -Destination (Join-Path $ServerRelease 'DEPLOYMENT.md') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $Root 'TESTING.md') -Destination (Join-Path $ServerRelease 'TESTING.md') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $Root 'CREATE_OR_UPDATE_MARIADB_USER_SQL3307_SERVER_SIDE.bat') -Destination (Join-Path $ServerRelease 'CREATE_OR_UPDATE_MARIADB_USER_SQL3307_SERVER_SIDE.bat') -Force -ErrorAction SilentlyContinue

$note = @'
MMOnsterpatch Server Release Folder

This folder is intentionally self-contained for testing/deployment.
Run MMOnsterpatchRepackManager.exe from this folder.
The manager treats this folder as the server root and expects Config, OfficialServer, PatchPayload, Logs, Backups, and AccountWebsite beside it. MariaDB now owns account/social/trading persistence.

You can copy this entire Releases\Server folder to your server desktop for testing, then move the same folder to C:\ later.
Do not copy only the EXE unless you only want to inspect the UI.
'@
Set-Content -LiteralPath (Join-Path $ServerRelease 'SERVER_RELEASE_README.txt') -Value $note -Encoding UTF8

Write-Host ''
Write-Host 'Build complete.' -ForegroundColor Green
Write-Host "Launcher release: $LauncherRelease"
Write-Host '  - MMOnsterpatchLauncher.exe'
Write-Host '  - MMOnsterpatchLauncher.Avalonia.exe'
Write-Host '  - MMOnsterpatchLauncher-linux-x64'
Write-Host '  - payloads baked into Avalonia launchers'
Write-Host "Server release  : $ServerRelease"
Write-Host "Client files    : $ClientFilesRelease"
Write-Host ''
Write-Host 'Copy the entire Releases\Server folder to the server for testing, not only the EXE.' -ForegroundColor Yellow
