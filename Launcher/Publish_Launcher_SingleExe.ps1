param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\Releases\Launcher")
)

$ErrorActionPreference = "Stop"
$SourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $SourceRoot "MMOnsterpatchLauncher.Wpf\MMOnsterpatchLauncher.Wpf.csproj"

# Build the client first: its MSBuild target refreshes the embedded WPF payload.
$ClientProject = Join-Path $SourceRoot "..\Client\MMOnsterpatchOfficialServerPatcher\MMOnsterpatchAIOPatcher.csproj"
$ReferenceAssembly = Join-Path $SourceRoot "..\Client\lib\Assembly-CSharp.dll"
if (!(Test-Path -LiteralPath $ReferenceAssembly) -and [string]::IsNullOrWhiteSpace($env:MONSTERPATCH_LIB_DIR)) {
    & (Join-Path $SourceRoot "..\Client\prepare_lib_from_game.bat")
    if ($LASTEXITCODE -ne 0) { throw "Client reference preparation failed." }
}
dotnet build $ClientProject -c Release
if ($LASTEXITCODE -ne 0) { throw "Client build failed; launcher was not published." }

[System.IO.Directory]::CreateDirectory($OutputRoot) | Out-Null

Write-Host "Publishing MMOnsterpatch WPF Launcher" -ForegroundColor Cyan
Write-Host "Project: $Project"
Write-Host "Output : $OutputRoot"

dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $OutputRoot

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "Done." -ForegroundColor Green
Write-Host (Join-Path $OutputRoot "MMOnsterpatchLauncher.exe") -ForegroundColor Green
