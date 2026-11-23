<#
.SYNOPSIS
    The git performance baseline (1041-B): processes started and time, per test run and per scheduler job.

.DESCRIPTION
    Measures, changes nothing in the product, and writes test\perf\git-baseline.json (the numbers) and
    docs\perf\git-baseline.md (the tables), so a later card can measure before/after the same way.

    How processes are counted (no elevation, no Sysmon, no ETW here):
      * git starts BY NAME are counted exactly by test\perf\GitShim, a git.exe put first on PATH that logs
        (time, pid, working folder, arguments) to PTK_GITLOG and then runs the real git. A start by absolute path
        is not seen by it.
      * every other process is counted by test\perf\ProcWatch, which snapshots the process table every 10 ms with
        each process's parent; the script keeps what descends from the measured command. A process that lives
        under about 10 ms can be missed, so those counts are a lower bound; the git shim counts are exact.
    Times come from separate runs with neither tool running (the tools cost time), as a range over -Runs runs.

.PARAMETER Part
    Tests, Scheduler, All (default) or Report (only render the markdown from the json). A part replaces its own section of the json and keeps the other.

.PARAMETER Projects
    Test projects for the Tests part: Core, Web, Tray, Host (default all).

.PARAMETER Runs
    Timed runs per project / scenario (default 3).

.PARAMETER Serial
    Run the counted test run one test at a time, so every start is attributed to one test (the top-20 list).
    Slow for Host. Without it the counted run is parallel and the per-test list is left out.

.PARAMETER Locks
    Both (default), Off or On: which scheduler sessions to measure (dynamic locks off / on); two can run side by side.

.PARAMETER CountedMinutes
    The longest the counted test run may take (default 25); a run over it is stopped and its partial counts kept, marked.

.PARAMETER Json
    Where the numbers are kept (default test\perf\git-baseline.json).

.PARAMETER Exe
    scheduler-next.exe to measure (default build\bin\Scheduler.Host\debug\scheduler-next.exe).
#>
param(
    [ValidateSet('Tests', 'Scheduler', 'All', 'Report')][string]$Part = 'All',
    [string[]]$Projects = @('Core', 'Web', 'Tray', 'Host'),
    [int]$Runs = 3,
    [switch]$Serial,
    [string]$Exe = '',
    [string]$Json = '',
    [int]$CountedMinutes = 25,
    [ValidateSet('Both', 'Off', 'On')][string]$Locks = 'Both'
)

$ErrorActionPreference = 'Stop'
$Projects = @($Projects | ForEach-Object { $_ -split ',' })
$repo = Split-Path -Parent $PSScriptRoot
$jsonFile = if ($Json -ne '') { $Json } else { Join-Path $repo 'test\perf\git-baseline.json' }
$mdFile = Join-Path $repo 'docs\perf\git-baseline.md'
$work = Join-Path ([IO.Path]::GetTempPath()) ('ptk-git-baseline-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $work | Out-Null
if ($Exe -eq '') { $Exe = Join-Path $repo 'build\bin\Scheduler.Host\debug\scheduler-next.exe' }
# A worker session injects the scheduler's own hooks into every git (GIT_CONFIG_* with core.hooksPath), its card and its slot:
# left in, they refuse the pushes of the temp repositories and break tests that push. The measurement runs without them.
foreach ($v in 'GIT_CONFIG_COUNT', 'GIT_CONFIG_KEY_0', 'GIT_CONFIG_VALUE_0', 'PTK_CARD', 'PTK_SLOT', 'PTK_SCHEDULER_EXE', 'PTK_MSG_FILE', 'PTK_LOG_DIR', 'PTK_TRAY_FROM') { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }
$jobs = if ($env:PTK_JOBS) { $env:PTK_JOBS } else { '5' }
$shimDir = Join-Path $repo 'build\bin\GitShim\debug'
$watchExe = Join-Path $repo 'build\bin\ProcWatch\debug\ProcWatch.exe'
$realGit = (Get-Command git.exe -CommandType Application | Select-Object -First 1).Source
if ($realGit -notmatch '\\cmd\\git\.exe$') { $realGit = 'C:\Program Files\Git\cmd\git.exe' }

function Build-Tool($name) {
    # Only when missing or older than its sources: a run in progress may be using the tool (a running exe cannot be replaced).
    $exeFile = Join-Path $repo "build\bin\$name\debug\$(if ($name -eq 'GitShim') { 'git' } else { $name }).exe"
    $newest = Get-ChildItem (Join-Path $repo "test\perf\$name") -Include *.cs, *.csproj -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ((Test-Path $exeFile) -and (Get-Item $exeFile).LastWriteTime -gt $newest.LastWriteTime) { return }
    $out = & dotnet build (Join-Path $repo "test\perf\$name\$name.csproj") -warnaserror "-m:$jobs" -v:q -nologo 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "$name did not build: $out" }
}

function Epoch([datetime]$t) { [long]([DateTimeOffset]$t.ToUniversalTime()).ToUnixTimeMilliseconds() }

# Start ProcWatch and put the git shim first on PATH; returns what Stop-Counting needs.
function Start-Counting([string]$tag) {
    $c = [pscustomobject]@{
        Tag = $tag; GitLog = (Join-Path $work "$tag.git.log"); WatchLog = (Join-Path $work "$tag.watch.log")
        Stop = (Join-Path $work "$tag.stop"); OldPath = $env:PATH; Watch = $null
    }
    Remove-Item $c.GitLog, $c.WatchLog, $c.Stop -ErrorAction SilentlyContinue
    $env:PTK_GITLOG = $c.GitLog; $env:PTK_REALGIT = $realGit
    $c.Watch = Start-Process $watchExe -ArgumentList ('"' + $c.WatchLog + '"'), ('"' + $c.Stop + '"') -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 800
    $env:PATH = "$shimDir;$($c.OldPath)"
    return $c
}

# Restore PATH, stop ProcWatch; returns the exact git calls and the processes below $rootPid.
function Stop-Counting($c, [int[]]$rootPids) {
    $env:PATH = $c.OldPath
    Remove-Item Env:PTK_GITLOG, Env:PTK_REALGIT -ErrorAction SilentlyContinue
    New-Item $c.Stop -ItemType File -Force | Out-Null
    $c.Watch.WaitForExit(10000) | Out-Null
    $calls = @(Read-GitCalls $c.GitLog)
    $procs = @(Read-Descendants $c.WatchLog $rootPids $calls)
    [pscustomobject]@{ Calls = $calls; Procs = $procs }
}

# Run a command counted.
function Invoke-Counted([string]$file, [string[]]$arguments, [string]$tag, [int]$timeoutMinutes = 25) {
    $c = Start-Counting $tag
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $quoted = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $p = Start-Process $file -ArgumentList $quoted -PassThru -NoNewWindow -WorkingDirectory $repo -RedirectStandardOutput (Join-Path $work "$tag.out") -RedirectStandardError (Join-Path $work "$tag.err")
    $timedOut = -not $p.WaitForExit($timeoutMinutes * 60000)
    if ($timedOut) { & taskkill /PID $p.Id /T /F 2>&1 | Out-Null }
    $secs = $sw.Elapsed.TotalSeconds
    $r = Stop-Counting $c @($p.Id)
    [pscustomobject]@{ Seconds = $secs; Exit = $(if ($timedOut) { -1 } else { $p.ExitCode }); TimedOut = $timedOut; Calls = $r.Calls; Procs = $r.Procs }
}

function Read-GitCalls($path) {
    if (-not (Test-Path $path)) { return @() }
    Get-Content -Encoding UTF8 $path | ForEach-Object {
        $f = $_ -split "`t", 4
        if ($f.Count -eq 4) { [pscustomobject]@{ Ms = [long]$f[0]; Pid = [int]$f[1]; Cwd = $f[2]; Args = $f[3] } }
    }
}

# Processes below $rootPids, with git.exe told apart by lineage: the shim (its pid is in the shim log) -> the
# cmd\git.exe wrapper (child of a shim) -> mingw64\bin\git.exe (child of a wrapper) -> anything git starts.
function Read-Descendants($watchLog, [int[]]$rootPids, $calls) {
    $rows = @(Read-Live $watchLog | ForEach-Object {
        $f = $_ -split "`t"
        if ($f.Count -eq 4) { [pscustomobject]@{ Ms = [long]$f[0]; Pid = [int]$f[1]; Ppid = [int]$f[2]; Name = $f[3].Trim() } }
    })
    if ($rows.Count -eq 0) { return @() }
    # Windows reuses pids quickly, so membership is decided in time order, row by row: a row is below the roots
    # when its parent was below them when the row first appeared; a reused pid with another parent starts afresh.
    # The first snapshot (everything already running) is left out.
    $first = ($rows | Measure-Object Ms -Minimum).Minimum
    $member = @{}; $nameOf = @{}; $parentOf = @{}
    foreach ($p in $rootPids) { $member[$p] = $true }
    $kept = New-Object System.Collections.Generic.List[object]
    foreach ($r in ($rows | Where-Object { $_.Ms -gt $first } | Sort-Object Ms)) {
        $isRoot = $rootPids -contains $r.Pid
        $in = $isRoot -or ($member.ContainsKey($r.Ppid) -and $member[$r.Ppid])
        if (-not $isRoot) { $member[$r.Pid] = $in }
        $nameOf[$r.Pid] = $r.Name; $parentOf[$r.Pid] = $r.Ppid
        if ($in -and -not $isRoot -and $r.Name -ne 'ProcWatch.exe') { $kept.Add($r) }
    }
    # git.exe told apart by lineage: the shim (its pid is in the shim log) -> the cmd\git.exe wrapper (child of a
    # shim) -> mingw64\bin\git.exe (child of a wrapper) -> anything git starts.
    $shimPids = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($c in $calls) { [void]$shimPids.Add($c.Pid) }
    $wrapperPids = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($r in $kept) { if ($r.Name -eq 'git.exe' -and $shimPids.Contains($r.Ppid)) { [void]$wrapperPids.Add($r.Pid) } }
    foreach ($r in $kept) {
        $name = $r.Name
        if ($name -eq 'git.exe') {
            if ($shimPids.Contains($r.Pid)) { $name = 'git.exe (the shim: the exact count)' }
            elseif ($shimPids.Contains($r.Ppid)) { $name = 'git.exe (cmd\git.exe wrapper)' }
            elseif ($wrapperPids.Contains($r.Ppid)) { $name = 'git.exe (mingw64\bin\git.exe)' }
            else { $name = 'git.exe (started by git itself)' }
        }
        # below a powershell.exe that is itself below the root: a stub worker's, not the scheduler's own
        $under = 'scheduler'; $q = $r.Ppid
        for ($d = 0; $d -lt 12 -and $q -and ($rootPids -notcontains $q) -and $nameOf.ContainsKey($q); $d++) {
            if ($nameOf[$q] -eq 'powershell.exe') { $under = 'worker'; break }
            $q = $parentOf[$q]
        }
        [pscustomobject]@{ Ms = $r.Ms; Pid = $r.Pid; Name = $name; Under = $under }
    }
}

# Where the git calls ran: the folder one level under the temp folder, or the first three folders elsewhere.
function Get-Area([string]$cwd) {
    $tmp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $c = $cwd.TrimEnd('\')
    if ($c.StartsWith($tmp, [StringComparison]::OrdinalIgnoreCase)) {
        $rest = $c.Substring($tmp.Length).TrimStart('\').Split('\')
        $first = $rest[0] -replace '[0-9a-f]{8,}', '<id>' -replace '[0-9]{4,}', '<n>'
        return @{ Folder = '%TEMP%\' + $first + '\...'; Outside = $true }
    }
    $segs = $c.Split('\')
    $take = [Math]::Min(3, $segs.Count)
    return @{ Folder = (($segs[0..($take - 1)] -join '\') -replace '[0-9a-f]{8,}', '<id>') + '\...'; Outside = -not ($c -like 'C:\Projects\*') }
}

function Get-Areas($calls) {
    $areas = @{}
    foreach ($c in $calls) {
        $a = Get-Area $c.Cwd
        if (-not $areas.ContainsKey($a.Folder)) { $areas[$a.Folder] = @{ folder = $a.Folder; outside = $a.Outside; calls = 0; places = (New-Object 'System.Collections.Generic.HashSet[string]') } }
        $areas[$a.Folder].calls++; [void]$areas[$a.Folder].places.Add($c.Cwd)
    }
    @($areas.Values | Sort-Object { $_.calls } -Descending | ForEach-Object { [ordered]@{ folder = $_.folder; outsideProjects = $_.outside; gitCalls = $_.calls; distinctWorkingFolders = $_.places.Count } })
}

function Group-Exe($procs) { @($procs | Group-Object Name | Sort-Object Count -Descending | ForEach-Object { [ordered]@{ exe = $_.Name; count = $_.Count } }) }
function Get-Sub([string]$gitArgs) { if ($gitArgs -match '^(?:(?:-C|-c)\s+(?:"[^"]*"|\S+)\s+)*(\S+)') { $Matches[1] } else { '(none)' } }
function Group-Sub($calls) { @($calls | Group-Object { Get-Sub $_.Args } | Sort-Object Count -Descending | Select-Object -First 15 | ForEach-Object { [ordered]@{ subcommand = $_.Name; count = $_.Count } }) }

function Read-Trx($path) {
    [xml]$x = Get-Content -Raw -Encoding UTF8 $path
    $ns = New-Object Xml.XmlNamespaceManager($x.NameTable); $ns.AddNamespace('t', $x.DocumentElement.NamespaceURI)
    foreach ($r in $x.SelectNodes('//t:UnitTestResult', $ns)) {
        [pscustomobject]@{
            Name = $r.testName; Outcome = $r.outcome
            Start = (Epoch ([datetimeoffset]::Parse($r.startTime).UtcDateTime)); End = (Epoch ([datetimeoffset]::Parse($r.endTime).UtcDateTime))
            Message = "$($r.Output.ErrorInfo.Message)"
        }
    }
}

function Measure-TestProject([string]$name) {
    $proj = Join-Path $repo "test\unit\Scheduler.$name.Tests"
    Write-Host "tests $name : $Runs timed runs (no tool running), then one counted run"
    $times = @(); $passed = $null
    for ($i = 1; $i -le $Runs; $i++) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $outFile = Join-Path $work "timed-$name-$i.out"
        $pr = Start-Process dotnet -ArgumentList "test", ("""$proj"""), "--no-build", "-m:$jobs", "-nologo" -NoNewWindow -Wait -PassThru -RedirectStandardOutput $outFile -RedirectStandardError "$outFile.err"
        $o = (Get-Content -Encoding UTF8 $outFile | Select-Object -Last 3) -join "`n"
        $times += [math]::Round($sw.Elapsed.TotalSeconds, 1)
        $line = ($o -split "`n" | Where-Object { $_ -match 'Passed!|Failed!' } | Select-Object -First 1)
        if ($line) { $passed = $line.Trim() }
        Write-Host ("  run {0}: {1} s  {2}" -f $i, $times[-1], $passed)
    }
    $trx = "$name.trx"
    $dargs = @('test', $proj, '--no-build', "-m:$jobs", '-nologo', '--logger', "trx;LogFileName=$trx", '--results-directory', $work)
    if ($Serial) { $dargs += @('--', 'xUnit.ParallelizeTestCollections=false') }
    $r = Invoke-Counted 'dotnet' $dargs "tests-$name" $CountedMinutes
    $calls = $r.Calls; $procs = $r.Procs
    Write-Host ("  counted run: {0:0} s, {1} git starts by name, {2} other processes seen" -f $r.Seconds, $calls.Count, $procs.Count)

    $top = @(); $failed = @()
    $trxFile = Join-Path $work $trx
    if (Test-Path $trxFile) {
        $tests = @(Read-Trx $trxFile)
        $failed = @($tests | Where-Object { $_.Outcome -eq 'Failed' })
        if ($Serial) {
            $rows = foreach ($t in $tests) {
                $in = { param($x) $x.Ms -ge $t.Start - 20 -and $x.Ms -le $t.End + 20 }
                [pscustomobject]@{
                    Test = $t.Name; Seconds = [math]::Round(($t.End - $t.Start) / 1000.0, 2)
                    Git = @($calls | Where-Object { & $in $_ }).Count
                    Processes = @($procs | Where-Object { (& $in $_) -and $_.Name -notlike 'git.exe (the shim*' }).Count
                }
            }
            $top = @($rows | Where-Object { $_.Processes -gt 0 -or $_.Git -gt 0 } | Sort-Object @{ Expression = { $_.Processes + $_.Git }; Descending = $true } | Select-Object -First 20 | ForEach-Object { [ordered]@{ test = $_.Test; gitCallsByName = $_.Git; otherProcessesSeen = $_.Processes; seconds = $_.Seconds } })
        }
    }
    [ordered]@{
        project = $name; runs = $Runs; seconds = $times; result = $passed
        countedRunSeconds = [math]::Round($r.Seconds, 1); countedSerial = [bool]$Serial; countedExit = $r.Exit; countedTimedOut = $r.TimedOut
        gitCallsByName = $calls.Count; otherProcessesSeen = $procs.Count
        processesByExe = (Group-Exe $procs); gitBySubcommand = (Group-Sub $calls); tempRepoFolders = (Get-Areas $calls); topTests = $top
        failedCount = $failed.Count
        failedExamples = @($failed | Select-Object -First 5 | ForEach-Object { [ordered]@{ test = $_.Name; message = $_.Message.Substring(0, [Math]::Min(200, $_.Message.Length)) -replace '\s+', ' ' } })
    }
}

$script:RealGitExe = 'C:\Program Files\Git\mingw64\bin\git.exe'

function Read-Live($path) {
    # the shim and ProcWatch keep their files open for appending: read with sharing
    if (-not (Test-Path $path)) { return @() }
    $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try { $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8); $t = $sr.ReadToEnd() } finally { $fs.Dispose() }
    @($t -split "`n" | Where-Object { $_ -ne '' })
}

function Get-LastCallMs($c) {
    $l = @(Read-Live $c.GitLog)
    if ($l.Count -eq 0) { return 0 }
    [long](($l[-1] -split "`t")[0])
}

# Wait until no git call has been logged for $quietMs (the scenario has settled) or $maxSec passed; returns the
# time the last call was logged (or $from when there was none).
function Wait-Quiet($c, [long]$from, [int]$quietMs = 4000, [int]$maxSec = 90) {
    $end = [DateTime]::UtcNow.AddSeconds($maxSec)
    while ([DateTime]::UtcNow -lt $end) {
        Start-Sleep -Milliseconds 500
        $last = [Math]::Max($from, (Get-LastCallMs $c))
        if ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - $last -gt $quietMs) { return $last }
    }
    return [Math]::Max($from, (Get-LastCallMs $c))
}

function Now-Ms { [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }

function Real-Git([string]$dir, [string[]]$gitArgs) {
    $ErrorActionPreference = 'Continue'   # git's warnings on stderr (CRLF) are not errors
    $o = & $script:RealGitExe -C $dir -c user.name=perf -c user.email=perf@example.invalid @gitArgs 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "git $($gitArgs -join ' ') in $dir failed: $o" }
    $o
}

function Post-Page([int]$port, [string]$route, [string]$project) {
    try {
        $r = Invoke-WebRequest -UseBasicParsing -Method POST -Uri "http://127.0.0.1:$port$route`?project=$project" -Body '' -TimeoutSec 30
        return [int]$r.StatusCode
    } catch { return $_.Exception.Message }
}

# One generated pool of 2 projects with one queued card, one review card and the rest done; its scheduler.json files
# rewritten for the scenario and pushed to each project's origin. $locks switches dynamic locks (hooks, lock
# requests, the audit) on; $healthCommand is what the health check runs.
function New-PerfPool([string]$root, [int]$port, [bool]$locks, [string]$stub) {
    & (Join-Path $repo 'build\bin\PoolMaker\debug\PoolMaker.exe') $root 2 10 $port | Out-Null
    $names = @((Get-Content -Encoding UTF8 (Join-Path $root 'pool.json') | ConvertFrom-Json).projects | ForEach-Object { $_.name })
    foreach ($n in $names) {
        $dir = Join-Path $root $n
        $f = Join-Path $dir 'jobs\scheduler.json'
        $j = Get-Content -Raw -Encoding UTF8 $f | ConvertFrom-Json
        $j.limits.pollSeconds = 10
        $j.worker.cli = 'powershell.exe'
        $j.worker.args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $stub, '{prompt}')
        $j.health.command = 'cmd /c exit 0'
        $j | Add-Member -NotePropertyName locks -NotePropertyValue $locks -Force
        $j | Add-Member -NotePropertyName planner -NotePropertyValue ([pscustomobject]@{ outbox = (Join-Path $root "$n-outbox"); fetchRepo = $dir }) -Force
        $j | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $f
        [void](Real-Git $dir @('add', '-A')); [void](Real-Git $dir @('commit', '-q', '-m', 'perf config'))
        [void](Real-Git $dir @('push', '-q', 'origin', 'HEAD:main'))
        New-Item -ItemType Directory -Force (Join-Path $root "$n-outbox") | Out-Null
    }
    $names
}

function Write-Stub([string]$path, [string]$root, [bool]$locks) {
    # A worker that does what a real one does to git and to the card, without a model: pull, edit the card's
    # Result, push a code change, ask for a lock when locks are on, hand over the move request, print the result.
    # It uses git by absolute path, so the shim counts only what the scheduler (and its hooks) start.
    $text = @'
param($prompt)
trap { Add-Content (Join-Path $env:PTK_LOG_DIR 'stub.log') ("$(Get-Date -Format s) $_ at line $($_.InvocationInfo.ScriptLineNumber)"); exit 1 }
$ErrorActionPreference = 'Continue'
$git = 'C:\Program Files\Git\mingw64\bin\git.exe'
$root = '__ROOT__'
$cwd = (Get-Location).Path
$name = (Split-Path (Split-Path $cwd -Parent) -Leaf) -replace '-ws$', ''
& $git pull -q --rebase origin main 2>$null | Out-Null
$card = Get-ChildItem (Join-Path $cwd 'jobs\active') -Filter '*.md' | Select-Object -First 1
$text = (Get-Content -Raw -Encoding UTF8 $card.FullName) -replace '(?m)^Status:.*$', 'Status: done'
$text = $text.TrimEnd() + "`n2026-11-21 12:00 stub worker: done.`n"
Set-Content -Encoding UTF8 -NoNewline -LiteralPath $card.FullName -Value $text
New-Item -ItemType Directory -Force (Join-Path $cwd 'src\b') | Out-Null
Set-Content -Encoding UTF8 (Join-Path $cwd 'src\b\work.txt') 'work'
if ('__LOCKS__' -eq '1') {
    & '__EXE__' lock 'src/a/extra.txt' --why 'perf baseline' | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $cwd 'src\a') | Out-Null
    Set-Content -Encoding UTF8 (Join-Path $cwd 'src\a\extra.txt') 'extra'
}
& $git add -A src 2>$null | Out-Null
& $git -c user.name=stub -c user.email=stub@example.invalid commit -q -m 'work' 2>$null | Out-Null
& $git push -q origin HEAD:main 2>$null | Out-Null
$moves = Join-Path $(if ($env:PTK_LOG_DIR) { $env:PTK_LOG_DIR } else { Join-Path $root ($name + '-log') }) 'moves'
New-Item -ItemType Directory -Force $moves | Out-Null
$f = Join-Path $moves ((Get-Date -Format yyyyMMdd-HHmmss-fff) + '-slot9')
Set-Content -Encoding UTF8 -NoNewline -Path ($f + '.tmp') -Value ('Card: ' + $card.BaseName + [char]10 + 'To: done' + [char]10 + '---' + [char]10 + $text)
Move-Item ($f + '.tmp') ($f + '.move')
Write-Output 'LANE-RESULT: done'
'@
    $text = $text.Replace('__ROOT__', $root).Replace('__LOCKS__', $(if ($locks) { '1' } else { '0' })).Replace('__EXE__', $Exe)
    Set-Content -Encoding UTF8 -LiteralPath $path -Value $text
}

function Slice($calls, $procs, [long]$t0, [long]$t1) {
    $cs = @($calls | Where-Object { $_.Ms -ge $t0 -and $_.Ms -le $t1 })
    $ps = @($procs | Where-Object { $_.Ms -ge $t0 -and $_.Ms -le $t1 -and $_.Name -notlike 'git.exe (the shim*' })
    $remote = @($cs | Where-Object { (Get-Sub $_.Args) -in 'fetch', 'push', 'pull', 'ls-remote', 'clone' })
    $own = @($ps | Where-Object { $_.Under -ne 'worker' })
    [ordered]@{
        seconds = [math]::Round(($t1 - $t0) / 1000.0, 1)
        gitCallsByName = $cs.Count
        remoteOperations = $remote.Count
        remoteBySubcommand = ((@($remote | Group-Object { Get-Sub $_.Args } | ForEach-Object { "$($_.Name) $($_.Count)" })) -join ', ')
        processesByExe = (Group-Exe $own | Select-Object -First 8)
        otherProcessesSeen = $own.Count
        workerSideProcessesSeen = ($ps.Count - $own.Count)
        gitBySubcommand = (Group-Sub $cs)
    }
}

function Measure-Session([bool]$locks) {
    $mode = if ($locks) { 'locks on' } else { 'locks off' }
    Write-Host "scheduler ($mode): generating a pool of 2 projects"
    $root = Join-Path ([IO.Path]::GetTempPath()) ('ptk-perf-sched-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force $root | Out-Null
    $used = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | ForEach-Object { $_.Port })
    do { $port = Get-Random -Minimum 20000 -Maximum 45000 } while ($used -contains $port)
    $stub = Join-Path $root 'stub-worker.ps1'
    Write-Stub $stub $root $locks
    $names = New-PerfPool $root $port $locks $stub
    $p1 = $names[0]
    $la = Join-Path $root 'localappdata'; New-Item -ItemType Directory -Force $la | Out-Null
    $out = Join-Path $root 'scheduler.out'
    $steps = [ordered]@{}
    $c = Start-Counting "sched-$locks"
    $oldLocal = $env:LOCALAPPDATA; $env:LOCALAPPDATA = $la
    $t0 = Now-Ms
    $proc = Start-Process $Exe -ArgumentList '-Pool', ('"' + (Join-Path $root 'pool.json') + '"'), "-PoolName:ptkperf-$([guid]::NewGuid().ToString('N').Substring(0,6))", '-Port', $port, '-NoBrowser', '-Paused' -PassThru -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError "$out.err"
    $env:LOCALAPPDATA = $oldLocal
    try {
        # start: to the "start took" line, then until quiet
        $tStart = 0
        $dead = [DateTime]::UtcNow.AddSeconds(150)
        while ([DateTime]::UtcNow -lt $dead -and $tStart -eq 0) {
            Start-Sleep -Milliseconds 300
            if (Test-Path $out) { if (Select-String -Path $out -Pattern 'pool: start took' -Quiet) { $tStart = Now-Ms } }
        }
        if ($tStart -eq 0) { throw 'the scheduler did not report "pool: start took" in 150 s' }
        $tQuiet = Wait-Quiet $c $tStart
        $steps['start'] = @{ from = $t0; to = $tQuiet; toStartLine = $tStart }

        # idle: three polls of 10 s with nothing to do
        $a = Now-Ms; Start-Sleep -Seconds 30; $b = Now-Ms
        $steps['idle: 30 s, 3 polls of the pool'] = @{ from = $a; to = $b }

        # a click on the page: Tick now
        $a = Now-Ms; $code = Post-Page $port '/tick' $p1; $q = Wait-Quiet $c $a
        $steps["click: POST /tick (status $code)"] = @{ from = $a; to = $q }

        # a move-request: the developer's Done on the review card, as the page writes it
        $card = (Get-ChildItem (Join-Path $root "$p1\jobs\review") -Filter '*.md' | Select-Object -First 1)
        $moves = Join-Path $root "$p1-log\moves"; New-Item -ItemType Directory -Force $moves | Out-Null
        $txt = (Get-Content -Raw -Encoding UTF8 $card.FullName)
        $a = Now-Ms
        $f = Join-Path $moves ((Get-Date -Format yyyyMMdd-HHmmss-fff) + '-slot9')
        Set-Content -Encoding UTF8 -NoNewline -Path ($f + '.tmp') -Value ('Card: ' + $card.BaseName + [char]10 + 'To: done' + [char]10 + '---' + [char]10 + $txt)
        Move-Item ($f + '.tmp') ($f + '.move')
        $q = Wait-Quiet $c $a
        $steps['move request: one card review -> done'] = @{ from = $a; to = $q }

        # a planner batch (the switch on)
        $pj = Join-Path $root "$p1-log\planner-state.json"; Set-Content -Encoding UTF8 $pj '{"push":true}'
        $batch = Join-Path $root "$p1-outbox\$(Get-Date -Format yyyyMMdd-HHmmss)-perf"; New-Item -ItemType Directory -Force $batch | Out-Null
        Set-Content -Encoding UTF8 (Join-Path $batch 'f1.md') "# 9001-A: planner card`n`nCategory: A`nType: feature`nPlan: none`nStatus: ready`nPriority: normal`nDepends on: none`nScope: src/a/`nLocks: none`nTime box: 60 min`n"
        Set-Content -Encoding UTF8 (Join-Path $batch 'batch.json') '{"message":"jobs: perf card 9001-A","trailers":[],"ops":[{"op":"write","path":"jobs/queue/9001-A-perf.md","base":"new","file":"f1.md"}]}'
        $a = Now-Ms
        $until = [DateTime]::UtcNow.AddSeconds(120); $landed = $false
        while ([DateTime]::UtcNow -lt $until -and -not $landed) {
            Start-Sleep -Seconds 2
            $landed = [bool](& $script:RealGitExe -C (Join-Path $root "$p1-origin.git") ls-tree -r --name-only main 2>$null | Select-String '9001-A')
        }
        $q = Wait-Quiet $c $a
        $steps["planner batch: one card written$(if (-not $landed) { ' (NOT applied in 120 s)' })"] = @{ from = $a; to = $q }

        # a health check of main (Check main on the page)
        $a = Now-Ms; $code = Post-Page $port '/health-now' $p1; $q = Wait-Quiet $c $a
        $steps["health check: POST /health-now (status $code), command 'cmd /c exit 0'"] = @{ from = $a; to = $q }

        # a worker, claim to finish: resume the pool; each project has one queued card (a stub worker for each)
        $a = Now-Ms; $code = Post-Page $port '/resume' $p1
        $until = [DateTime]::UtcNow.AddSeconds(240); $done = 0
        while ([DateTime]::UtcNow -lt $until -and $done -lt 2) {
            Start-Sleep -Seconds 3
            $done = @($names | Where-Object { [bool](& $script:RealGitExe -C (Join-Path $root "$_-origin.git") ls-tree -r --name-only main 2>$null | Select-String 'jobs/done/1-B-') }).Count
        }
        $q = Wait-Quiet $c $a 8000 120
        $steps["worker claim -> finish, $done of 2 cards finished by their stub workers, $mode"] = @{ from = $a; to = $q; jobs = [Math]::Max(1, $done) }
    } finally {
        & taskkill /PID $proc.Id /T /F 2>$null | Out-Null
        Start-Sleep -Seconds 1
    }
    $r = Stop-Counting $c @($proc.Id)
    $rows = foreach ($k in $steps.Keys) {
        $s = Slice $r.Calls $r.Procs $steps[$k].from $steps[$k].to
        $s['step'] = $k; if ($steps[$k].ContainsKey('jobs')) { $s['jobs'] = $steps[$k].jobs }
        [pscustomobject]$s
    }
    $stderr = if (Test-Path "$out.err") { (Get-Content "$out.err" -Encoding UTF8 | Select-Object -First 3) -join ' | ' } else { '' }
    $notes = @()
    foreach ($n in $names) {
        $ld = Join-Path $root "$n-log"
        $sl = Join-Path $ld 'stub.log'
        if (Test-Path $sl) { $notes += (Get-Content $sl -Encoding UTF8 | Select-Object -Last 2) }
        foreach ($wl in (Get-ChildItem $ld -Filter '*-2026*.log' -ErrorAction SilentlyContinue | Select-Object -First 1)) { $notes += ("$($wl.Name): " + ((Get-Content $wl.FullName -Encoding UTF8 | Select-Object -Last 2) -join ' | ')) }
    }
    try { Remove-Item -Recurse -Force $root -ErrorAction Stop } catch { Write-Host "could not delete $root" }
    [ordered]@{ mode = $mode; steps = @($rows); stderr = $stderr; workerNotes = (@($notes) -join ' // ') }
}

function Measure-Scheduler {
    $sessions = @()
    $modes = switch ($Locks) { 'Off' { @($false) } 'On' { @($true) } default { @($false, $true) } }
    foreach ($locks in $modes) { $sessions += (Measure-Session $locks) }
    [ordered]@{ sessions = $sessions; remoteProbe = (Measure-RemoteProbe) }
}

# One read-only remote operation on this repository's real remote, to see what a remote operation starts besides git.
function Measure-RemoteProbe {
    $remote = (& git -C $repo remote get-url origin 2>$null | Out-String).Trim()
    $c = Start-Counting 'probe'
    $p = Start-Process 'git' -ArgumentList 'ls-remote', 'origin', 'HEAD' -WorkingDirectory $repo -PassThru -NoNewWindow -Wait -RedirectStandardOutput (Join-Path $work 'probe.out') -RedirectStandardError (Join-Path $work 'probe.err')
    $r = Stop-Counting $c @($p.Id)
    [ordered]@{ remote = ($remote -replace '//[^@/]*@', '//'); gitCallsByName = $r.Calls.Count; processesByExe = (Group-Exe ($r.Procs | Where-Object { $_.Name -notlike 'git.exe (the shim*' })) }
}

function Write-SchedulerReport($sc) {
    $s = New-Object Text.StringBuilder
    [void]$s.AppendLine(''); [void]$s.AppendLine('## 2. Scheduler: processes and time per step')
    [void]$s.AppendLine('')
    [void]$s.AppendLine('A temp pool of 2 generated projects (10 cards each, a bare origin each, `-Pool`/`-PoolName`, its own port, temp `%LOCALAPPDATA%`; the live pool is never touched), poll 10 s, started `-Paused` and resumed for the worker step. "git by name" is exact; "other processes" is the ProcWatch lower bound for what descends from the scheduler (stub workers'' own children are listed apart). The stub worker pulls, edits its card, pushes a code change, (locks on: asks for a lock with the lock command,) hands over its move request and prints `LANE-RESULT: done`; it runs git by absolute path so only the scheduler''s (and its hooks'') git is counted. Times are to the settled state: from the trigger to the last git call, or to the effect where one is waited for.')
    foreach ($sess in $sc.sessions) {
        [void]$s.AppendLine(''); [void]$s.AppendLine("### $($sess.mode)")
        [void]$s.AppendLine('')
        [void]$s.AppendLine('| Step | Seconds | git by name (exact) | Remote ops (fetch/push/pull/ls-remote) | Other processes seen | Worker-side processes seen |')
        [void]$s.AppendLine('|---|---|---|---|---|---|')
        foreach ($r in $sess.steps) {
            $per = if ($r.PSObject.Properties['jobs'] -and $r.jobs -gt 1) { " (= $([math]::Round($r.gitCallsByName / $r.jobs, 1)) per job)" } else { '' }
            [void]$s.AppendLine("| $($r.step) | $($r.seconds) | $($r.gitCallsByName)$per | $($r.remoteOperations) $(if ($r.remoteBySubcommand) { '(' + $r.remoteBySubcommand + ')' }) | $($r.otherProcessesSeen) | $($r.workerSideProcessesSeen) |")
        }
        foreach ($r in $sess.steps) {
            [void]$s.AppendLine('')
            [void]$s.AppendLine("- **$($r.step)**: git by subcommand: " + ((@($r.gitBySubcommand) | ForEach-Object { "$($_.subcommand) $($_.count)" }) -join ', ') + '; processes: ' + ((@($r.processesByExe) | ForEach-Object { "$($_.exe) $($_.count)" }) -join ', '))
        }
        if ($sess.workerNotes) { [void]$s.AppendLine(''); [void]$s.AppendLine("Stub worker notes: $($sess.workerNotes)") }
        if ($sess.stderr) { [void]$s.AppendLine(''); [void]$s.AppendLine("Scheduler stderr (first lines): $($sess.stderr)") }
    }
    $p = $sc.remoteProbe
    [void]$s.AppendLine(''); [void]$s.AppendLine('### What one remote operation starts')
    [void]$s.AppendLine('')
    [void]$s.AppendLine("`git ls-remote origin HEAD` on this repository (remote ``$($p.remote)``): $($p.gitCallsByName) git start by name; processes: " + ((@($p.processesByExe) | ForEach-Object { "$($_.exe) $($_.count)" }) -join ', ') + '. The temp pools'' remotes are local bare folders, so they start no credential helper; a real remote adds the helper starts shown here.')
    $s.ToString()
}

function Fmt-Range($xs) {
    $xs = @($xs)
    if ($xs.Count -eq 0) { return 'n/a' }
    $m = $xs | Measure-Object -Minimum -Maximum
    if ($m.Minimum -eq $m.Maximum) { return ('{0:0.0}' -f $m.Minimum) }
    return ('{0:0.0}-{1:0.0}' -f $m.Minimum, $m.Maximum)
}

function Format-Result([string]$r) { if ($r -match '(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+).*Total:\s+(\d+)') { "$($Matches[3]) passed, $($Matches[2]) failed of $($Matches[4])" } else { $r } }
function Write-Report($data, [string]$path) {
    $notesBegin = '<!-- notes:begin -->'; $notesEnd = '<!-- notes:end -->'
    $notes = "$notesBegin`n(no notes yet)`n$notesEnd"
    if (Test-Path $path) {
        $old = Get-Content -Raw -Encoding UTF8 $path
        $a = $old.IndexOf($notesBegin); $b = $old.IndexOf($notesEnd)
        if ($a -ge 0 -and $b -gt $a) { $notes = $old.Substring($a, $b + $notesEnd.Length - $a) }
    }
    $m = $data.meta
    $s = New-Object Text.StringBuilder
    [void]$s.AppendLine('# Git performance baseline')
    [void]$s.AppendLine('')
    [void]$s.AppendLine("Measured $($m.measured) on $($m.machine), scheduler-next $($m.version), $($m.git), at commit $($m.commit), $($m.threads) threads, with the scheduler pool and other workers running on the same PC (so times are noisy: read them as ranges).")
    [void]$s.AppendLine('Generated by `bin\git-baseline.ps1` (numbers in `test\perf\git-baseline.json`); the notes block below is kept when it is regenerated.')
    [void]$s.AppendLine('')
    [void]$s.AppendLine('## How it was measured')
    [void]$s.AppendLine('')
    [void]$s.AppendLine('No elevation, Sysmon or ETW is available to a worker, so processes are counted with two tools in `test\perf\`:')
    [void]$s.AppendLine('')
    [void]$s.AppendLine('- **GitShim** (`git.exe`, first on PATH): logs every start of `git` *by name* (time, pid, working folder, arguments) and runs the real git. **Exact count** of what the code under test starts as `git`; a start by absolute path is not seen.')
    [void]$s.AppendLine('- **ProcWatch**: snapshots the process table every 10 ms with each process''s parent; the script keeps the processes below the measured command and tells the git.exe copies apart by lineage (shim -> `cmd\git.exe` wrapper -> `mingw64\bin\git.exe` -> started by git). A process that lives under about 10 ms can be missed: **a lower bound**.')
    [void]$s.AppendLine('- **Times** come from separate runs with neither tool running (they cost time), a range over the runs listed.')
    [void]$s.AppendLine('')
    [void]$s.AppendLine($notes)
    [void]$s.AppendLine('')
    if ($data.PSObject.Properties['tests']) {
        [void]$s.AppendLine('## 1. Test runs')
        [void]$s.AppendLine('')
        [void]$s.AppendLine('| Project | Result (last timed run) | Wall time, s (range of runs) | git starts by name (exact) | Other processes seen (lower bound) | Failed in the counted run |')
        [void]$s.AppendLine('|---|---|---|---|---|---|')
        foreach ($t in $data.tests) {
            [void]$s.AppendLine("| $($t.project) | $(Format-Result $t.result) | $(Fmt-Range $t.seconds) ($(@($t.seconds).Count) runs) | $($t.gitCallsByName) | $($t.otherProcessesSeen) | $($t.failedCount) |")
        }
        foreach ($t in $data.tests) {
            [void]$s.AppendLine('')
            [void]$s.AppendLine("### $($t.project): processes by executable (counted run, $(if ($t.countedSerial) { 'one test at a time' } else { 'parallel' }), $('{0:0}' -f $t.countedRunSeconds) s)")
            [void]$s.AppendLine('')
            [void]$s.AppendLine('| Executable | Starts |'); [void]$s.AppendLine('|---|---|')
            foreach ($e in @($t.processesByExe | Select-Object -First 12)) { [void]$s.AppendLine("| $($e.exe) | $($e.count) |") }
            [void]$s.AppendLine('')
            [void]$s.AppendLine("git by subcommand (exact): " + ((@($t.gitBySubcommand) | ForEach-Object { "$($_.subcommand) $($_.count)" }) -join ', '))
            if ($t.failedCount -gt 0) {
                [void]$s.AppendLine('')
                [void]$s.AppendLine("Failures in the counted run ($($t.failedCount)); first: " + ((@($t.failedExamples) | Select-Object -First 2 | ForEach-Object { "``$($_.test)``: $($_.message)" }) -join ' / '))
            }
        }
        [void]$s.AppendLine('')
        [void]$s.AppendLine('### Where the tests create their repositories (working folders of the git calls)')
        [void]$s.AppendLine('')
        [void]$s.AppendLine('| Project | Folder | git calls | Distinct working folders | Outside C:\Projects (Defender scans it) |')
        [void]$s.AppendLine('|---|---|---|---|---|')
        foreach ($t in $data.tests) { foreach ($a in @($t.tempRepoFolders | Select-Object -First 8)) { [void]$s.AppendLine("| $($t.project) | ``$($a.folder)`` | $($a.gitCalls) | $($a.distinctWorkingFolders) | $(if ($a.outsideProjects) { 'yes' } else { 'no' }) |") } }
        foreach ($t in $data.tests) {
            if (@($t.topTests).Count -gt 0) {
                [void]$s.AppendLine('')
                [void]$s.AppendLine("### $($t.project): the 20 tests that start the most processes (serial counted run)")
                [void]$s.AppendLine('')
                [void]$s.AppendLine('| Test | git by name | Other processes seen | Seconds |'); [void]$s.AppendLine('|---|---|---|---|')
                foreach ($x in $t.topTests) { [void]$s.AppendLine("| $($x.test) | $($x.gitCallsByName) | $($x.otherProcessesSeen) | $($x.seconds) |") }
            }
        }
    }
    if ($data.PSObject.Properties['scheduler']) { [void]$s.Append((Write-SchedulerReport $data.scheduler)) }
    Set-Content -Encoding UTF8 -LiteralPath $path -Value $s.ToString()
}

# ---- main (skipped when PTK_BASELINE_LIB is set, so the functions can be loaded and tried one by one) ----
if (-not $env:PTK_BASELINE_LIB) {
    Build-Tool 'GitShim'; Build-Tool 'ProcWatch'; Build-Tool 'PoolMaker'
    $data = if (Test-Path $jsonFile) { Get-Content -Raw -Encoding UTF8 $jsonFile | ConvertFrom-Json } else { [pscustomobject]@{} }
    $meta = [ordered]@{
        measured = (Get-Date -Format 'yyyy-MM-dd HH:mm'); machine = $env:COMPUTERNAME
        version = (& $Exe --version 2>&1 | Out-String).Trim(); git = (& $realGit --version).Trim(); threads = $jobs
        commit = (& git -C $repo rev-parse --short HEAD).Trim()
    }
    $data | Add-Member -NotePropertyName meta -NotePropertyValue $meta -Force
    New-Item -ItemType Directory -Force -Path (Split-Path $jsonFile) | Out-Null

    if ($Part -in 'Tests', 'All') {
        $existing = @{}
        if ($data.PSObject.Properties['tests']) { foreach ($t in $data.tests) { $existing[$t.project] = $t } }
        foreach ($p in $Projects) { $existing[$p] = Measure-TestProject $p }
        $ordered = @('Core', 'Web', 'Tray', 'Host') | Where-Object { $existing.ContainsKey($_) } | ForEach-Object { $existing[$_] }
        $data | Add-Member -NotePropertyName tests -NotePropertyValue @($ordered) -Force
        $data | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $jsonFile
    }

    if ($Part -in 'Scheduler', 'All') {
        $data | Add-Member -NotePropertyName scheduler -NotePropertyValue (Measure-Scheduler) -Force
        $data | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $jsonFile
    }

    Write-Report $data $mdFile
    Write-Host "git baseline: wrote $jsonFile and $mdFile"
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
