# live-stream-measure.ps1 - 1104-B: measure what the live stream (1101-B/1102-C/1103-C) costs.
#
# It drives the page server of a RUNNING pool as a browser would, and for each scenario reports the
# scheduler process's CPU, the requests per second it makes to the page server, the frames per second
# it receives, and the tick times the pool log's "tick took" lines give over the same window.
#
# Scenarios (each -Seconds long):
#   none     no page open (the baseline; the tray still polls occasionally)
#   stream1  one page on the stream   (GET /live/stream, Server-Sent Events)
#   stream3  three pages on the stream
#   poll1    one page forced to the polling fallback (GET /live.json every -PollSeconds)
#   poll3    three pages on the polling fallback
#
# Nothing here changes the product: no pool is started, no card is moved, no file outside the -Csv
# path is written. The clients are plain TCP sockets that speak HTTP/1.1 the way PageServer reads it.
#
# Usage (from the repository root):
#   powershell -File bin\live-stream-measure.ps1                       # all four, 120 s each
#   powershell -File bin\live-stream-measure.ps1 -Scenarios none,stream1 -Seconds 60
#   powershell -File bin\live-stream-measure.ps1 -Action report -Csv <file>   # the markdown table
#
# Reference readings are in docs/perf/live-stream.md.

[CmdletBinding()]
param(
    [ValidateSet('measure', 'report')]
    [string] $Action = 'measure',

    # The pool's page server. 0 means read "port" from <Repo>\jobs\scheduler.json.
    [int] $Port = 0,
    [string] $Repo = 'C:\Projects\ProjectToolkit',

    # Where the pool writes its one shared log (the "tick took" lines are read from its tail).
    [string] $PoolLog = '',

    [string[]] $Scenarios = @('none', 'stream1', 'stream3', 'poll1'),
    [int] $Seconds = 120,
    [int] $PollSeconds = 2,

    # The scheduler process to weigh. The pool runs as scheduler-next.
    [string] $ProcessName = 'scheduler-next',

    [string] $Csv = (Join-Path $env:TEMP ('live-stream-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.csv')),

    # Safety: the whole run gives up after this many seconds (0 = no cap).
    [int] $TimeoutSec = 0
)

$ErrorActionPreference = 'Stop'
$started = Get-Date

# ---------------------------------------------------------------- the pool page server's address ----

function Resolve-Port {
    param([string] $Repo, [int] $Port)
    if ($Port -gt 0) { return $Port }
    $file = Join-Path (Join-Path $Repo 'jobs') 'scheduler.json'
    if (-not (Test-Path $file)) { throw "no jobs/scheduler.json under $Repo; pass -Port" }
    $json = Get-Content -Raw -Encoding UTF8 $file | ConvertFrom-Json
    if (-not $json.port) { throw "jobs/scheduler.json has no port; pass -Port" }
    return [int] $json.port
}

function Resolve-PoolLog {
    param([string] $Repo, [string] $PoolLog)
    if ($PoolLog) { return $PoolLog }
    # -Pool writes the one shared log beside the pool's state folder.
    return (Join-Path $env:LOCALAPPDATA (Join-Path 'ProjectToolkit' (Join-Path 'pool' 'scheduler.log')))
}

# ------------------------------------------------------------- the scheduler process's CPU ----

function Get-ProcSample {
    param([string] $Name)
    $cpu = 0.0
    $mem = 0.0
    $threads = 0
    $handles = 0
    $found = 0
    foreach ($p in (Get-Process -Name $Name -ErrorAction SilentlyContinue)) {
        $found++
        try { $cpu += $p.TotalProcessorTime.TotalSeconds } catch { }
        try { $mem += $p.WorkingSet64 / 1MB } catch { }
        try { $threads += $p.Threads.Count } catch { }
        try { $handles += $p.HandleCount } catch { }
    }
    return [pscustomobject]@{
        Found   = $found
        CpuSecs = $cpu
        MemMb   = [math]::Round($mem, 1)
        Threads = $threads
        Handles = $handles
    }
}

function Get-MachineLoad {
    # Percent of all cores busy, host-wide, from the OS counter. Null when the counter is missing.
    try {
        $v = (Get-CimInstance -ClassName Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'" -ErrorAction Stop).PercentProcessorTime
        if ($null -eq $v) { return $null }
        return [double] $v
    } catch { return $null }
}

function Get-WorkerCount {
    # How many agent sessions are running right now: the worker CLIs the pool starts.
    $names = @('copilot', 'claude', 'node')
    $n = 0
    foreach ($name in $names) {
        foreach ($p in (Get-Process -Name $name -ErrorAction SilentlyContinue)) { $n++ }
    }
    return $n
}

# ------------------------------------------------------------------- the pool log's tick times ----

function Read-TickLines {
    # The "tick took N.NNs" lines in a window, read from the tail of the log only: the pool log is
    # gigabytes and is never scanned from the start.
    param([string] $Path, [datetime] $From, [datetime] $To, [int] $MaxBytes = 40MB)
    $out = @()
    if (-not (Test-Path $Path)) { return $out }
    try {
        $fs = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    } catch { return $out }
    try {
        $take = [math]::Min([long] $MaxBytes, $fs.Length)
        $fs.Seek(-$take, [System.IO.SeekOrigin]::End) | Out-Null
        $reader = [System.IO.StreamReader]::new($fs, [System.Text.Encoding]::UTF8)
        $first = $true
        while (-not $reader.EndOfStream) {
            $line = $reader.ReadLine()
            if ($first) { $first = $false; continue }   # the tail may start mid-line
            if ($line -notmatch 'tick took ') { continue }
            $when = [regex]::Match($line, '^(\d{2}):(\d{2}):(\d{2})')
            if (-not $when.Success) { continue }
            $stamp = [datetime]::new($From.Year, $From.Month, $From.Day, [int] $when.Groups[1].Value, [int] $when.Groups[2].Value, [int] $when.Groups[3].Value)
            if ($stamp -lt $From.AddSeconds(-2) -or $stamp -gt $To.AddSeconds(2)) { continue }
            $took = [regex]::Match($line, 'tick took ([\d.]+)s')
            $longest = [regex]::Match($line, 'longest ([\d.]+)s')
            $slowest = [regex]::Match($line, 'slowest step ([A-Za-z0-9_.-]+) ([\d.]+)s')
            $out += [pscustomobject]@{
                Time    = $stamp.ToString('HH:mm:ss')
                Took    = [double] $took.Groups[1].Value
                Longest = if ($longest.Success) { [double] $longest.Groups[1].Value } else { 0.0 }
                Step    = if ($slowest.Success) { $slowest.Groups[1].Value } else { '' }
                StepSec = if ($slowest.Success) { [double] $slowest.Groups[2].Value } else { 0.0 }
            }
        }
        $reader.Dispose()
    } finally {
        if ($fs) { $fs.Dispose() }
    }
    return $out
}

# ------------------------------------------------------------------------- the page clients ----

$StreamClient = {
    param($port, $secs, $path, $outFile)
    $frames = 0
    $keeps = 0
    $bytes = 0
    $perSec = New-Object 'System.Collections.Generic.List[int]'
    $headMs = -1
    $reconnects = 0
    $err = ''
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $secs) {
        $client = New-Object System.Net.Sockets.TcpClient
        try {
            $client.Connect('127.0.0.1', $port)
            $ns = $client.GetStream()
            $ns.ReadTimeout = 5000
            $req = "GET $path HTTP/1.1`r`nHost: 127.0.0.1`r`nAccept: text/event-stream`r`nCache-Control: no-cache`r`nConnection: close`r`n`r`n"
            $raw = [System.Text.Encoding]::ASCII.GetBytes($req)
            $ns.Write($raw, 0, $raw.Length)
            $buf = New-Object byte[] 65536
            $headDone = $false
            $tail = ''
            while ($sw.Elapsed.TotalSeconds -lt $secs) {
                $n = 0
                try { $n = $ns.Read($buf, 0, $buf.Length) } catch { $n = 0 }
                if ($n -le 0) { break }
                if ($headMs -lt 0 -and -not $headDone) { $headMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 1) }
                $headDone = $true
                $bytes += $n
                $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
                $probe = $tail + $text
                $cut = $probe.LastIndexOf("`n")
                if ($cut -lt 0) { $tail = $probe; continue }
                $tail = $probe.Substring($cut + 1)
                $complete = $probe.Substring(0, $cut + 1)
                $mine = 0
                foreach ($line in ($complete -split "`n")) {
                    if ($line.StartsWith('id: ')) { $mine++ }
                    elseif ($line.StartsWith(': keep-alive')) { $keeps++ }
                }
                if ($mine -gt 0) {
                    $frames += $mine
                    $sec = [int] [math]::Floor($sw.Elapsed.TotalSeconds)
                    while ($perSec.Count -le $sec) { $perSec.Add(0) }
                    $perSec[$sec] = $perSec[$sec] + $mine
                }
            }
            while ($perSec.Count -le [int] [math]::Floor($sw.Elapsed.TotalSeconds)) { $perSec.Add(0) }
        } catch {
            $err = $_.Exception.Message
        } finally {
            if ($client) { $client.Close() }
        }
        if ($sw.Elapsed.TotalSeconds -lt $secs) {
            $reconnects++
            Start-Sleep -Milliseconds 500
        }
    }
    $obj = [pscustomobject]@{
        Frames     = $frames
        Keeps      = $keeps
        Bytes      = $bytes
        HeadMs     = $headMs
        Reconnects = $reconnects
        Err        = $err
        PerSec     = ($perSec -join ',')
    }
    $obj | ConvertTo-Json -Compress | Set-Content -Encoding UTF8 -NoNewline -Path $outFile
}

$PollClient = {
    param($port, $secs, $pollSec, $outFile)
    $requests = 0
    $failed = 0
    $bytes = 0
    $lat = New-Object 'System.Collections.Generic.List[double]'
    $err = ''
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $i = 0
    while ($sw.Elapsed.TotalSeconds -lt $secs) {
        $i++
        $one = [System.Diagnostics.Stopwatch]::StartNew()
        $client = New-Object System.Net.Sockets.TcpClient
        try {
            $client.Connect('127.0.0.1', $port)
            $ns = $client.GetStream()
            $ns.ReadTimeout = 8000
            $req = "GET /live.json?c=$i HTTP/1.1`r`nHost: 127.0.0.1`r`nAccept: application/json`r`nConnection: close`r`n`r`n"
            $raw = [System.Text.Encoding]::ASCII.GetBytes($req)
            $ns.Write($raw, 0, $raw.Length)
            $buf = New-Object byte[] 65536
            $n = 0
            while ($true) {
                try { $read = $ns.Read($buf, 0, $buf.Length) } catch { break }
                if ($read -le 0) { break }
                $n += $read
                if ($n -gt 8000000) { break }
            }
            if ($n -gt 0) { $requests++; $bytes += $n; $lat.Add([math]::Round($one.Elapsed.TotalMilliseconds, 1)) }
            else { $failed++ }
        } catch {
            $failed++
            $err = $_.Exception.Message
        } finally {
            if ($client) { $client.Close() }
        }
        $next = $i * $pollSec
        $gap = ($next * 1000) - $sw.Elapsed.TotalMilliseconds
        if ($gap -gt 0) { Start-Sleep -Milliseconds ([int] [math]::Min($gap, 2000)) }
    }
    $sorted = @($lat | Sort-Object)
    $p50 = 0.0
    $p95 = 0.0
    $maxMs = 0.0
    if ($sorted.Count -gt 0) {
        $p50 = $sorted[[int] [math]::Floor(($sorted.Count - 1) * 0.50)]
        $p95 = $sorted[[int] [math]::Floor(($sorted.Count - 1) * 0.95)]
        $maxMs = $sorted[$sorted.Count - 1]
    }
    $obj = [pscustomobject]@{
        Requests = $requests
        Failed   = $failed
        Bytes    = $bytes
        P50Ms    = $p50
        P95Ms    = $p95
        MaxMs    = $maxMs
        Err      = $err
    }
    $obj | ConvertTo-Json -Compress | Set-Content -Encoding UTF8 -NoNewline -Path $outFile
}

function Start-Clients {
    param([string] $Kind, [int] $Count, [int] $Port, [int] $Seconds, [int] $PollSeconds, [string] $Dir)
    $jobs = @()
    for ($i = 1; $i -le $Count; $i++) {
        $out = Join-Path $Dir ($Kind + $i + '.json')
        if ($Kind -eq 'stream') {
            $jobs += Start-Job -ScriptBlock $StreamClient -ArgumentList $Port, $Seconds, '/live/stream', $out
        } else {
            $jobs += Start-Job -ScriptBlock $PollClient -ArgumentList $Port, $Seconds, $PollSeconds, $out
        }
    }
    return $jobs
}

# ------------------------------------------------------------------------- measure one scenario ----

function Measure-Scenario {
    param([string] $Name, [int] $Port, [int] $Seconds, [int] $PollSeconds, [string] $PoolLog, [string] $ProcessName, [string] $Csv)

    $streams = 0
    $polls = 0
    switch ($Name) {
        'none'    { }
        'stream1' { $streams = 1 }
        'stream3' { $streams = 3 }
        'poll1'   { $polls = 1 }
        'poll3'   { $polls = 3 }
        default   { throw "unknown scenario '$Name'" }
    }

    $dir = Join-Path $env:TEMP ('lsm-' + (Get-Date -Format 'HHmmss') + '-' + $Name)
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    Write-Host ("[{0}] {1}: {2} stream page(s), {3} polling page(s), {4} s" -f (Get-Date -Format 'HH:mm:ss'), $Name, $streams, $polls, $Seconds)

    $before = Get-ProcSample $ProcessName
    $jobs = @()
    if ($streams -gt 0) { $jobs += Start-Clients 'stream' $streams $Port $Seconds $PollSeconds $dir }
    if ($polls -gt 0)    { $jobs += Start-Clients 'poll' $polls $Port $Seconds $PollSeconds $dir }

    $from = Get-Date
    $loads = @()
    $peakCpu = 0.0
    $pct = 0.0
    $elapsedTick = 0
    $limit = $from.AddSeconds($Seconds)
    while ((Get-Date) -lt $limit) {
        Start-Sleep -Seconds 5
        $now = Get-Date
        $l = Get-MachineLoad
        if ($null -ne $l) { $loads += $l }
        $s = Get-ProcSample $ProcessName
        $ran = ($now - $from).TotalSeconds
        if ($ran -gt 0 -and $before.Found -gt 0) {
            $pct = (($s.CpuSecs - $before.CpuSecs) / ($ran * [Environment]::ProcessorCount)) * 100
            if ($pct -gt $peakCpu) { $peakCpu = $pct }
        }
        $elapsedTick += 5
        if ($elapsedTick -ge 30) {
            $elapsedTick = 0
            Write-Host ("      ... {0,3}/{1} s  scheduler cpu {2:0.0}%  host {3}%" -f [int] ($now - $from).TotalSeconds, $Seconds, $pct, ($loads | Select-Object -Last 1))
        }
    }
    $to = Get-Date
    if ($jobs.Count -gt 0) {
        Wait-Job -Job $jobs -Timeout 40 | Out-Null
        foreach ($j in $jobs) { Receive-Job -Job $j -ErrorAction SilentlyContinue | Out-Null; Remove-Job -Job $j -Force }
    }

    $after = Get-ProcSample $ProcessName
    $ranSec = ($to - $from).TotalSeconds
    $cpuPct = 0.0
    if ($before.Found -gt 0) { $cpuPct = (($after.CpuSecs - $before.CpuSecs) / ($ranSec * [Environment]::ProcessorCount)) * 100 }

    $streamRows = @()
    $pollRows = @()
    foreach ($file in (Get-ChildItem $dir -Filter 'stream*.json' -ErrorAction SilentlyContinue)) {
        try { $streamRows += (Get-Content -Raw -Encoding UTF8 $file.FullName | ConvertFrom-Json) } catch { }
    }
    foreach ($file in (Get-ChildItem $dir -Filter 'poll*.json' -ErrorAction SilentlyContinue)) {
        try { $pollRows += (Get-Content -Raw -Encoding UTF8 $file.FullName | ConvertFrom-Json) } catch { }
    }

    $frames = 0
    $keeps = 0
    $sbytes = 0
    $head = 0.0
    $recon = 0
    $fpsPeak = 0
    $fpsZero = 0
    $fpsSamples = 0
    foreach ($r in $streamRows) {
        $frames += $r.Frames
        $keeps += $r.Keeps
        $sbytes += $r.Bytes
        $recon += $r.Reconnects
        if ($r.HeadMs -gt 0) { $head = [math]::Max($head, $r.HeadMs) }
        if ($r.PerSec) {
            foreach ($v in ($r.PerSec -split ',')) {
                $iv = 0
                if ([int]::TryParse($v, [ref] $iv)) {
                    $fpsSamples++
                    if ($iv -gt $fpsPeak) { $fpsPeak = $iv }
                    if ($iv -eq 0) { $fpsZero++ }
                }
            }
        }
    }

    $requests = 0
    $failed = 0
    $pbytes = 0
    $p50 = 0.0
    $p95 = 0.0
    $maxMs = 0.0
    foreach ($r in $pollRows) {
        $requests += $r.Requests
        $failed += $r.Failed
        $pbytes += $r.Bytes
        $p50 = [math]::Max($p50, $r.P50Ms)
        $p95 = [math]::Max($p95, $r.P95Ms)
        $maxMs = [math]::Max($maxMs, $r.MaxMs)
    }
    # A stream page is one request for the whole interval; a polling page makes Seconds/PollSeconds.
    $requests += $streamRows.Count

    $ticks = @(Read-TickLines $PoolLog $from $to)
    $tickAvg = 0.0
    $tickMax = 0.0
    $step = ''
    $stepMax = 0.0
    if ($ticks.Count -gt 0) {
        $tickAvg = ($ticks | Measure-Object -Property Took -Average).Average
        $tickMax = ($ticks | Measure-Object -Property Took -Maximum).Maximum
        $worst = $ticks | Sort-Object -Property StepSec -Descending | Select-Object -First 1
        if ($worst) { $step = $worst.Step; $stepMax = $worst.StepSec }
    }

    $hostAvg = 0.0
    $hostPeak = 0.0
    if ($loads.Count -gt 0) {
        $hostAvg = ($loads | Measure-Object -Average).Average
        $hostPeak = ($loads | Measure-Object -Maximum).Maximum
    }

    $row = [pscustomobject]@{
        When        = $from.ToString('yyyy-MM-dd HH:mm:ss')
        Scenario    = $Name
        Seconds     = [int] $ranSec
        CpuPct      = [math]::Round($cpuPct, 2)
        CpuPeakPct  = [math]::Round($peakCpu, 2)
        MemMb       = $after.MemMb
        MemDeltaMb  = [math]::Round($after.MemMb - $before.MemMb, 1)
        Threads     = $after.Threads
        Handles     = $after.Handles
        HostPct     = [math]::Round($hostAvg, 1)
        HostPeak    = [math]::Round($hostPeak, 1)
        Workers     = Get-WorkerCount
        Pages       = ($streamRows.Count + $pollRows.Count)
        Requests    = $requests
        ReqPerSec   = [math]::Round($requests / [math]::Max(1, $ranSec), 3)
        PollP50Ms   = $p50
        PollP95Ms   = $p95
        PollMaxMs   = $maxMs
        Failed      = $failed
        Frames      = $frames
        FramesPerSec = [math]::Round($frames / [math]::Max(1, $ranSec), 2)
        FpsPeak     = $fpsPeak
        QuietSecs   = $fpsZero
        KeepAlives  = $keeps
        Reconnects  = $recon
        HeadMs      = $head
        StreamKb    = [math]::Round($sbytes / 1KB, 1)
        PollKb      = [math]::Round($pbytes / 1KB, 1)
        Ticks       = $ticks.Count
        TickAvgSec  = [math]::Round($tickAvg, 2)
        TickMaxSec  = [math]::Round($tickMax, 2)
        SlowestStep = $step
        SlowestSec  = [math]::Round($stepMax, 2)
        PoolLog     = $PoolLog
    }

    $exists = Test-Path $Csv
    $row | Export-Csv -NoTypeInformation -Path $Csv -Append
    Write-Host ("      -> cpu {0:0.00}% (peak {1:0.00}%)  req/s {2:0.000}  fps {3:0.00} (peak {4})  tick avg {5:0.00}s max {6:0.00}s over {7} tick(s)  host {8:0}% w{9}" -f `
            $row.CpuPct, $row.CpuPeakPct, $row.ReqPerSec, $row.FramesPerSec, $row.FpsPeak, $row.TickAvgSec, $row.TickMaxSec, $row.Ticks, $row.HostPct, $row.Workers)
    Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
    return $row
}

# ------------------------------------------------------------------------------------ report ----

function Format-Report {
    param([string] $Csv)
    if (-not (Test-Path $Csv)) { throw "no csv at $Csv" }
    $rows = @(Import-Csv $Csv)
    if ($rows.Count -eq 0) { throw "no rows in $Csv" }
    $cols = @(
        @('Scenario',   'scenario'),
        @('CpuPct',     'sched CPU %'),
        @('CpuPeakPct', 'peak %'),
        @('ReqPerSec',  'req/s'),
        @('PollP95Ms',  'poll p95 ms'),
        @('Frames',     'frames'),
        @('FramesPerSec', 'frames/s'),
        @('FpsPeak',    'peak f/s'),
        @('StreamKb',   'stream KB'),
        @('PollKb',     'poll KB'),
        @('Ticks',      'ticks'),
        @('TickAvgSec', 'tick avg s'),
        @('TickMaxSec', 'tick max s'),
        @('SlowestStep', 'slowest step'),
        @('HostPct',    'host %'),
        @('Workers',    'workers')
    )
    Write-Host ('| ' + (($cols | ForEach-Object { $_[1] }) -join ' | ') + ' |')
    Write-Host ('|' + (('---' | ForEach-Object { $_ }) * $cols.Count -join '|') + '|')
    foreach ($r in $rows) {
        $cells = @()
        foreach ($c in $cols) { $cells += [string] $r.($c[0]) }
        Write-Host ('| ' + ($cells -join ' | ') + ' |')
    }
    Write-Host ''
    Write-Host ('rows: ' + $rows.Count + '  csv: ' + $Csv)
}

# ---------------------------------------------------------------------------------------- run ----

if ($Action -eq 'report') {
    Format-Report $Csv
    return
}

# -File cannot pass an array, so a comma string is accepted too: -Scenarios none,stream1,poll1
if ($Scenarios.Count -eq 1 -and $Scenarios[0] -match ',') {
    $Scenarios = @($Scenarios[0] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

$resolvedPort = Resolve-Port $Repo $Port
$logPath = Resolve-PoolLog $Repo $PoolLog
$sample = Get-ProcSample $ProcessName
if ($sample.Found -eq 0) {
    throw "no $ProcessName process is running; the live stream can only be measured against a running pool (start it with scheduler-next -Pool, then run this again)"
}
Write-Host ("pool page: 127.0.0.1:{0}   log: {1}   cores: {2}   scheduler pid(s): {3}   start: {4}" -f `
        $resolvedPort, $logPath, [Environment]::ProcessorCount, $sample.Found, $started.ToString('HH:mm:ss'))

$all = @()
foreach ($name in $Scenarios) {
    if ($TimeoutSec -gt 0 -and ((Get-Date) - $started).TotalSeconds + $Seconds + 60 -gt $TimeoutSec) {
        Write-Host ("stopping: the next scenario ({0}) would pass the {1} s cap" -f $name, $TimeoutSec)
        break
    }
    $all += Measure-Scenario $name $resolvedPort $Seconds $PollSeconds $logPath $ProcessName $Csv
    Start-Sleep -Seconds 2
}
Write-Host ''
Format-Report $Csv
Write-Host ''
Write-Host ('csv: ' + $Csv)
