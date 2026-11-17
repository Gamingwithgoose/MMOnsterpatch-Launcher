param(
    [string]$OutputRoot = "C:\[MMOnsterpatch] UnOfficial Launcher Releases\Release"
)

$ErrorActionPreference = "Stop"
$SourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $SourceRoot "MMOnsterpatchLauncher.Wpf\MMOnsterpatchLauncher.Wpf.csproj"

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
