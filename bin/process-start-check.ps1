<#
.SYNOPSIS
  Measures how fast this PC starts processes side by side, in starts per second.
.DESCRIPTION
  Runs five rounds. Each round starts 8 separate cmd.exe processes at once, each in a hidden window, and each of
  them starts %WINDIR%\System32\whoami.exe 50 times, one after another. The rate is the 400 starts over the
  round's wall time. Prints the rate of each round and the median.

  The loops MUST be separate processes. .NET (and so PowerShell) starts processes one at a time within one
  process, so loops that are threads or tasks of the measuring process measure that queue (about 50/s on this PC
  whatever its state) and not Windows. The first version of this script (1078-B) did that and read 45-61/s on a
  healthy PC.

  Reference values on this PC (16 cores, 32 threads), measured this way on 2026-11-22:
    healthy, nothing else running            about 470/s (rounds 395-481)
    healthy, 2 workers running               about 260/s
    healthy, 3 workers running               about 100/s
    degraded (before the reboot), idle       20-70/s, with rounds in the teens
  Workers start processes of their own, so measure with the pool idle when the number is to be compared.
.PARAMETER Rounds
  Rounds to run; the median is reported. Default 5.
.PARAMETER Loops
  Processes started side by side per round. Default 8.
.PARAMETER StartsPerLoop
  Starts of whoami.exe per loop. Default 50.
#>
[CmdletBinding()]
param(
    [int]$Rounds = 5,
    [int]$Loops = 8,
    [int]$StartsPerLoop = 50
)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $env:WINDIR 'System32\whoami.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "not found: $exe" }

# One loop, as cmd runs it: no .NET and no PowerShell between one start and the next.
$loop = "for /L %i in (1,1,$StartsPerLoop) do @`"$exe`" >nul 2>nul"

$rates = @()
for ($r = 1; $r -le $Rounds; $r++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $procs = @(1..$Loops | ForEach-Object {
        Start-Process -FilePath $env:ComSpec -ArgumentList '/d', '/c', $loop -WindowStyle Hidden -PassThru
    })
    foreach ($p in $procs) { $p.WaitForExit() }
    $sw.Stop()
    $rate = ($Loops * $StartsPerLoop) / $sw.Elapsed.TotalSeconds
    $rates += $rate
    '{0}: round {1}: {2:N0} starts/s' -f (Get-Date -Format 'HH:mm:ss'), $r, $rate
    Start-Sleep -Seconds 2
}
$sorted = @($rates | Sort-Object)
$median = $sorted[[int][math]::Floor($sorted.Count / 2)]
if ($sorted.Count % 2 -eq 0) { $median = ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2 }
'median: {0:N0} starts/s' -f $median
