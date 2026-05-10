# Firepit dev runner -- kills any running instance, builds, and launches the
# correct exe in one step. Eliminates "which exe is the freshest one" guessing
# by always pointing to src/Firepit/bin/{Config}/Firepit.exe (the path that
# Directory.Build.props guarantees via AppendTargetFrameworkToOutputPath=false).
#
# Usage:
#   ./run.ps1                # Debug build + run (default -- fast iteration)
#   ./run.ps1 -Release       # Release build + run (realistic perf)
#   ./run.ps1 -NoBuild       # Skip build, just launch the existing exe
#   ./run.ps1 -Clean         # Wipe stale TFM/RID output dirs first, then build+run

[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$NoBuild,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$config   = if ($Release) { 'Release' } else { 'Debug' }
$exePath  = Join-Path $repoRoot "src/Firepit/bin/$config/Firepit.exe"

function Write-Status($msg) { Write-Host "[run.ps1] $msg" -ForegroundColor Cyan }

# 1. Kill any running Firepit instance -- releases the file lock on Firepit.exe
#    and the singleton mutex so the new launch becomes the primary instance.
$running = @(Get-Process -Name Firepit -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Status "Killing $($running.Count) running Firepit instance(s)..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 600
}

# 2. Optional: clear stale TFM/RID output dirs left from pre-V1.12 builds where
#    AppendTargetFrameworkToOutputPath was still true. Those stale exes are the
#    classic "which one did I just launch?" trap.
if ($Clean) {
    $staleDirs = @(
        "src/Firepit/bin/Debug/net10.0-windows10.0.17763.0",
        "src/Firepit/bin/Release/net10.0-windows10.0.17763.0",
        "src/Firepit/bin/Debug/win-x64",
        "src/Firepit/bin/Release/win-x64"
    )
    foreach ($d in $staleDirs) {
        $full = Join-Path $repoRoot $d
        if (Test-Path $full) {
            Write-Status "Removing stale $d"
            Remove-Item $full -Recurse -Force
        }
    }
}

# 3. Build (unless explicitly skipped). dotnet build is incremental -- if nothing
#    changed it returns in ~1 s, so we don't gate on `-NoBuild` for speed alone.
if (-not $NoBuild) {
    Write-Status "Building $config..."
    $buildStart = [DateTime]::UtcNow
    & dotnet build (Join-Path $repoRoot 'Firepit.slnx') --configuration $config --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Status "Build FAILED (exit $LASTEXITCODE) -- not launching."
        exit $LASTEXITCODE
    }
    $buildSecs = [Math]::Round(([DateTime]::UtcNow - $buildStart).TotalSeconds, 1)
    Write-Status "Build OK in $buildSecs s"
}

# 4. Launch. Start-Process so this script returns immediately (the user's shell
#    is freed up; the WPF app runs detached).
if (-not (Test-Path $exePath)) {
    Write-Status "EXE not found: $exePath"
    Write-Status "Run without -NoBuild to build it first."
    exit 1
}
$builtAge = [Math]::Round(([DateTime]::UtcNow - (Get-Item $exePath).LastWriteTimeUtc).TotalSeconds, 0)
Write-Status "Launching src/Firepit/bin/$config/Firepit.exe (built $builtAge s ago)"
Start-Process $exePath
