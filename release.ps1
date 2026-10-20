# Builds a diagnostic DLL and publishes it to GitHub Releases for RebornManager.
# It NEVER installs into any game folder. Requires dotnet, git and authenticated gh.
param([string]$GameDir = '', [switch]$Preview)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repo = 'Hor1zonVR/OrbusPaladinRangeFix'
$project = Join-Path $root 'src\OrbusPaladinRangeFix\OrbusPaladinRangeFix.csproj'
$plugin = Join-Path $root 'src\OrbusPaladinRangeFix\Plugin.cs'
$dll = Join-Path $root 'src\OrbusPaladinRangeFix\bin\Release\net6.0\OrbusPaladinRangeFix.dll'
$instanceRoot = Join-Path $env:LOCALAPPDATA 'OrbusRebornModdingInstances'
$cache = Join-Path $instanceRoot '.last-build-references.txt'
$required = @(
    'BepInEx\core\BepInEx.Core.dll',
    'BepInEx\core\BepInEx.Unity.IL2CPP.dll',
    'BepInEx\core\Il2CppInterop.Runtime.dll',
    'BepInEx\interop\Il2Cppmscorlib.dll',
    'BepInEx\interop\UnityEngine.CoreModule.dll',
    'BepInEx\interop\UnityEngine.SceneManagementModule.dll'
)
function Assert-OK([string]$what) {
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}
function Valid([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    foreach ($file in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $path $file) -PathType Leaf)) { return $false }
    }
    return $true
}
Set-Location $root
foreach ($cmd in @('git','dotnet','gh')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "Missing command: $cmd" }
}
& gh auth status
Assert-OK 'GitHub login'
$origin = (& git remote get-url origin).Trim()
Assert-OK 'Read git origin'
if ($origin -notmatch '[/\\:]Hor1zonVR/OrbusPaladinRangeFix(\.git)?$') {
    throw "Wrong repository origin: $origin"
}
if ((& git branch --show-current).Trim() -ne 'main') { throw 'Switch to main.' }
if (@(& git status --porcelain).Count -gt 0) { throw 'Local changes detected; commit or stash before release.' }
& git pull --ff-only origin main
Assert-OK 'Pull latest source'

if (-not (Valid $GameDir) -and (Test-Path -LiteralPath $cache)) {
    $saved = (Get-Content -LiteralPath $cache -Raw).Trim()
    if (Valid $saved) { $GameDir = $saved }
}
if (-not (Valid $GameDir) -and (Test-Path -LiteralPath $instanceRoot)) {
    foreach ($candidate in @(Get-ChildItem -LiteralPath $instanceRoot -Directory | Sort-Object Name)) {
        if (Valid $candidate.FullName) { $GameDir = $candidate.FullName; break }
    }
}
if (-not (Valid $GameDir)) {
    throw "Game interop not found. Re-run with -GameDir 'C:\full\path\to\your\modding\instance'."
}
Write-Host "Compiling against $GameDir" -ForegroundColor Cyan
$versionMatch = [regex]::Match((Get-Content $plugin -Raw),
    '\[BepInPlugin\("[^"]+",\s*"[^"]+",\s*"(?<version>\d+\.\d+\.\d+)"\)\]')
if (-not $versionMatch.Success) { throw 'Could not detect plugin version.' }
$tag = 'v' + $versionMatch.Groups['version'].Value
$existing = @(& git ls-remote --tags --refs origin "refs/tags/$tag")
Assert-OK 'Check release tag'
if ($existing.Count -gt 0) { throw "GitHub already has $tag. Bump Plugin.cs version first." }

& dotnet build $project -c Release --no-incremental "-p:GameDir=$GameDir"
Assert-OK 'Build'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Compiled DLL missing.' }
$sha = (& git rev-parse HEAD).Trim()
Assert-OK 'Read commit'
$args = @('release','create',$tag,$dll,'--repo',$repo,'--target',$sha,
    '--title',"OrbusPaladinRangeFix $tag (Diagnostics)",
    '--notes','READ-ONLY Paladin hammer teleport and AutoMount discovery build. F8/F9 recording, F10 snapshot, F11 marker. No teleport changes or auto mounting yet.')
if ($Preview) { $args += '--prerelease' }
& gh @args
Assert-OK 'Publish GitHub release'
Write-Host "Published $tag. Use RebornManager to install the Client Mod." -ForegroundColor Green
