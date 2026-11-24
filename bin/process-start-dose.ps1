<#
.SYNOPSIS
  Which kind of work lowers this PC's process-start rate? Gives the PC measured doses of work and reads the rate
  after each one (card 1158-B).
.DESCRIPTION
  Run from a healthy start (after a reboot, pool and scheduler idle, no other worker running). Each step is one
  -Step value, run in this order; every step ends with a READING and appends it as one JSON line to
  <OutDir>\dose.jsonl, so a step can be run on its own and the experiment survives an interrupted session.

    baseline   reading only
    idle10     wait 10 minutes, then read (does the rate move by itself?)
    whoami     5,200 starts of %WINDIR%\System32\whoami.exe (13 rounds of 8 cmd loops x 50), then read
    git        5,200 starts of C:\Program Files\Git\mingw64\bin\git.exe --version, the same way, then read
    build      dotnet build code\src\SchedulerNext.slnx -warnaserror -m:$PTK_JOBS --no-incremental, then read
    test1..3   dotnet test code\src\SchedulerNext.slnx --no-build -m:$PTK_JOBS (run time recorded), then read
    idle15     wait 15 minutes, then read (does it recover when left alone?)
    fsmon      read; end only the git.exe processes whose command line holds fsmonitor--daemon; read again
    report     print the recorded readings as markdown tables (no measuring)

  A READING is three runs of bin\process-start-check.ps1 a minute apart (start to start), five rounds each, called
  one round at a time so that a round under 100 starts/s is followed at once by a sample of
  '\Process(*)\% Processor Time' (2 s x 3, the ten busiest processes, idle and _total left out). Only a change in
  all three medians counts: single runs differ by a third with nothing changed.
  With each reading it records, read-only: MsMpEng, System, csrss, services, lsass and the five largest svchost
  (working set, CPU seconds, handles); \Memory\Pool Nonpaged Bytes, Pool Paged Bytes, Cache Bytes, Available MBytes
  and \Objects\Processes, Sections, Events, Threads; running processes and the kernel's count minus it (ended but
  still held); and how many git.exe run and how many of them are fsmonitor--daemon.

  Changes no product code and no machine setting. The only process it ends is a git fsmonitor--daemon, in step
  fsmon (git starts a new one the next time it runs in that worktree). Run steps one after another with
  -Step <name>; each call prints a short summary.
.PARAMETER Step
  One of the step names above.
.PARAMETER OutDir
  Where dose.jsonl is written. Default: out\process-start-dose under the repository.
.PARAMETER Repo
  The worktree the build and test steps run in. Default: the repository this script is in.
.PARAMETER IdleMinutes
  Overrides the wait of idle10 / idle15 (for trying the script out).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('baseline', 'idle10', 'whoami', 'git', 'build', 'test1', 'test2', 'test3', 'idle15', 'fsmon', 'report')]
    [string]$Step,
    [string]$OutDir,
    [string]$Repo,
    [int]$IdleMinutes = 0
)
$ErrorActionPreference = 'Stop'
if (-not $Repo) { $Repo = Split-Path -Parent $PSScriptRoot }
if (-not $OutDir) { $OutDir = Join-Path $Repo 'out\process-start-dose' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$jsonl = Join-Path $OutDir 'dose.jsonl'
$check = Join-Path $PSScriptRoot 'process-start-check.ps1'
$jobs = if ($env:PTK_JOBS) { $env:PTK_JOBS } else { '6' }

function Get-Median([double[]]$v) {
    $s = @($v | Sort-Object)
    if ($s.Count -eq 0) { return 0 }
    if ($s.Count % 2 -eq 0) { return ($s[$s.Count / 2 - 1] + $s[$s.Count / 2]) / 2 }
    return $s[[int][math]::Floor($s.Count / 2)]
}

# The ten busiest processes over 3 samples of 2 s (idle and _total left out).
function Get-Busiest {
    # A process that ends during a sample makes Get-Counter throw; try again, then give up (a reading must not die of it).
    $c = $null
    for ($try = 0; $try -lt 3 -and -not $c; $try++) {
        try { $c = Get-Counter '\Process(*)\% Processor Time' -SampleInterval 2 -MaxSamples 3 -ErrorAction Stop } catch { $c = $null }
    }
    if (-not $c) { return 'sample failed (counter error 3 times)' }
    $sum = @{}
    foreach ($set in $c) {
        foreach ($s in $set.CounterSamples) {
            if ($s.InstanceName -in 'idle', '_total') { continue }
            $sum[$s.InstanceName] = [double]$sum[$s.InstanceName] + $s.CookedValue
        }
    }
    @($sum.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 10 |
        ForEach-Object { '{0} {1:N0}%' -f $_.Key, ($_.Value / 3) }) -join '; '
}

# Everything the card wants recorded with a reading.
function Get-Figures {
    $f = [ordered]@{}
    $all = @(Get-Process)
    # Get-Process gives no CPU time for protected processes (MsMpEng, System) when not elevated; WMI does.
    $cpu = @{}
    foreach ($w in @(Get-CimInstance Win32_Process)) { $cpu[[int]$w.ProcessId] = ([double]$w.KernelModeTime + [double]$w.UserModeTime) / 1e7 }
    foreach ($n in 'MsMpEng', 'System', 'csrss', 'services', 'lsass') {
        $i = 0
        foreach ($p in @($all | Where-Object { $_.ProcessName -eq $n })) {
            $k = if ($i++ -eq 0) { $n } else { "$n#$i" }
            $f[$k] = '{0:N0}MB/{1:N0}s/{2}h' -f ($p.WorkingSet64 / 1MB), $cpu[[int]$p.Id], $p.HandleCount
        }
    }
    $i = 0
    foreach ($p in @($all | Where-Object { $_.ProcessName -eq 'svchost' } | Sort-Object WorkingSet64 -Descending | Select-Object -First 5)) {
        $f["svchost$((++$i))"] = '{0:N0}MB/{1:N0}s/{2}h' -f ($p.WorkingSet64 / 1MB), $cpu[[int]$p.Id], $p.HandleCount
    }
    $names = '\Memory\Pool Nonpaged Bytes', '\Memory\Pool Paged Bytes', '\Memory\Cache Bytes', '\Memory\Available MBytes',
    '\Objects\Processes', '\Objects\Sections', '\Objects\Events', '\Objects\Threads'
    $cs = (Get-Counter -Counter $names).CounterSamples
    foreach ($s in $cs) {
        $leaf = ($s.Path -split '\\')[-1]
        $v = $s.CookedValue
        $f[$leaf] = if ($leaf -like '*bytes' -and $leaf -notlike '*mbytes') { [math]::Round($v / 1MB, 1) } else { [long]$v }
    }
    $f['Running processes'] = $all.Count
    $f['Ended but held'] = [long]$f['processes'] - $all.Count
    $git = @(Get-CimInstance Win32_Process -Filter "Name='git.exe'")
    $f['git.exe'] = $git.Count
    $f['fsmonitor daemons'] = @($git | Where-Object { $_.CommandLine -like '*fsmonitor--daemon*' }).Count
    $f
}

# One reading: three runs of process-start-check.ps1, a minute apart; slow rounds are sampled at once.
function Get-Reading([string]$Label, [hashtable]$Extra = @{}) {
    $runs = @(); $slow = @()
    $t0 = Get-Date
    for ($run = 0; $run -lt 3; $run++) {
        $wait = $t0.AddSeconds(60 * $run) - (Get-Date)
        if ($wait.TotalSeconds -gt 0) { Start-Sleep -Seconds ([int][math]::Ceiling($wait.TotalSeconds)) }
        $rounds = @()
        for ($r = 1; $r -le 5; $r++) {
            $line = (& $check -Rounds 1 | Select-String 'round 1: ([\d,]+) starts/s').Matches[0].Groups[1].Value
            $rate = [double]($line -replace ',', '')
            $rounds += $rate
            if ($rate -lt 100) { $slow += [pscustomobject]@{ run = $run + 1; round = $r; rate = $rate; busiest = (Get-Busiest) } }
        }
        $runs += , @($rounds)
        Write-Host ('  run {0}: {1}  median {2:N0}' -f ($run + 1), (($rounds | ForEach-Object { '{0:N0}' -f $_ }) -join ' '), (Get-Median $rounds))
    }
    $figs = Get-Figures
    $rec = [ordered]@{
        step = $Label; time = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        rounds = $runs; medians = @($runs | ForEach-Object { [math]::Round((Get-Median $_)) })
        slow = $slow; figures = $figs
    }
    foreach ($k in $Extra.Keys) { $rec[$k] = $Extra[$k] }
    ($rec | ConvertTo-Json -Depth 6 -Compress) | Add-Content -Path $jsonl -Encoding UTF8
    Write-Host ('{0}: medians {1}; {2} slow rounds; {3} running, {4} held, git {5} (fsmonitor {6})' -f
        $Label, ($rec.medians -join ' / '), $slow.Count, $figs['Running processes'], $figs['Ended but held'], $figs['git.exe'], $figs['fsmonitor daemons'])
}

# 13 rounds of 8 cmd loops x 50 starts of one program (the same method as process-start-check.ps1).
function Invoke-StartLoad([string]$Exe, [string]$ExeArgs) {
    if (-not (Test-Path -LiteralPath $Exe)) { throw "not found: $Exe" }
    $loop = "for /L %i in (1,1,50) do @`"$Exe`" $ExeArgs >nul 2>nul"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($r = 1; $r -le 13; $r++) {
        $procs = @(1..8 | ForEach-Object { Start-Process -FilePath $env:ComSpec -ArgumentList '/d', '/c', $loop -WindowStyle Hidden -PassThru })
        foreach ($p in $procs) { $p.WaitForExit() }
    }
    Write-Host ('  {0} starts of {1} in {2:N0} s ({3:N0}/s)' -f 5200, (Split-Path -Leaf $Exe), $sw.Elapsed.TotalSeconds, (5200 / $sw.Elapsed.TotalSeconds))
}

function Invoke-Timed([string]$What, [string[]]$DotnetArgs) {
    $log = Join-Path $OutDir ("$What.log")
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Push-Location $Repo
    try { & dotnet @DotnetArgs *> $log; $code = $LASTEXITCODE } finally { Pop-Location }
    $sw.Stop()
    Write-Host ('  {0}: exit {1} in {2:N0} s (log {3})' -f $What, $code, $sw.Elapsed.TotalSeconds, $log)
    [ordered]@{ seconds = [math]::Round($sw.Elapsed.TotalSeconds); exit = $code }
}

if ($Step -eq 'report') {
    if (-not (Test-Path $jsonl)) { throw "nothing recorded: $jsonl" }
    $rows = @(Get-Content $jsonl -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
    '| Step | Time | Medians (3 runs) | Run time s | Running | Held | git (fsmon) | Processes | Threads | Nonpaged MB | Paged MB | Cache MB | Avail MB |'
    '|---|---|---|---|---|---|---|---|---|---|---|---|---|'
    foreach ($r in $rows) {
        $g = $r.figures
        '| {0} | {1} | {2} | {3} | {4} | {5} | {6} ({7}) | {8} | {9} | {10} | {11} | {12} | {13} |' -f $r.step, $r.time.Substring(11), ($r.medians -join ' / '),
        $r.seconds, $g.'Running processes', $g.'Ended but held', $g.'git.exe', $g.'fsmonitor daemons', $g.processes, $g.threads,
        $g.'pool nonpaged bytes', $g.'pool paged bytes', $g.'cache bytes', $g.'available mbytes'
    }
    ''
    'Every round (per run):'
    foreach ($r in $rows) { '- {0}: {1}' -f $r.step, (($r.rounds | ForEach-Object { ($_ | ForEach-Object { '{0:N0}' -f $_ }) -join ' ' }) -join ' | ') }
    ''
    'Busiest in slow rounds:'
    foreach ($r in $rows) { foreach ($s in @($r.slow)) { '- {0} run {1} round {2} ({3:N0}/s): {4}' -f $r.step, $s.run, $s.round, $s.rate, $s.busiest } }
    ''
    'Recorded processes:'
    foreach ($r in $rows) {
        '- {0}: {1}' -f $r.step, ((@($r.figures.PSObject.Properties | Where-Object { $_.Value -is [string] }) | ForEach-Object { '{0} {1}' -f $_.Name, $_.Value }) -join '; ')
    }
    return
}

Write-Host ('{0} step {1}' -f (Get-Date -Format 'HH:mm:ss'), $Step)
switch ($Step) {
    'baseline' { Get-Reading 'baseline' }
    'idle10' {
        $m = if ($IdleMinutes) { $IdleMinutes } else { 10 }
        Start-Sleep -Seconds ($m * 60)
        Get-Reading 'idle10'
    }
    'whoami' { Invoke-StartLoad (Join-Path $env:WINDIR 'System32\whoami.exe') ''; Get-Reading 'whoami' }
    'git' { Invoke-StartLoad 'C:\Program Files\Git\mingw64\bin\git.exe' '--version'; Get-Reading 'git' }
    'build' {
        $x = Invoke-Timed 'build' @('build', 'code\src\SchedulerNext.slnx', '-warnaserror', "-m:$jobs", '--no-incremental')
        Get-Reading 'build' $x
    }
    { $_ -in 'test1', 'test2', 'test3' } {
        $x = Invoke-Timed $Step @('test', 'code\src\SchedulerNext.slnx', '--no-build', "-m:$jobs")
        Get-Reading $Step $x
    }
    'idle15' {
        $m = if ($IdleMinutes) { $IdleMinutes } else { 15 }
        Start-Sleep -Seconds ($m * 60)
        Get-Reading 'idle15'
    }
    'fsmon' {
        Get-Reading 'fsmon-before'
        $d = @(Get-CimInstance Win32_Process -Filter "Name='git.exe'" | Where-Object { $_.CommandLine -like '*fsmonitor--daemon*' })
        foreach ($p in $d) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
        Write-Host ('  ended {0} fsmonitor daemons' -f $d.Count)
        Start-Sleep -Seconds 5
        Get-Reading 'fsmon-after' @{ ended = $d.Count }
    }
}
