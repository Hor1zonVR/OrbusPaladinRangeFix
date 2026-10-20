# Print the most recent read-only MechanicsDiagnostics report and related loader messages.
param([string]$GameDir = '')

$root = Join-Path $env:LOCALAPPDATA 'OrbusRebornModdingInstances'
$cache = Join-Path $root '.last-build-references.txt'
if (-not $GameDir -and (Test-Path $cache)) { $GameDir = (Get-Content $cache -Raw).Trim() }
if (-not $GameDir -and (Test-Path $root)) {
    foreach ($candidate in @(Get-ChildItem $root -Directory | Sort-Object Name)) {
        if (Test-Path (Join-Path $candidate.FullName 'BepInEx\MechanicsDiagnostics')) {
            $GameDir = $candidate.FullName; break
        }
    }
}
if (-not $GameDir) { throw 'Specify -GameDir with your RebornManager instance folder.' }
$folder = Join-Path $GameDir 'BepInEx\MechanicsDiagnostics'
if (-not (Test-Path $folder)) { throw "No diagnostic folder found in $GameDir. Confirm the mod was installed and the game launched." }
$latest = Get-ChildItem $folder -Filter 'Mechanics_*.txt' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $latest) { throw "No recording files found in $folder" }
Write-Host "Latest report: $($latest.FullName)" -ForegroundColor Cyan
Write-Host ''
Get-Content -LiteralPath $latest.FullName
Write-Host ''
Write-Host 'Paste or upload the report above (and LogOutput.log if there are errors).' -ForegroundColor Yellow
