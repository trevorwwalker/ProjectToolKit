<#
.SYNOPSIS
  Profiles the test suites: total time per project and the slowest tests, with the processes the slowest Host tests start.

.DESCRIPTION
  Part 1, per project (Core, Web, Tray, Host): one `dotnet test --no-build` run with a TRX logger. Reports the wall time,
  the pass/fail/skip counts, the CPU load at the start, and the -Top slowest tests with their duration (from the TRX).
  The runs are normal ones (xUnit parallelism on), so the durations include contention from neighbouring tests.

  Part 2, Host only: the -Top slowest Host tests are run again, one at a time (xUnit.ParallelizeTestCollections=false),
  with the same counting as bin\git-baseline.ps1: git starts by name are counted exactly by test\perf\GitShim (a git.exe first
  on PATH that logs to PTK_GITLOG), every other process by test\perf\ProcWatch (a snapshot of the process table every 10 ms).
  A test's starts are those inside its start/end window in that serial run, so they belong to that test alone.
  Part 2 is skipped with -NoCount.

  Nothing else should run beside it: it measures. It does not build the tests (--no-build); build them first.
  Writes build\test-profile\test-profile.json (override with -Json) and prints a short summary.

.EXAMPLE
  bin\test-profile.ps1                       # everything
  bin\test-profile.ps1 -Projects Core,Web    # without Host, no counting
#>
param(
    [string[]]$Projects = @('Core', 'Web', 'Tray', 'Host'),
    [int]$Top = 30,
    [int]$RunMinutes = 25,
    [string]$Json = '',
    [switch]$NoCount,
    [string]$FromDir = ''   # analyse the files of an earlier run (its temp folder) instead of running the tests again
)

$ErrorActionPreference = 'Stop'
$Projects = @($Projects | ForEach-Object { $_ -split ',' })
$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repo 'build\test-profile'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$jsonFile = if ($Json -ne '') { $Json } else { Join-Path $outDir 'test-profile.json' }
$work = if ($FromDir -ne '') { $FromDir } else { Join-Path ([IO.Path]::GetTempPath()) ('ptk-test-profile-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
New-Item -ItemType Directory -Force -Path $work | Out-Null
# A worker session injects the scheduler's hooks into every git; left in, they refuse the pushes of the tests' temp repositories.
foreach ($v in 'GIT_CONFIG_COUNT', 'GIT_CONFIG_KEY_0', 'GIT_CONFIG_VALUE_0', 'PTK_CARD', 'PTK_SLOT', 'PTK_SCHEDULER_EXE', 'PTK_MSG_FILE', 'PTK_LOG_DIR', 'PTK_TRAY_FROM') { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }
$jobs = if ($env:PTK_JOBS) { $env:PTK_JOBS } else { '5' }
$shimDir = Join-Path $repo 'build\bin\GitShim\debug'
$watchExe = Join-Path $repo 'build\bin\ProcWatch\debug\ProcWatch.exe'
$realGit = (Get-Command git.exe -CommandType Application | Select-Object -First 1).Source
if ($realGit -notmatch '\\cmd\\git\.exe$') { $realGit = 'C:\Program Files\Git\cmd\git.exe' }

function Build-Tool($name) {
    $exeFile = Join-Path $repo "build\bin\$name\debug\$(if ($name -eq 'GitShim') { 'git' } else { $name }).exe"
    $newest = Get-ChildItem (Join-Path $repo "test\perf\$name") -Include *.cs, *.csproj -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ((Test-Path $exeFile) -and (Get-Item $exeFile).LastWriteTime -gt $newest.LastWriteTime) { return }
    $out = & dotnet build (Join-Path $repo "test\perf\$name\$name.csproj") -warnaserror "-m:$jobs" -v:q -nologo 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "$name did not build: $out" }
}

function Epoch([datetime]$t) { [long]([DateTimeOffset]$t.ToUniversalTime()).ToUnixTimeMilliseconds() }
function Cpu-Load { [int](Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average }
function Read-Live($path) {
    if (-not (Test-Path $path)) { return @() }
    $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try { $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8); $t = $sr.ReadToEnd() } finally { $fs.Dispose() }
    @($t -split "`n" | Where-Object { $_ -ne '' })
}

function Read-Trx($path) {
    [xml]$x = Get-Content -Raw -Encoding UTF8 $path
    $ns = New-Object Xml.XmlNamespaceManager($x.NameTable); $ns.AddNamespace('t', $x.DocumentElement.NamespaceURI)
    foreach ($r in $x.SelectNodes('//t:UnitTestResult', $ns)) {
        $start = [datetimeoffset]::Parse($r.startTime).UtcDateTime; $end = [datetimeoffset]::Parse($r.endTime).UtcDateTime
        [pscustomobject]@{
            Name = $r.testName; Outcome = $r.outcome; Seconds = [math]::Round([timespan]::Parse($r.duration).TotalSeconds, 2)
            Start = (Epoch $start); End = (Epoch $end); Message = "$($r.Output.ErrorInfo.Message)"
        }
    }
}

# git starts by name (the shim log) and every other process (ProcWatch), for one command run.
function Start-Counting([string]$tag) {
    $c = [pscustomobject]@{ GitLog = (Join-Path $work "$tag.git.log"); WatchLog = (Join-Path $work "$tag.watch.log"); Stop = (Join-Path $work "$tag.stop"); OldPath = $env:PATH; Watch = $null }
    $env:PTK_GITLOG = $c.GitLog; $env:PTK_REALGIT = $realGit
    $c.Watch = Start-Process $watchExe -ArgumentList ('"' + $c.WatchLog + '"'), ('"' + $c.Stop + '"') -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 800
    $env:PATH = "$shimDir;$($c.OldPath)"
    $c
}

function Stop-Counting($c, [int]$rootPid) {
    $env:PATH = $c.OldPath
    Remove-Item Env:PTK_GITLOG, Env:PTK_REALGIT -ErrorAction SilentlyContinue
    if ($c.Watch) { New-Item $c.Stop -ItemType File -Force | Out-Null }
    if ($c.Watch) { $c.Watch.WaitForExit(10000) | Out-Null }
    $calls = @(Read-Live $c.GitLog | ForEach-Object {
        $f = $_ -split "`t", 4
        if ($f.Count -eq 4) { [pscustomobject]@{ Ms = [long]$f[0]; Pid = [int]$f[1]; Args = $f[3] } }
    })
    $rows = @(Read-Live $c.WatchLog | ForEach-Object {
        $f = $_ -split "`t"
        if ($f.Count -eq 4) { [pscustomobject]@{ Ms = [long]$f[0]; Pid = [int]$f[1]; Ppid = [int]$f[2]; Name = $f[3].Trim() } }
    })
    # processes below the root, decided in time order (pids are reused); the first snapshot is everything already running
    $procs = New-Object System.Collections.Generic.List[object]
    if ($rows.Count -gt 0) {
        $first = ($rows | Measure-Object Ms -Minimum).Minimum
        $member = @{ $rootPid = $true }
        $shimPids = New-Object 'System.Collections.Generic.HashSet[int]'
        foreach ($g in $calls) { [void]$shimPids.Add($g.Pid) }
        $wrapperPids = New-Object 'System.Collections.Generic.HashSet[int]'
        foreach ($r in ($rows | Where-Object { $_.Ms -gt $first } | Sort-Object Ms)) {
            if ($r.Pid -eq $rootPid) { continue }
            $in = $member.ContainsKey($r.Ppid) -and $member[$r.Ppid]
            $member[$r.Pid] = $in
            if (-not $in -or $r.Name -eq 'ProcWatch.exe') { continue }
            $name = $r.Name
            if ($name -eq 'git.exe') {
                # lineage: the shim (pid in the shim log) -> cmd\git.exe wrapper -> mingw64\bin\git.exe -> started by git itself
                if ($shimPids.Contains($r.Pid)) { $name = 'git.exe (shim: exact)' }
                elseif ($shimPids.Contains($r.Ppid)) { $name = 'git.exe (wrapper)'; [void]$wrapperPids.Add($r.Pid) }
                elseif ($wrapperPids.Contains($r.Ppid)) { $name = 'git.exe (mingw64)' }
                else { $name = 'git.exe (by git)' }
            }
            $procs.Add([pscustomobject]@{ Ms = $r.Ms; Pid = $r.Pid; Name = $name })
        }
    }
    [pscustomobject]@{ Calls = $calls; Procs = $procs.ToArray() }
}

function Get-Sub([string]$a) { if ($a -match '^(?:(?:-C|-c)\s+(?:"[^"]*"|\S+)\s+)*(\S+)') { $Matches[1] } else { '(none)' } }

function Summarise([object[]]$tests) {
    [ordered]@{
        passed = @($tests | Where-Object { $_.Outcome -eq 'Passed' }).Count
        failed = @($tests | Where-Object { $_.Outcome -eq 'Failed' }).Count
        other = @($tests | Where-Object { $_.Outcome -notin 'Passed', 'Failed' }).Count
        sumOfTestSeconds = [math]::Round(($tests | Measure-Object Seconds -Sum).Sum, 1)
    }
}

$result = [ordered]@{ date = (Get-Date -Format 'yyyy-MM-dd HH:mm'); jobs = $jobs; projects = @() }
$hostTests = @()
foreach ($name in $Projects) {
    $proj = Join-Path $repo "test\unit\Scheduler.$name.Tests"
    $load = Cpu-Load
    Write-Host ("[{0}] {1}: CPU load {2}% at the start; running" -f (Get-Date -Format 'HH:mm:ss'), $name, $load)
    $trx = "$name.trx"
    $timedOut = $false; $exit = 0; $secs = 0
    if ($FromDir -eq '') {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process dotnet -ArgumentList 'test', ('"' + $proj + '"'), '--no-build', "-m:$jobs", '-nologo', '--logger', "trx;LogFileName=$trx", '--results-directory', ('"' + $work + '"') `
            -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $work "$name.out") -RedirectStandardError (Join-Path $work "$name.err")
        $null = $p.Handle   # keeps ExitCode readable after the exit
        $timedOut = -not $p.WaitForExit($RunMinutes * 60000)
        if ($timedOut) { & taskkill /PID $p.Id /T /F 2>&1 | Out-Null }
        $secs = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        $exit = $(if ($timedOut) { -1 } else { $p.ExitCode })
    }
    $trxFile = Join-Path $work $trx
    $tests = if (Test-Path $trxFile) { @(Read-Trx $trxFile) } else { @() }
    if ($FromDir -ne '' -and $tests.Count -gt 0) { $secs = [math]::Round((($tests | Measure-Object End -Maximum).Maximum - ($tests | Measure-Object Start -Minimum).Minimum) / 1000.0, 1) }
    $sum = Summarise $tests
    Write-Host ("[{0}] {1}: {2} s, exit {3}{4}, passed {5}, failed {6}, other {7}" -f (Get-Date -Format 'HH:mm:ss'), $name, $secs, $exit, $(if ($timedOut) { ' (TIMED OUT)' } else { '' }), $sum.passed, $sum.failed, $sum.other)
    $slow = @($tests | Sort-Object Seconds -Descending | Select-Object -First $Top)
    $failed = @($tests | Where-Object { $_.Outcome -eq 'Failed' })
    $result.projects += [ordered]@{
        project = $name; wallSeconds = $secs; exit = $exit; timedOut = $timedOut; cpuLoadAtStart = $load
        summary = $sum
        slowest = @($slow | ForEach-Object { [ordered]@{ test = $_.Name; seconds = $_.Seconds; outcome = $_.Outcome } })
        failedTests = @($failed | ForEach-Object { [ordered]@{ test = $_.Name; message = ($_.Message.Substring(0, [Math]::Min(200, $_.Message.Length)) -replace '\s+', ' ') } })
    }
    if ($name -eq 'Host') { $hostTests = $slow }
}

# Part 2: the slowest Host tests again, one at a time and counted.
if (-not $NoCount -and $hostTests.Count -gt 0) {
    Build-Tool 'GitShim'; Build-Tool 'ProcWatch'
    $names = @($hostTests | ForEach-Object { ($_.Name -replace '\(.*$', '').Trim() } | Sort-Object -Unique)
    $filter = ($names | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
    $proj = Join-Path $repo 'test\unit\Scheduler.Host.Tests'
    $load = Cpu-Load
    Write-Host ("[{0}] counted serial run of {1} Host test methods: CPU load {2}% at the start" -f (Get-Date -Format 'HH:mm:ss'), $names.Count, $load)
    $c = if ($FromDir -ne '') { [pscustomobject]@{ GitLog = (Join-Path $work 'counted.git.log'); WatchLog = (Join-Path $work 'counted.watch.log'); Watch = $null; OldPath = $env:PATH } } else { $null }
    $rootPid = 0; $secs = 0; $exit = 0; $timedOut = $false
    if ($FromDir -eq '') {
        $c = Start-Counting 'counted'
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process dotnet -ArgumentList 'test', ('"' + $proj + '"'), '--no-build', "-m:$jobs", '-nologo', '--filter', ('"' + $filter + '"'), '--logger', 'trx;LogFileName=counted.trx', '--results-directory', ('"' + $work + '"'), '--', 'xUnit.ParallelizeTestCollections=false' `
            -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $work 'counted.out') -RedirectStandardError (Join-Path $work 'counted.err')
        $null = $p.Handle   # keeps ExitCode readable after the exit
        $timedOut = -not $p.WaitForExit($RunMinutes * 60000)
        if ($timedOut) { & taskkill /PID $p.Id /T /F 2>&1 | Out-Null }
        $secs = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        $rootPid = $p.Id; $exit = $(if ($timedOut) { -1 } else { $p.ExitCode })
    } else {
        # the dotnet.exe that appeared first after the first snapshot of the earlier run's ProcWatch log
        $rows = @(Read-Live $c.WatchLog | ForEach-Object { $f = $_ -split "`t"; if ($f.Count -eq 4) { [pscustomobject]@{ Ms = [long]$f[0]; Pid = [int]$f[1]; Name = $f[3].Trim() } } })
        $first = ($rows | Measure-Object Ms -Minimum).Minimum
        $rootPid = ($rows | Where-Object { $_.Ms -gt $first -and $_.Name -eq 'dotnet.exe' } | Sort-Object Ms | Select-Object -First 1).Pid
    }
    $r = Stop-Counting $c $rootPid
    Write-Host ("[{0}] counted run: {1} s, {2} git starts by name, {3} other processes seen" -f (Get-Date -Format 'HH:mm:ss'), $secs, $r.Calls.Count, $r.Procs.Count)
    $counted = @(Read-Trx (Join-Path $work 'counted.trx'))
    $bySlow = @{}; foreach ($t in $hostTests) { $bySlow[$t.Name] = $t.Seconds }
    $rows = foreach ($t in ($counted | Sort-Object Seconds -Descending)) {
        $in = { param($x) $x.Ms -ge $t.Start - 20 -and $x.Ms -le $t.End + 20 }
        $g = @($r.Calls | Where-Object { & $in $_ })
        $o = @($r.Procs | Where-Object { (& $in $_) -and $_.Name -ne 'git.exe (shim: exact)' })
        $nonGit = @($o | Where-Object { $_.Name -notlike 'git.exe*' })
        $gitExe = @($o | Where-Object { $_.Name -like 'git.exe*' })   # git started by absolute path never meets the shim; its git.exe processes are seen here (cmd\git.exe wrapper + mingw64in\git.exe: two per start)
        [ordered]@{
            test = $t.Name; outcome = $t.Outcome; serialSeconds = $t.Seconds; parallelSeconds = $bySlow[$t.Name]
            gitStarts = $g.Count
            gitExeSeen = $gitExe.Count
            gitBySubcommand = @($g | Group-Object { Get-Sub $_.Args } | Sort-Object Count -Descending | Select-Object -First 5 | ForEach-Object { "$($_.Name) x$($_.Count)" })
            otherProcessStarts = $nonGit.Count
            otherByExe = @($nonGit | Group-Object Name | Sort-Object Count -Descending | ForEach-Object { "$($_.Name) x$($_.Count)" })
        }
    }
    $result.counted = [ordered]@{ wallSeconds = $secs; cpuLoadAtStart = $load; exit = $exit; gitStartsTotal = $r.Calls.Count; otherProcessesTotal = $r.Procs.Count; tests = @($rows) }
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $jsonFile
Write-Host "written: $jsonFile (temp files in $work)"
foreach ($p in $result.projects) {
    Write-Host ("`n{0}: {1} s wall, {2} passed, {3} failed; slowest:" -f $p.project, $p.wallSeconds, $p.summary.passed, $p.summary.failed)
    $p.slowest | Select-Object -First 5 | ForEach-Object { Write-Host ("  {0,7:0.00} s  {1}" -f $_.seconds, $_.test) }
}
