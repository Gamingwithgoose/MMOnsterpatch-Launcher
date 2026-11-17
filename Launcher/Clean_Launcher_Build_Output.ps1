$ErrorActionPreference = "Stop"
$SourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Get-ChildItem -LiteralPath $SourceRoot -Recurse -Directory | Where-Object { $_.Name -in @('bin','obj','.vs') } | ForEach-Object {
    Write-Host "Removing $($_.FullName)"
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}
Write-Host "Clean complete." -ForegroundColor Green
