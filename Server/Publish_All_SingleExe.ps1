param(
    [string]$RuntimeRoot = "",
    [switch]$SkipRuntimeCopy,
    [switch]$SkipBepInExImport,
    [string]$BepInExSource = ""
)

$ErrorActionPreference = "Stop"

function Write-Step($Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Ensure-Dir($Path) {
    [System.IO.Directory]::CreateDirectory($Path) | Out-Null
}

function Copy-FolderIfExists($Source, $Dest) {
    if (Test-Path -LiteralPath $Source) {
        Ensure-Dir $Dest
        Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $Dest -Recurse -Force
        }
    }
}

function Publish-Project($ProjectPath, $OutputPath) {
    Write-Step "Publishing $ProjectPath"
    Ensure-Dir $OutputPath

    dotnet publish $ProjectPath `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $OutputPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath"
    }

    Get-ChildItem -LiteralPath $OutputPath -File | Where-Object { $_.Extension -ne '.exe' } | Remove-Item -Force
}

$SourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$WorkspaceRoot = Split-Path -Parent $SourceRoot
if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
    $RuntimeRoot = Join-Path $WorkspaceRoot "Releases\Server"
}

Write-Host "MMOnsterpatch Server Self-Contained Release Publisher" -ForegroundColor Magenta
Write-Host "Source root : $SourceRoot"
Write-Host "Runtime root: $RuntimeRoot"
Write-Host "Layout      : manager EXE + server folders beside it"

Write-Step "Checking .NET SDK"
dotnet --version

Write-Step "Cleaning runtime root"
if (Test-Path -LiteralPath $RuntimeRoot) {
    Remove-Item -LiteralPath $RuntimeRoot -Recurse -Force
}
Ensure-Dir $RuntimeRoot

Write-Step "Creating runtime folders"
$runtimeFolders = @(
    "Config",
    "OfficialServer",
    "AuthServer",
    "WorldServer",
    "AccountWebsite",
    "Data",
    "Data\Catalog",
    "Data\Manifests",
    "Data\CompatibilityReports",
    "Data\GameUpdates",
    "PatchPayload",
    "PatchPayload\files",
    "PatchPayload\files\BepInEx",
    "PatchPayload\files\BepInEx\core",
    "PatchPayload\files\Root",
    "PatchPayload\files\BepInEx\patchers",
    "PatchPayload\files\BepInEx\plugins",
    "PatchPayload\files\BepInEx\config",
    "PatchPayload\manifest_backups",
    "SQL",
    "SQL\base",
    "SQL\updates",
    "Logs",
    "Logs\Auth",
    "Logs\World",
    "Logs\Website",
    "Logs\Manager",
    "Backups",
    "Tools"
)

foreach ($folder in $runtimeFolders) {
    Ensure-Dir (Join-Path $RuntimeRoot $folder)
}

if (!$SkipRuntimeCopy) {
    Write-Step "Copying runtime folders beside manager EXE"
    Copy-FolderIfExists (Join-Path $SourceRoot "Config")        (Join-Path $RuntimeRoot "Config")
    Copy-FolderIfExists (Join-Path $SourceRoot "OfficialServer") (Join-Path $RuntimeRoot "OfficialServer")
    Copy-FolderIfExists (Join-Path $SourceRoot "Data")          (Join-Path $RuntimeRoot "Data")
    Copy-FolderIfExists (Join-Path $SourceRoot "PatchPayload")  (Join-Path $RuntimeRoot "PatchPayload")
    Copy-FolderIfExists (Join-Path $SourceRoot "SQL")           (Join-Path $RuntimeRoot "SQL")
    Copy-FolderIfExists (Join-Path $SourceRoot "Logs")          (Join-Path $RuntimeRoot "Logs")
    Copy-FolderIfExists (Join-Path $SourceRoot "Backups")       (Join-Path $RuntimeRoot "Backups")
    Copy-FolderIfExists (Join-Path $SourceRoot "Tools")         (Join-Path $RuntimeRoot "Tools")
}

if (!$SkipBepInExImport) {
    $importScript = Join-Path $SourceRoot "Import_BepInEx_To_PatchPayload.ps1"
    if (Test-Path -LiteralPath $importScript) {
        if ([string]::IsNullOrWhiteSpace($BepInExSource)) {
            Write-Host ""
            Write-Host "BepInEx import skipped because -BepInExSource was not provided." -ForegroundColor Yellow
        } else {
            Write-Step "Importing BepInEx payload"
            & $importScript -BepInExSource $BepInExSource -RuntimeRoot $RuntimeRoot
        }
    }
}

Publish-Project (Join-Path $SourceRoot "RepackManager\MMOnsterpatch.RepackManager.csproj") $RuntimeRoot
Publish-Project (Join-Path $SourceRoot "AuthServer\MMOnsterpatch.AuthServer.csproj")       (Join-Path $RuntimeRoot "AuthServer")
Publish-Project (Join-Path $SourceRoot "WorldServer\MMOnsterpatch.WorldServer.csproj")     (Join-Path $RuntimeRoot "WorldServer")
Publish-Project (Join-Path $SourceRoot "AccountWebsite\MMOnsterpatch.AccountWebsite.csproj") (Join-Path $RuntimeRoot "AccountWebsite")

Copy-Item -LiteralPath (Join-Path $SourceRoot 'README.md') -Destination (Join-Path $RuntimeRoot 'README.md') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $SourceRoot 'CHANGELOG.md') -Destination (Join-Path $RuntimeRoot 'CHANGELOG.md') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $SourceRoot 'chatreadme.txt') -Destination (Join-Path $RuntimeRoot 'chatreadme.txt') -Force -ErrorAction SilentlyContinue

$note = @'
MMOnsterpatch Server Release Folder

Run MMOnsterpatchRepackManager.exe from this folder.
This folder is designed to be copied as-is to your server desktop, then moved later to C:\ when stable.
The manager treats this folder as the server root and expects Config, OfficialServer, PatchPayload, Logs, Backups, Tools, Data, and SQL beside it.
'@
Set-Content -LiteralPath (Join-Path $RuntimeRoot 'SERVER_RELEASE_README.txt') -Value $note -Encoding UTF8

Write-Step "Done"
Write-Host "Server release folder:" -ForegroundColor Green
Write-Host $RuntimeRoot -ForegroundColor Green
Write-Host ""
Write-Host "Run:" -ForegroundColor Green
Write-Host (Join-Path $RuntimeRoot "MMOnsterpatchRepackManager.exe") -ForegroundColor Green
