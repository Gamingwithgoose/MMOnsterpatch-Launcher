param(
    [string]$RuntimeRoot = "",
    [switch]$NoPrompt
)

$ErrorActionPreference = "Stop"
$SourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$WorkspaceRoot = Split-Path -Parent $SourceRoot
if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
    $RuntimeRoot = Join-Path $WorkspaceRoot "Releases\Server"
}

if ((Test-Path -LiteralPath $RuntimeRoot) -and !$NoPrompt) {
    Write-Host ""
    Write-Host "This will delete and rebuild the server release folder:" -ForegroundColor Yellow
    Write-Host $RuntimeRoot -ForegroundColor Yellow
    Write-Host ""
    $answer = Read-Host "Type YES to continue"
    if ($answer -ne "YES") {
        Write-Host "Cancelled."
        exit 0
    }
}

& "$PSScriptRoot\Publish_All_SingleExe.ps1" -RuntimeRoot $RuntimeRoot
