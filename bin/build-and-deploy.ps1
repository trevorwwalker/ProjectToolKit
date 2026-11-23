<#
.SYNOPSIS
  Updates main, then deploys the scheduler as a pool with bin\deploy-scheduler.ps1 (publish beside, swap, roll back).
.DESCRIPTION
  Runs from any folder. The deploy itself (publish into out\windows\scheduler.next while the old one runs,
  restart with workers kept, swap, start from the same path, roll back on a failed start) is
  deploy-scheduler.ps1 (card 967-B); this script brings main up to date first and makes sure a tray is up.
  The scheduler always starts as a pool (every project of the pool file in one process; developer, 2026-11-20).
  -Single keeps the old one-project mode for reference until the pool is stable; it will then be removed.
.PARAMETER PoolFile
  The pool file. Default: %LOCALAPPDATA%\ProjectToolkit\pool.json.
.PARAMETER Single
  Reference only: deploy and start the scheduler for one project (-Project) instead of the pool.
.PARAMETER Project
  With -Single, the project folder the scheduler serves. Default: ProjectToolkit.
.PARAMETER NoPull
  Deploy what is checked out now, without fetching main first.
#>
param(
    [string]$PoolFile = '',
    [switch]$Single,
    [string]$Project,
    [switch]$NoPull
)

$ErrorActionPreference = 'Stop'
$Kit = Split-Path -Parent $PSScriptRoot

if (-not $NoPull) {
    Write-Host "Updating main..."
    git -C $Kit fetch origin
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed" }
    git -C $Kit merge --ff-only origin/main
    if ($LASTEXITCODE -ne 0) { throw "main cannot be fast-forwarded (local commits or changes?) - fix that, or run with -NoPull" }
}

if (-not $Single) {
    $file = if ($PoolFile) { $PoolFile } else { Join-Path $env:LOCALAPPDATA 'ProjectToolkit\pool.json' }
    if (-not (Test-Path $file)) { throw "No pool file at $file - write it first (see docs/design/tdd/scheduler/multi-project.md), or run with -Single" }
}

$deploy = Join-Path $PSScriptRoot 'deploy-scheduler.ps1'
if ($Single) {
    if ($Project) { & $deploy -Kit $Kit -Repo $Project } else { & $deploy -Kit $Kit }
} else {
    & $deploy -Kit $Kit -Pool -PoolFile $PoolFile
}
$code = $LASTEXITCODE
if ($code -ne 0) { exit $code }

# deploy-scheduler.ps1 brings a tray back only if one was running before (975-B). This script always wants
# the tray: if none is up now, start one with the same mode.
# 1014-B: that start counts as by hand, so no -Quiet: the tray opens the page once.
if (-not (Get-Process scheduler-tray -ErrorAction SilentlyContinue)) {
    $tray = Join-Path $Kit 'out\windows\scheduler\scheduler-tray.exe'
    if ($Single) {
        $repo = if ($Project) { $Project } else { $Kit }
        $trayArgs = @("`"$repo`"")
    } elseif ($PoolFile) {
        $trayArgs = @('-Pool', "`"$PoolFile`"")
    } else {
        $trayArgs = @('-Pool')
    }
    Write-Host "Starting the tray: $tray $($trayArgs -join ' ')"
    Start-Process -FilePath $tray -ArgumentList $trayArgs -WorkingDirectory (Split-Path $tray)
}
