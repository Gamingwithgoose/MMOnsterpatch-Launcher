$ErrorActionPreference = 'Stop'
$launcherRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $launcherRoot
$built = Join-Path $repoRoot 'Client\MMOnsterpatchOfficialServerPatcher\bin\Release\net472\MMOnsterpatchOfficialServerPatcher.dll'
$pdb = Join-Path $repoRoot 'Client\MMOnsterpatchOfficialServerPatcher\bin\Release\net472\MMOnsterpatchOfficialServerPatcher.pdb'
if (!(Test-Path $built)) { throw "Built patcher not found: $built" }
foreach ($rid in @('win-x64','linux-x64')) {
  $dest = Join-Path $launcherRoot "Payloads\$rid\BepInEx\patchers"
  New-Item -ItemType Directory -Force -Path $dest | Out-Null
  Copy-Item -LiteralPath $built -Destination (Join-Path $dest 'MMOnsterpatchOfficialServerPatcher.dll') -Force
  if (Test-Path $pdb) { Copy-Item -LiteralPath $pdb -Destination (Join-Path $dest 'MMOnsterpatchOfficialServerPatcher.pdb') -Force }
  Write-Host "Copied patcher to $dest" -ForegroundColor Green
}
