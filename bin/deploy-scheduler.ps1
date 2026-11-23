<#
.SYNOPSIS
    Publishes a new scheduler beside the one that is running and swaps it in (card 967-B).

.DESCRIPTION
    The tray's "Update and restart" menu item does this from the icon; this is the same deploy for the
    command line, for when the tray is not running.

    The new build is published into out\windows\scheduler.next while out\windows\scheduler keeps running,
    so no publish ever fights a locked file. The running scheduler is then ended on its own (its workers
    are left running and the new scheduler adopts them), the build it replaced is kept in
    out\windows\scheduler.prev, and the new one is started from the SAME path - so the page's inbound
    firewall rule, which is bound to out\windows\scheduler\scheduler-next.exe, still matches (BUILD-3).

    A deploy leaves what it found (975-B): when a tray was running over this scheduler, the tray is ended
    after the scheduler and started again from the new build (it moves to its own copy and starts the new
    scheduler as its child), so the icon, its log window and Exit are still there; the console says "tray
    restarted with the new scheduler". With no tray before, the new scheduler is started hidden, as before.

    If the new build does not stay up for the start window, the build in scheduler.prev is swapped back and
    started, and this script says so and exits 1.

    The version that replaced which is written to the console here and into the scheduler's own log: the new
    scheduler is started with -Replaces:<the version it took over from>.

.PARAMETER Kit
    The kit's root, the folder holding code\ and out\. Default: the folder above this script.

.PARAMETER Repo
    The repository to start the new scheduler for. Default: the repository the running scheduler was
    started for, read back from its command line.

.PARAMETER Pool
    Start the new scheduler (and its tray) as a pool: every project of the pool file in one process (multi-project
    design MP-C9). Without it the scheduler is started as it was found, so a deploy over a running pool stays a pool.
    With it the pool is always started through its tray (1013-B), so there is one scheduler: a tray started
    afterwards attaches to the running pool instead of starting a second one.

.PARAMETER PoolFile
    With -Pool, the pool file; default %LOCALAPPDATA%ProjectToolkitpool.json (MP-C1).

.PARAMETER UsePublished
    Deploy the out\windows\scheduler.next that is already there instead of publishing a new one.

.PARAMETER StartWindowSeconds
    How long the new scheduler is given to stay up before it is swapped back. Default 30 (967-B).

.EXAMPLE
    bin\deploy-scheduler.ps1

.EXAMPLE
    bin\deploy-scheduler.ps1 -Repo C:\Projects\Game -UsePublished
#>
[CmdletBinding()]
param(
    [string]$Kit = (Split-Path -Parent $PSScriptRoot),
    [string]$Repo = '',
    [switch]$Pool,
    [string]$PoolFile = '',
    [switch]$UsePublished,
    [int]$StartWindowSeconds = 30
)

$ErrorActionPreference = 'Stop'

$Out = Join-Path $Kit 'out\windows'
$Current = Join-Path $Out 'scheduler'
$Next = Join-Path $Out 'scheduler.next'
$Prev = Join-Path $Out 'scheduler.prev'
$Exe = Join-Path $Current 'scheduler-next.exe'
$Project = Join-Path $Kit 'code\src\Scheduler.Host'

function Get-BuildVersion([string]$Path) {
    if (-not (Test-Path $Path)) { return 'an unknown version' }
    $info = (Get-Item $Path).VersionInfo
    $text = if ($info.ProductVersion) { $info.ProductVersion } else { $info.FileVersion }
    if (-not $text) { return 'an unknown version' }
    $text = ($text -split '\+')[0].Trim()
    $parts = $text -split '\.'
    if ($parts.Count -eq 4 -and $parts[3] -eq '0') { $text = ($parts[0..2] -join '.') }
    return $text
}

function Stop-RunningScheduler {
    # End the scheduler alone. Its workers are in job objects of their own that this does not end, so the
    # new scheduler adopts them, as after the tray's Restart (TRAY-5).
    $found = @(Get-CimInstance Win32_Process -Filter "Name='scheduler-next.exe'" |
            Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $Exe) })
    if ($found.Count -eq 0) { return $null }
    $running = $found[0]
    Write-Host "  ending the scheduler (pid $($running.ProcessId)); its workers keep running"
    Stop-Process -Id $running.ProcessId -Force
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $running.ProcessId -ErrorAction SilentlyContinue)) { return $running }
        Start-Sleep -Milliseconds 250
    }
    throw "the scheduler (pid $($running.ProcessId)) would not end; nothing was swapped"
}

function Get-TrayLockedNames {
    # The tray's own files, when a tray is running from the published folder: Windows will not let them go,
    # so the swap leaves them where they are. A tray started from its own copy in
    # %LOCALAPPDATA%\ProjectToolkit\tray locks nothing here (967-B).
    $trays = @(Get-CimInstance Win32_Process -Filter "Name='scheduler-tray.exe'" |
            Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Current, [StringComparison]::OrdinalIgnoreCase) })
    if ($trays.Count -eq 0) { return @() }
    return @('scheduler-tray.exe', 'scheduler-tray.dll', 'scheduler-tray.deps.json', 'scheduler-tray.runtimeconfig.json', 'Scheduler.Tray.dll')
}

function Split-CommandLine([string]$Line) {
    return @([regex]::Matches($Line, '"([^"]*)"|(\S+)') | ForEach-Object { if ($_.Groups[1].Success) { $_.Groups[1].Value } else { $_.Groups[2].Value } })
}

function Get-TraysForScheduler {
    # 975-B: the trays over this folder's scheduler - run from it, or from their own copy in
    # %LOCALAPPDATA%\ProjectToolkit\tray and told where it is with -SchedulerDir. A deploy that finds one
    # brings one back, so the icon, its log window and Exit are still there afterwards.
    return @(Get-CimInstance Win32_Process -Filter "Name='scheduler-tray.exe'" | Where-Object {
            ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($Current, [StringComparison]::OrdinalIgnoreCase)) -or
            ($_.CommandLine -and $_.CommandLine.IndexOf($Current, [StringComparison]::OrdinalIgnoreCase) -ge 0) })
}

function Stop-Trays($Trays) {
    # The tray is ended after its scheduler, so the scheduler's arguments are read first; its workers
    # are not the tray's to end either (TRAY-5).
    foreach ($tray in $Trays) {
        Write-Host "  ending the tray (pid $($tray.ProcessId)); a tray is started again with the new scheduler"
        Stop-Process -Id $tray.ProcessId -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $tray.ProcessId -Timeout 15 -ErrorAction SilentlyContinue
    }
}

function Get-TrayArguments($Tray, [string[]]$Fallback) {
    # The tray's own arguments (the repository and the scheduler's), without its exe and the folder it was
    # told, which the tray started from this folder works out again.
    $trayArgs = @(Split-CommandLine $Tray.CommandLine | Select-Object -Skip 1 | Where-Object { $_ -notmatch '(?i)^-SchedulerDir[:=]' })
    if ($trayArgs.Count -eq 0) { $trayArgs = @($Fallback | Where-Object { $_ -notmatch '(?i)^-(ShutdownEvent|Replaces)[:=]' }) }
    return $trayArgs
}

function Get-PoolArguments {
    # MP-C9: -Pool [<file>] starts every project of the pool file in the one scheduler (the tray takes the same).
    if ($PoolFile) { return @('-Pool', $PoolFile) }
    return @('-Pool')
}

function Start-TrayFrom([string[]]$Arguments, [switch]$Quiet) {
    # 1014-B: a tray this script brings back after a deploy is an automatic start: -Quiet, so it opens no page.
    if ($Quiet -and -not (@($Arguments) -match '(?i)^-Quiet$')) { $Arguments = @($Arguments) + '-Quiet' }
    # The tray in the new build copies itself to %LOCALAPPDATA%\ProjectToolkit\tray, starts from there
    # (967-B) and starts the new scheduler as its child.
    $trayExe = Join-Path $Current 'scheduler-tray.exe'
    if (-not (Test-Path $trayExe)) { throw "there is no scheduler-tray.exe in $Current" }
    if ($Arguments.Count -eq 0) { throw 'nothing to start: no repository for the tray (pass -Repo)' }
    Start-Process -FilePath $trayExe -ArgumentList $Arguments | Out-Null
}

function Wait-SchedulerStarted([int]$Seconds) {
    # The scheduler a tray starts is the tray's child, not this script's: it is found by its path.
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $found = @(Get-CimInstance Win32_Process -Filter "Name='scheduler-next.exe'" |
                Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $Exe) })
        if ($found.Count -gt 0) { return (Get-Process -Id $found[0].ProcessId -ErrorAction SilentlyContinue) }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Move-Entries([string]$From, [string]$To, [string[]]$Keep) {
    foreach ($entry in Get-ChildItem $From -Force) {
        if ($Keep -contains $entry.Name) { continue }
        $target = Join-Path $To $entry.Name
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Move-Item $entry.FullName $target
    }
}

function Start-SchedulerFrom([string[]]$Arguments, [string]$Replaces) {
    $args = @($Arguments)
    if ($Replaces) { $args += "-Replaces:$Replaces" }
    # -ShutdownEvent names an event the process that set it owns; a new scheduler must not be handed a
    # stale one, and it is the reason the recorded command line cannot simply be replayed.
    $args = @($args | Where-Object { $_ -notmatch '(?i)^-ShutdownEvent[:=]' })
    if ($args.Count -eq 0) { throw 'nothing to start: no repository for the new scheduler (pass -Repo)' }
    return (Start-Process -FilePath $Exe -ArgumentList $args -WindowStyle Hidden -PassThru)
}

function Test-StaysUp($Process, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) { return $false }
        Start-Sleep -Milliseconds 500
    }
    return -not $Process.HasExited
}

function Restore-OldBuild {
    # The build it replaced, back in place and started the way it was found: by a tray when there was one
    # (975-B), whose own copy is refreshed from the old build, so any tray the new build started is ended first.
    $newTrays = @(Get-TraysForScheduler)
    if ($newTrays.Count -gt 0) { Stop-Trays $newTrays }
    $leftover = @(Get-CimInstance Win32_Process -Filter "Name='scheduler-next.exe'" | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $Exe) })
    foreach ($p in $leftover) {
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $p.ProcessId -Timeout 15 -ErrorAction SilentlyContinue
    }
    Move-Entries $Prev $Current $keep
    if ($trays.Count -gt 0) {
        Start-TrayFrom $trayArgs -Quiet
        Write-Host "  tray restarted with the old scheduler"
    }
    else {
        Start-SchedulerFrom $savedArgs '' | Out-Null
    }
}

if (-not (Test-Path $Current)) { throw "$Current is not there; publish the scheduler once (see bin\projecttoolkit.ps1)" }

$old = Get-BuildVersion $Exe
Write-Host "scheduler: $Current is $old"

# Publish beside the running one, or take the scheduler.next that is already there (967-B).
if ($UsePublished) {
    if (-not (Test-Path (Join-Path $Next 'scheduler-next.exe'))) { throw "nothing to deploy: $Next has no scheduler-next.exe in it" }
    Write-Host "  using the build already published in $Next"
}
else {
    if (-not (Test-Path (Join-Path $Project ((Split-Path -Leaf $Project) + '.csproj')))) { throw "there is no project to publish at $Project" }
    if (Test-Path $Next) { Remove-Item $Next -Recurse -Force }
    Write-Host "  publishing $Project into $Next; the running scheduler is left alone"
    dotnet publish $Project -c Release -o $Next -v quiet --nologo
    if ($LASTEXITCODE -ne 0) { throw "the publish did not finish (exit $LASTEXITCODE); the running scheduler was not touched" }
}

$new = Get-BuildVersion (Join-Path $Next 'scheduler-next.exe')

# The arguments the new scheduler is started with: the ones it was started with, or -Repo.
$trays = @(Get-TraysForScheduler)
$saved = Stop-RunningScheduler
$savedArgs = @()
if ($saved) { $savedArgs = @(Split-CommandLine $saved.CommandLine) }
if ($savedArgs.Count -gt 0) { $savedArgs = @($savedArgs | Select-Object -Skip 1) }   # the exe itself
if ($Pool) { $savedArgs = @(Get-PoolArguments) }
elseif ($Repo) { $savedArgs = @($Repo) }
$trayArgs = @()
if ($trays.Count -gt 0) {
    $trayArgs = if ($Pool) { @(Get-PoolArguments) } elseif ($Repo) { @($Repo) } else { @(Get-TrayArguments $trays[0] $savedArgs) }
    Stop-Trays $trays
}

# Swap: the running build aside into scheduler.prev, the published build into its place.
if (Test-Path $Prev) { Remove-Item $Prev -Recurse -Force }
New-Item -ItemType Directory -Path $Prev | Out-Null
$keep = @(Get-TrayLockedNames)
if ($keep.Count -gt 0) { Write-Host "  a tray is running from $Current; leaving $($keep -join ', ') where it is" }
try {
    Move-Entries $Current $Prev $keep
    Move-Entries $Next $Current $keep
    Remove-Item $Next -Recurse -Force
}
catch {
    Write-Host "  the swap went wrong ($($_.Exception.Message)); putting the old build back"
    Restore-OldBuild
    Write-Host "scheduler: $old is running again from $Current"
    throw "the deploy failed; $old is still running"
}

# Start the new one from the same path, and put the old build back if it does not stay up.
Write-Host "  starting the new scheduler from $Exe"
try {
    if ($trays.Count -gt 0) {
        # 975-B: a deploy leaves what it found - with a tray before, the new scheduler is a tray's child.
        Start-TrayFrom $trayArgs -Quiet
        $started = Wait-SchedulerStarted $StartWindowSeconds
        if (-not $started) { throw "the tray did not start the scheduler within $StartWindowSeconds s" }
        Write-Host "  tray restarted with the new scheduler (pid $($started.Id))"
    }
    elseif ($Pool) {
        # 1013-B: -Pool starts the pool through its tray, which starts the one scheduler; starting it here as well
        # made two (a tray started afterwards started its own). A tray started later attaches to this one.
        Start-TrayFrom @(Get-PoolArguments)
        $started = Wait-SchedulerStarted $StartWindowSeconds
        if (-not $started) { throw "the tray did not start the pool within $StartWindowSeconds s" }
        Write-Host "  tray started with the new pool scheduler (pid $($started.Id))"
    }
    else {
        $started = Start-SchedulerFrom $savedArgs $old
    }
}
catch {
    Write-Host "  the new scheduler would not start ($($_.Exception.Message)); putting the old build back"
    Restore-OldBuild
    Write-Host "scheduler: $old is running again from $Current"
    exit 1
}

if (-not (Test-StaysUp $started $StartWindowSeconds)) {
    Write-Host "  the new scheduler $new did not stay up for $StartWindowSeconds s; putting the old build back"
    if (-not $started.HasExited) { Stop-Process -Id $started.Id -Force }
    Restore-OldBuild
    Write-Host "scheduler: $old is running again from $Current"
    exit 1
}

if (Test-Path $Prev) {
    Write-Host "  the build it replaced is kept in $Prev"
}

Write-Host "scheduler: $new replaced $old in $Current (pid $($started.Id))"
exit 0
