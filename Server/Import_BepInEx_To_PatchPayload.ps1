param(
    [Parameter(Mandatory=$true)]
    [string]$BepInExSource,

    [string]$RuntimeRoot = "C:\[MMOnsterpatch] UnOfficial Server Repack"
)

$ErrorActionPreference = "Stop"

function Ensure-Dir($Path) {
    [System.IO.Directory]::CreateDirectory($Path) | Out-Null
}

function Copy-ManagedFiles($SourceDir, $DestDir, $Patterns) {
    if (!(Test-Path -LiteralPath $SourceDir)) {
        Write-Host "Skipped missing source: $SourceDir" -ForegroundColor Yellow
        return
    }

    Ensure-Dir $DestDir

    foreach ($pattern in $Patterns) {
        Get-ChildItem -LiteralPath $SourceDir -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "Copying $($_.Name) -> $DestDir"
            Copy-Item -LiteralPath $_.FullName -Destination $DestDir -Force
        }
    }
}

$BepInExSource = (Resolve-Path -LiteralPath $BepInExSource).Path
$PayloadFilesRoot = Join-Path $RuntimeRoot "PatchPayload\files"
$PatchRoot = Join-Path $PayloadFilesRoot "BepInEx"

Write-Host "Importing BepInEx managed payload" -ForegroundColor Cyan
Write-Host "Source : $BepInExSource"
Write-Host "Target : $PatchRoot"

Ensure-Dir (Join-Path $PatchRoot "patchers")
Ensure-Dir (Join-Path $PatchRoot "plugins")
Ensure-Dir (Join-Path $PatchRoot "config")

# Keep this intentionally narrow so we don't accidentally package unrelated user mods.
# Add more patterns here as we confirm exact MMOnsterpatch filenames.
$patcherPatterns = @(
    "*MMOnsterpatch*.dll",
    "*Monsterpatch*.dll"
)

$pluginPatterns = @(
    "*MMOnsterpatch*.dll",
    "*Monsterpatch*.dll"
)

$configPatterns = @(
    "*mmonsterpatch*.cfg",
    "*monsterpatch*.cfg",
    "*goose*.cfg"
)


function Copy-AllRealFiles($SourceDir, $DestDir) {
    if (!(Test-Path -LiteralPath $SourceDir)) {
        Write-Host "Skipped missing source: $SourceDir" -ForegroundColor Yellow
        return
    }

    Ensure-Dir $DestDir

    Get-ChildItem -LiteralPath $SourceDir -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -ne "_KEEP_FOLDER.txt" -and !$_.Name.StartsWith("PUT_")
    } | ForEach-Object {
        Write-Host "Copying $($_.Name) -> $DestDir"
        Copy-Item -LiteralPath $_.FullName -Destination $DestDir -Force
    }
}

Copy-ManagedFiles (Join-Path $BepInExSource "patchers") (Join-Path $PatchRoot "patchers") $patcherPatterns
Copy-ManagedFiles (Join-Path $BepInExSource "plugins")  (Join-Path $PatchRoot "plugins")  $pluginPatterns
Copy-ManagedFiles (Join-Path $BepInExSource "config")   (Join-Path $PatchRoot "config")   $configPatterns

Write-Host ""
Write-Host "BepInEx import complete." -ForegroundColor Green
Write-Host "Next: open Repack Manager -> Patch Manifest Builder -> Scan Payload Folder -> Build Manifest."

# Optional: if the source parent has Root bootstrap files, copy them too.
$RootSource = Join-Path (Split-Path -Parent $BepInExSource) "Root"
Copy-AllRealFiles $RootSource (Join-Path $PayloadFilesRoot "Root")
Copy-AllRealFiles (Join-Path $BepInExSource "core") (Join-Path $PatchRoot "core")
