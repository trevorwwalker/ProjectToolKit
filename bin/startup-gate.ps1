<#
.SYNOPSIS
    The startup-time gate (1032-B): a pool start may not get clearly slower.

.DESCRIPTION
    Builds test\perf\StartupBench, which writes a pool of generated projects to a temp folder (real git
    repositories, 3 x 300 cards and one project of 2,000, done/ included), starts the built scheduler on it three
    times on a free port with its own pool name, temp log and workspace folders (the live pool, its pool.json,
    ports and logs are not touched) and prints the median time to (a) the page answering and (b) every project
    joined with its first snapshot published (the "pool: start took" line, 1015-B).

    The gate fails when a median exceeds test\perf\startup-baseline.json by more than 50% AND by more than 2 s
    (both, so noise on a fast start does not fail it). A failure is measured once more before it counts (load on
    the PC); the output names both times. -Record writes the baseline from this PC's medians: a deliberate step,
    done in the card that makes startup faster or accepts a slower start, and logged in that card's Result.

.PARAMETER Record
    Write test\perf\startup-baseline.json from this run instead of comparing.

.PARAMETER Exe
    The scheduler to time. Default: the Debug build's build\bin\Scheduler.Host\debug\scheduler-next.exe.

.PARAMETER Runs
    Starts per measurement; the median is taken. Default 3.

.PARAMETER Quick
    A small pool (for trying the script itself); never compared with or recorded as the baseline.

.OUTPUTS
    Exit code 0 passed (or recorded), 1 too slow, 2 the benchmark could not run.
#>
[CmdletBinding()]
param(
    [switch]$Record,
    [string]$Exe = '',
    [int]$Runs = 3,
    [switch]$Quick
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$baselineFile = Join-Path $repo 'test\perf\startup-baseline.json'
$project = Join-Path $repo 'test\perf\StartupBench\StartupBench.csproj'
if ($Exe -eq '') { $Exe = Join-Path $repo 'build\bin\Scheduler.Host\debug\scheduler-next.exe' }
if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Host "startup gate: $Exe not found: build the solution first (dotnet build code\src\SchedulerNext.slnx)."
    exit 2
}

$jobs = if ($env:PTK_JOBS) { $env:PTK_JOBS } else { '5' }
$out = & dotnet build $project -warnaserror "-m:$jobs" -v:q -nologo 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    Write-Host "startup gate: the benchmark did not build:"
    ($out -split "`n" | Select-Object -Last 15) -join "`n" | Write-Host
    exit 2
}
$bench = Join-Path $repo 'build\bin\StartupBench\debug\StartupBench.dll'

function Measure-Start {
    $result = Join-Path ([IO.Path]::GetTempPath()) ('ptk-startup-result-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.json')
    $argList = @($bench, '--exe', $Exe, '--runs', $Runs, '--out', $result)
    if ($Quick) { $argList += @('--projects', 1, '--cards', 30, '--big', 100) }
    $err = $result + '.err'
    # Start-Process: Windows PowerShell 5.1 turns a native program's redirected stderr into error records.
    $quoted = ($argList | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList $quoted -NoNewWindow -Wait -PassThru -RedirectStandardError $err -RedirectStandardOutput ($result + '.out')
    $code = $proc.ExitCode
    Remove-Item -LiteralPath ($result + '.out') -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $err) {
        Get-Content -Encoding UTF8 -LiteralPath $err | Where-Object { $_ -notmatch '^bench: git ' } | ForEach-Object { Write-Host "  $_" }
        Remove-Item -LiteralPath $err -Force -ErrorAction SilentlyContinue
    }
    if ($code -ne 0 -or -not (Test-Path -LiteralPath $result)) { return $null }
    try { return Get-Content -Raw -Encoding UTF8 -LiteralPath $result | ConvertFrom-Json }
    finally { Remove-Item -LiteralPath $result -Force -ErrorAction SilentlyContinue }
}

# Over the limit: more than 50% above the baseline and more than 2 s above it.
function Test-Slow([double]$now, [double]$base) { return ($now -gt $base * 1.5) -and ($now -gt $base + 2.0) }

$m = Measure-Start
if ($null -eq $m) { Write-Host 'startup gate: the benchmark did not produce a result.'; exit 2 }
Write-Host ("startup gate: medians of {0}: page {1:0.00} s, every project joined {2:0.00} s" -f $m.runs, $m.pageSeconds, $m.startSeconds)

if ($Record) {
    if ($Quick) { Write-Host 'startup gate: -Record is not allowed with -Quick.'; exit 2 }
    $json = [ordered]@{
        about       = 'Median pool start times on this PC (bin\startup-gate.ps1 -Record). The gate fails when a time is over this by more than 50% and by more than 2 s.'
        recorded    = (Get-Date -Format 'yyyy-MM-dd HH:mm')
        version     = (& $Exe --version)
        pageSeconds = [double]$m.pageSeconds
        startSeconds = [double]$m.startSeconds
    } | ConvertTo-Json
    Set-Content -Encoding UTF8 -LiteralPath $baselineFile -Value $json
    Write-Host "startup gate: baseline recorded in $baselineFile"
    exit 0
}

if ($Quick) { Write-Host 'startup gate: quick run, nothing compared.'; exit 0 }
if (-not (Test-Path -LiteralPath $baselineFile)) {
    Write-Host "startup gate: no baseline ($baselineFile): record one with bin\startup-gate.ps1 -Record."
    exit 2
}
$base = Get-Content -Raw -Encoding UTF8 -LiteralPath $baselineFile | ConvertFrom-Json

function Test-Gate($m) {
    $bad = @()
    if (Test-Slow $m.pageSeconds $base.pageSeconds) { $bad += ("page answers in {0:0.00} s, baseline {1:0.00} s" -f $m.pageSeconds, $base.pageSeconds) }
    if (Test-Slow $m.startSeconds $base.startSeconds) { $bad += ("every project joined in {0:0.00} s, baseline {1:0.00} s" -f $m.startSeconds, $base.startSeconds) }
    return $bad
}

$bad = Test-Gate $m
if ($bad.Count -gt 0) {
    Write-Host ('startup gate: too slow once (' + ($bad -join '; ') + '); measuring again in case the PC was busy.')
    $m = Measure-Start
    if ($null -eq $m) { Write-Host 'startup gate: the benchmark did not produce a result.'; exit 2 }
    Write-Host ("startup gate: second measurement: page {0:0.00} s, every project joined {1:0.00} s" -f $m.pageSeconds, $m.startSeconds)
    $bad = Test-Gate $m
}

if ($bad.Count -gt 0) {
    Write-Host ('startup gate FAILED: ' + ($bad -join '; ') + ' (limit: over the baseline by more than 50% and more than 2 s).')
    Write-Host 'If the slower start is intended, re-record with bin\startup-gate.ps1 -Record in the card that accepts it and log it in its Result.'
    exit 1
}
Write-Host ("startup gate passed: page {0:0.00} s (baseline {1:0.00}), every project joined {2:0.00} s (baseline {3:0.00})" -f $m.pageSeconds, $base.pageSeconds, $m.startSeconds, $base.startSeconds)
exit 0
