<#
.SYNOPSIS
  Which operation of a test run leaves kernel paged pool behind? One probe = one kind of work, measured (card 1159-B).
.DESCRIPTION
  Run with pwsh (7), from a quiet PC: no other worker, no build, no test run. Each -Probe reads
  \Memory\Pool Paged Bytes, \Memory\Pool Nonpaged Bytes and the ended-but-held process count
  (\Objects\Processes minus the processes running now), does ONE kind of work, reads again straight after, waits
  60 seconds and reads a third time, then appends one JSON line to <OutDir>\probe.jsonl: probe, run, units of work,
  before, afterWork, after, differences (the "after" reading minus "before": what was left behind a minute after the
  work), seconds. Run every probe twice (-Run 1, -Run 2); a difference under 3 MB is nothing.

    drift            nothing for 3 minutes (the PC's own drift)
    build            dotnet build code\src\SchedulerNext.slnx -warnaserror -m:$PTK_JOBS --no-incremental
    host:<Folder>    dotnet test ...Scheduler.Host.Tests.csproj --no-build --filter FullyQualifiedName~Scheduler.Host.Tests.<Folder>
    core | web | tray   the same for Scheduler.Core.Tests, Scheduler.Web.Tests, Scheduler.Tray.Tests (whole project)
    files            create and delete 20,000 small files in 200 folders under out\test-tmp
    gitinit          git init, one commit, delete: 200 repositories under out\test-tmp
    gitfsmon         the same 200 with core.fsmonitor=true and one git status in each; then
                     `git fsmonitor--daemon stop` in each (only the daemons of these repositories) and delete
    gitinit-only     200 x git init, then delete (no commit)
    gitinit-keep     200 x init + one commit, NOT deleted; gitinit-del then deletes them (creation apart from deletion)
    gitcommits       600 x (write a file, git add, git commit) in one repository, then delete
    gitop:<op>       600 x one git command in one repository of 100 files: add | commit | hash | status |
                     commit-nofsync (core.fsync=none) | commit-nogc (gc.auto=0, maintenance.auto=false)
    rename           20,000 files written as x.lock, set read-only and renamed to x (git's lock-file pattern), deleted
    worktree         git worktree add and remove, 100 times, on one temp repository
    clonepush        clone a local bare repository, commit and push to it, 100 times
    cmd-nowindow     2,000 starts of `cmd /c exit` with CreateNoWindow (a new console host each)
    cmd-inherit      2,000 starts of `cmd /c exit` inheriting the console
    workers          500 stub workers started and ended the way TickRig does it: the product's own
                     Scheduler.Host.Slots.WorkerProcesses (a cmd in a job object, running ping), loaded from the
                     build, in batches of 100
    check            bin\process-start-check.ps1 once (use at the start and at the end of a series)
    report           print probe.jsonl as a markdown table sorted by paged pool left behind (no measuring)

  How the plain operations are sized: the counts are the card's (they are about what one full test run of the
  heaviest folders does: many small files, hundreds of temp repositories, hundreds of worker stubs). The test run
  itself is recorded with the number of tests it ran, so per-test figures can be set against per-file and
  per-repository ones.

  Changes no product code and no machine setting. Everything it creates is under out\test-tmp\probe-* and removed.
  The only processes it ends are its own stub workers (through their job objects) and the fsmonitor daemons of its
  own temp repositories.
.PARAMETER Probe
  Probe name, as above.
.PARAMETER Run
  1 or 2 (a label in the JSON line). Default 1.
.PARAMETER OutDir
  Where probe.jsonl is written. Default: out\paged-pool-probe under the repository.
.PARAMETER Repo
  The worktree to build and test. Default: the repository this script is in.
.PARAMETER Minutes
  Length of the drift probe. Default 3.
.PARAMETER WaitSeconds
  The wait between the work and the last reading. Default 60 (the card's figure).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Probe,
    [int]$Run = 1,
    [string]$OutDir,
    [string]$Repo,
    [int]$Minutes = 3,
    [int]$WaitSeconds = 60
)
$ErrorActionPreference = 'Stop'
if (-not $Repo) { $Repo = Split-Path -Parent $PSScriptRoot }
if (-not $OutDir) { $OutDir = Join-Path $Repo 'out\paged-pool-probe' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$jsonl = Join-Path $OutDir 'probe.jsonl'
$jobs = if ($env:PTK_JOBS) { $env:PTK_JOBS } else { '6' }
$git = 'C:\Program Files\Git\cmd\git.exe'
if (-not (Test-Path -LiteralPath $git)) { $git = 'git' }
$tmpRoot = Join-Path $Repo 'out\test-tmp'

function Get-Reading {
    $c = (Get-Counter '\Memory\Pool Paged Bytes', '\Memory\Pool Nonpaged Bytes', '\Objects\Processes').CounterSamples
    $running = @(Get-Process).Count
    [pscustomobject]@{
        pagedMB    = [math]::Round(($c | Where-Object Path -like '*pool paged bytes').CookedValue / 1MB, 1)
        nonpagedMB = [math]::Round(($c | Where-Object Path -like '*pool nonpaged bytes').CookedValue / 1MB, 1)
        held       = [int]((($c | Where-Object Path -like '*objects(*processes').CookedValue) - $running)
    }
}

# git with no prompts and no user settings in the way; output dropped, exit code ignored (a probe must not die of one).
function Invoke-Git([string]$dir, [string[]]$gitArgs) {
    & $git -C $dir -c user.name=probe -c user.email=probe@example.invalid -c commit.gpgsign=false @gitArgs *> $null
}

function New-TempDir([string]$name) {
    $d = Join-Path $tmpRoot $name
    if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    $d
}

function Remove-TempDir([string]$d) {
    # git objects are read-only: cmd rd removes them where Remove-Item can balk.
    if (Test-Path -LiteralPath $d) { & cmd.exe /d /c rd /s /q "`"$d`"" *> $null }
}

function Invoke-Tests([string]$project, [string]$filter) {
    $csproj = Get-ChildItem -Path (Join-Path $Repo "test\unit\$project") -Filter *.csproj | Select-Object -First 1
    $a = @('test', $csproj.FullName, '--no-build', "-m:$jobs")
    if ($filter) { $a += @('--filter', $filter) }
    $log = Join-Path $OutDir ("test-{0}.log" -f ($Probe -replace '[^\w]', '_'))
    & dotnet @a *> $log
    $line = Select-String -Path $log -Pattern 'Passed!|Failed!' | Select-Object -Last 1
    $n = 0
    if ($line -and $line.Line -match 'Passed:\s*(\d+)') { $n += [int]$Matches[1] }
    if ($line -and $line.Line -match 'Failed:\s*(\d+)') { $n += [int]$Matches[1] }
    [pscustomobject]@{ units = $n; unit = 'tests'; note = if ($line) { $line.Line.Trim() } else { 'no summary line: see ' + $log } }
}

# The work of each probe: returns units, unit and an optional note.
function Invoke-Work {
    switch -Wildcard ($Probe) {
        'drift' { Start-Sleep -Seconds ($Minutes * 60); return [pscustomobject]@{ units = $Minutes; unit = 'minutes'; note = '' } }
        'build' {
            $log = Join-Path $OutDir 'build.log'
            & dotnet build (Join-Path $Repo 'code\src\SchedulerNext.slnx') -warnaserror "-m:$jobs" --no-incremental *> $log
            $e = Select-String -Path $log -Pattern '\d+ Error\(s\)' | Select-Object -Last 1
            return [pscustomobject]@{ units = 1; unit = 'builds'; note = if ($e) { $e.Line.Trim() } else { '' } }
        }
        'host:*' {
            $f = $Probe.Substring(5)
            return Invoke-Tests 'Scheduler.Host.Tests' "FullyQualifiedName~Scheduler.Host.Tests.$f"
        }
        'core' { return Invoke-Tests 'Scheduler.Core.Tests' $null }
        'web' { return Invoke-Tests 'Scheduler.Web.Tests' $null }
        'tray' { return Invoke-Tests 'Scheduler.Tray.Tests' $null }
        'files' {
            $d = New-TempDir 'probe-files'
            foreach ($i in 1..200) {
                $sub = Join-Path $d "dir$i"
                [void][IO.Directory]::CreateDirectory($sub)
                foreach ($j in 1..100) { [IO.File]::WriteAllText((Join-Path $sub "f$j.txt"), "probe $i $j") }
            }
            Remove-TempDir $d
            return [pscustomobject]@{ units = 20000; unit = 'files'; note = '200 folders x 100 files, created and deleted' }
        }
        { $_ -in 'gitinit', 'gitfsmon' } {
            $fs = $Probe -eq 'gitfsmon'
            $d = New-TempDir "probe-$Probe"
            foreach ($i in 1..200) {
                $r = Join-Path $d "r$i"
                [void][IO.Directory]::CreateDirectory($r)
                Invoke-Git $r @('init', '-q')
                if ($fs) { Invoke-Git $r @('config', 'core.fsmonitor', 'true') }
                [IO.File]::WriteAllText((Join-Path $r 'a.txt'), "probe $i")
                Invoke-Git $r @('add', '.')
                Invoke-Git $r @('commit', '-q', '-m', 'probe')
                if ($fs) { Invoke-Git $r @('status', '--short') }
            }
            $daemons = 0
            if ($fs) {
                $daemons = @(Get-CimInstance Win32_Process -Filter "Name='git.exe'" | Where-Object { $_.CommandLine -like '*fsmonitor--daemon*' }).Count
                foreach ($i in 1..200) { Invoke-Git (Join-Path $d "r$i") @('fsmonitor--daemon', 'stop') }
            }
            Remove-TempDir $d
            return [pscustomobject]@{ units = 200; unit = 'repositories'; note = if ($fs) { "fsmonitor daemons running before they were stopped (all git, not only these): $daemons" } else { '' } }
        }
        'gitinit-only' {
            $d = New-TempDir 'probe-gitinit-only'
            foreach ($i in 1..200) { $r = Join-Path $d "r$i"; [void][IO.Directory]::CreateDirectory($r); Invoke-Git $r @('init', '-q') }
            Remove-TempDir $d
            return [pscustomobject]@{ units = 200; unit = 'repositories'; note = 'git init only, then delete' }
        }
        'gitinit-keep' {
            # Creates and commits like gitinit but does NOT delete: tells creation from deletion. Run gitinit-del after it.
            $d = New-TempDir 'probe-gitinit-keep'
            foreach ($i in 1..200) {
                $r = Join-Path $d "r$i"
                [void][IO.Directory]::CreateDirectory($r)
                Invoke-Git $r @('init', '-q')
                [IO.File]::WriteAllText((Join-Path $r 'a.txt'), "probe $i")
                Invoke-Git $r @('add', '.')
                Invoke-Git $r @('commit', '-q', '-m', 'probe')
            }
            return [pscustomobject]@{ units = 200; unit = 'repositories'; note = 'init+commit, kept (not deleted)' }
        }
        'gitinit-del' {
            Remove-TempDir (Join-Path $tmpRoot 'probe-gitinit-keep')
            return [pscustomobject]@{ units = 200; unit = 'repositories'; note = 'delete of the 200 repositories left by gitinit-keep (no git runs)' }
        }
        'gitcommits' {
            $d = New-TempDir 'probe-gitcommits'
            Invoke-Git $d @('init', '-q')
            foreach ($i in 1..600) {
                [IO.File]::WriteAllText((Join-Path $d "f$i.txt"), "probe $i")
                Invoke-Git $d @('add', '.')
                Invoke-Git $d @('commit', '-q', '-m', "c$i")
            }
            Remove-TempDir $d
            return [pscustomobject]@{ units = 600; unit = 'commits'; note = 'add+commit in one repository (1200 git starts), then delete' }
        }
        'gitop:*' {
            # One git command, 600 times in one repository: which part of add+commit leaves the pool?
            $op = $Probe.Substring(6)
            $d = New-TempDir "probe-gitop-$op"
            Invoke-Git $d @('init', '-q')
            foreach ($i in 1..100) { [IO.File]::WriteAllText((Join-Path $d "f$i.txt"), "probe $i") }
            Invoke-Git $d @('add', '.')
            Invoke-Git $d @('commit', '-q', '-m', 'seed')
            foreach ($i in 1..600) {
                switch ($op) {
                    'hash' { [IO.File]::WriteAllText((Join-Path $d 'h.txt'), "probe $i"); Invoke-Git $d @('hash-object', '-w', 'h.txt') }
                    'add' { [IO.File]::WriteAllText((Join-Path $d 'f1.txt'), "probe $i"); Invoke-Git $d @('add', 'f1.txt') }
                    'commit' { Invoke-Git $d @('commit', '-q', '--allow-empty', '-m', "c$i") }
                    # commit with the file-flush settings off (core.fsync=none), and with automatic gc / maintenance off
                    'commit-nofsync' { Invoke-Git $d @('-c', 'core.fsync=none', '-c', 'core.fsyncMethod=writeout-only', 'commit', '-q', '--allow-empty', '-m', "c$i") }
                    'commit-nogc' { Invoke-Git $d @('-c', 'gc.auto=0', '-c', 'maintenance.auto=false', 'commit', '-q', '--allow-empty', '-m', "c$i") }
                    'status' { Invoke-Git $d @('status', '--short') }
                    default { throw "unknown gitop '$op'" }
                }
            }
            Remove-TempDir $d
            return [pscustomobject]@{ units = 600; unit = "git $op"; note = "600 x git $op in one repository (100 tracked files), then delete" }
        }
        'rename' {
            # The lock-file pattern git uses: write x.lock, set read-only, rename over x, then delete (clearing read-only).
            $d = New-TempDir 'probe-rename'
            foreach ($i in 1..200) {
                $sub = Join-Path $d "dir$i"
                [void][IO.Directory]::CreateDirectory($sub)
                foreach ($j in 1..100) {
                    $f = Join-Path $sub "f$j"
                    [IO.File]::WriteAllText("$f.lock", "probe $i $j")
                    [IO.File]::SetAttributes("$f.lock", [IO.FileAttributes]::ReadOnly)
                    [IO.File]::Move("$f.lock", $f)
                }
            }
            Remove-TempDir $d
            return [pscustomobject]@{ units = 20000; unit = 'files'; note = 'create .lock, read-only, rename, delete' }
        }
        'worktree' {
            $d = New-TempDir 'probe-worktree'
            $r = Join-Path $d 'main'
            [void][IO.Directory]::CreateDirectory($r)
            Invoke-Git $r @('init', '-q')
            [IO.File]::WriteAllText((Join-Path $r 'a.txt'), 'probe')
            Invoke-Git $r @('add', '.')
            Invoke-Git $r @('commit', '-q', '-m', 'probe')
            foreach ($i in 1..100) {
                $w = Join-Path $d "wt$i"
                Invoke-Git $r @('worktree', 'add', '-q', $w, '-b', "b$i")
                Invoke-Git $r @('worktree', 'remove', '--force', $w)
            }
            Remove-TempDir $d
            return [pscustomobject]@{ units = 100; unit = 'worktree add+remove'; note = '' }
        }
        'clonepush' {
            $d = New-TempDir 'probe-clonepush'
            $bare = Join-Path $d 'origin.git'
            & $git init -q --bare $bare *> $null
            $seed = Join-Path $d 'seed'
            [void][IO.Directory]::CreateDirectory($seed)
            Invoke-Git $seed @('init', '-q', '-b', 'main')
            [IO.File]::WriteAllText((Join-Path $seed 'a.txt'), 'probe')
            Invoke-Git $seed @('add', '.')
            Invoke-Git $seed @('commit', '-q', '-m', 'seed')
            Invoke-Git $seed @('push', '-q', $bare, 'main')
            foreach ($i in 1..100) {
                $c = Join-Path $d "c$i"
                & $git clone -q $bare $c *> $null
                [IO.File]::WriteAllText((Join-Path $c "n$i.txt"), "probe $i")
                Invoke-Git $c @('add', '.')
                Invoke-Git $c @('commit', '-q', '-m', "n$i")
                Invoke-Git $c @('push', '-q', 'origin', 'HEAD:refs/heads/main')
                Invoke-Git $c @('pull', '-q', '--rebase', 'origin', 'main')
            }
            Remove-TempDir $d
            return [pscustomobject]@{ units = 100; unit = 'clone+commit+push'; note = '' }
        }
        { $_ -in 'cmd-nowindow', 'cmd-inherit' } {
            $nw = $Probe -eq 'cmd-nowindow'
            foreach ($i in 1..2000) {
                $psi = [Diagnostics.ProcessStartInfo]::new($env:ComSpec, '/d /c exit')
                $psi.UseShellExecute = $false
                $psi.CreateNoWindow = $nw
                $p = [Diagnostics.Process]::Start($psi)
                $p.WaitForExit()
                $p.Dispose()
            }
            return [pscustomobject]@{ units = 2000; unit = 'cmd starts'; note = if ($nw) { 'CreateNoWindow' } else { 'inherits the console' } }
        }
        'workers' {
            $d = Join-Path $Repo 'build\bin\Scheduler.Host\debug'
            Add-Type -Path (Join-Path $d 'Scheduler.Core.dll')
            Add-Type -Path (Join-Path $d 'scheduler-next.dll')
            $wp = [Scheduler.Host.Slots.WorkerProcesses]::new([Scheduler.Host.Slots.JobObjects]::new())
            $env0 = [Collections.Generic.Dictionary[string, string]]::new()
            $started = 0
            foreach ($batch in 1..5) {
                $live = @()
                foreach ($i in 1..100) {
                    $name = "ProjectToolkit-probe-worker-$Run-$batch-$i-{pid}"
                    $s = $wp.Start('ping -n 600 127.0.0.1 >nul', $name, $env:TEMP, $env0)
                    if ($s) { $live += [pscustomobject]@{ name = $name; pid = $s.Pid }; $started++ }
                }
                foreach ($l in $live) { $wp.EndJob($l.name, $l.pid) }
            }
            return [pscustomobject]@{ units = $started; unit = 'workers'; note = 'WorkerProcesses.Start then EndJob, batches of 100' }
        }
        'check' {
            $out = & (Join-Path $PSScriptRoot 'process-start-check.ps1') *>&1 | Out-String
            $m = [regex]::Match($out, 'median:\s*([\d,]+)')
            return [pscustomobject]@{ units = 5; unit = 'rounds'; note = 'median starts/s: ' + $m.Groups[1].Value }
        }
        default { throw "unknown probe '$Probe'" }
    }
}

if ($Probe -eq 'report') {
    $rows = Get-Content -Encoding UTF8 $jsonl | ForEach-Object { $_ | ConvertFrom-Json }
    '| Probe | Run | Work | Units | Paged left MB (after 60 s) | Paged right after MB | Nonpaged MB | Held procs | KB paged per unit | Note |'
    '|---|---|---|---|---|---|---|---|---|---|'
    foreach ($r in ($rows | Sort-Object { [double]$_.diff.pagedMB } -Descending)) {
        $per = if ($r.units -gt 0) { '{0:N1}' -f ($r.diff.pagedMB * 1024 / $r.units) } else { '' }
        '| {0} | {1} | {2:N0} s | {3} {4} | {5} | {6} | {7} | {8} | {9} | {10} |' -f $r.probe, $r.run, $r.workSeconds, $r.units, $r.unit,
        $r.diff.pagedMB, $r.diffAfterWork.pagedMB, $r.diff.nonpagedMB, $r.diff.held, $per, $r.note
    }
    return
}

$before = Get-Reading
$sw = [Diagnostics.Stopwatch]::StartNew()
$w = Invoke-Work
$sw.Stop()
$afterWork = Get-Reading
Start-Sleep -Seconds $WaitSeconds
$after = Get-Reading
function Get-Diff($a, $b) {
    [pscustomobject]@{ pagedMB = [math]::Round($b.pagedMB - $a.pagedMB, 1); nonpagedMB = [math]::Round($b.nonpagedMB - $a.nonpagedMB, 1); held = $b.held - $a.held }
}
$line = [ordered]@{
    time          = (Get-Date).ToString('s')
    probe         = $Probe
    run           = $Run
    units         = $w.units
    unit          = $w.unit
    workSeconds   = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    waitSeconds   = $WaitSeconds
    before        = $before
    afterWork     = $afterWork
    after         = $after
    diff          = Get-Diff $before $after
    diffAfterWork = Get-Diff $before $afterWork
    note          = $w.note
}
($line | ConvertTo-Json -Compress -Depth 5) | Add-Content -Encoding UTF8 -Path $jsonl
'{0} {1} run {2}: {3} {4} in {5:N0} s; paged {6} -> {7} MB ({8:+0.0;-0.0} MB), nonpaged {9:+0.0;-0.0} MB, held {10:+0;-0}  {11}' -f (Get-Date -Format 'HH:mm:ss'),
$Probe, $Run, $w.units, $w.unit, $sw.Elapsed.TotalSeconds, $before.pagedMB, $after.pagedMB, $line.diff.pagedMB, $line.diff.nonpagedMB, $line.diff.held, $w.note
