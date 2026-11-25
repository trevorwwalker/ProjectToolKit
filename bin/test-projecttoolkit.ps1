<#
.SYNOPSIS
    Script test for projecttoolkit.ps1's setup actions (card 1028-B): categories.json, no workers.json,
    and the pool join. Runs in a temp folder with a temp LOCALAPPDATA; touches nothing else.
    Exit code 0 when every check passes.
#>

# Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
# Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)

$ErrorActionPreference = 'Stop'
$kit = Join-Path $PSScriptRoot 'projecttoolkit.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("ptk-test-" + [guid]::NewGuid().ToString('N'))
$fakeTemp = Join-Path $tmp 'faketemp'
$fails = 0
function Check([bool]$ok, [string]$what) { if ($ok) { Write-Host "ok    $what" } else { Write-Host "FAIL  $what"; $script:fails++ } }

# Each run is a child powershell, so the temp LOCALAPPDATA is what the script sees. TEMP/TMP point at a fake
# folder ($script:fakeTemp), so the repos under $tmp\real are "normal" ones; worker variables are cleared unless
# given in $extraEnv (card 1034-B).
function Run([string]$local, [string[]]$more, [hashtable]$extraEnv = @{}) {
    $names = @('LOCALAPPDATA', 'TEMP', 'TMP', 'PTK_CARD') + @(Get-ChildItem Env: | Where-Object Name -like '*_JOBS' | ForEach-Object Name) + @($extraEnv.Keys)
    $saved = @{}; foreach ($n in $names | Select-Object -Unique) { $saved[$n] = [Environment]::GetEnvironmentVariable($n) }
    try {
        foreach ($n in $saved.Keys) { if ($n -like '*_JOBS' -or $n -eq 'PTK_CARD') { [Environment]::SetEnvironmentVariable($n, $null) } }
        $env:LOCALAPPDATA = $local; $env:TEMP = $script:fakeTemp; $env:TMP = $script:fakeTemp
        foreach ($k in $extraEnv.Keys) { [Environment]::SetEnvironmentVariable($k, $extraEnv[$k]) }
        $out = & powershell.exe -NoProfile -File $kit @more 2>&1
    } finally { foreach ($n in $saved.Keys) { [Environment]::SetEnvironmentVariable($n, $saved[$n]) } }
    return ($out | Out-String)
}
try {
    $local = Join-Path $tmp 'local'; $real = Join-Path $tmp 'real'; $repoA = Join-Path $real 'Alpha'; $repoB = Join-Path $real 'Beta'
    New-Item -ItemType Directory -Force -Path $local, $repoA, $repoB, $fakeTemp | Out-Null
    $pool = Join-Path $local 'ProjectToolkit\pool.json'

    # 1. jobs on an empty repo: categories.json, no workers.json, pool created with the project once.
    $null = Run $local @('-Action', 'jobs', '-Repo', $repoA, '-Slots', '3')
    Check (Test-Path "$repoA\jobs\categories.json") 'categories.json written'
    Check (-not (Test-Path "$repoA\jobs\lanes.json")) 'no lanes.json written'
    Check (-not (Test-Path "$repoA\docs\ai")) 'no docs/ai folder created (plans start in jobs/plans/)'
    Check (-not (Test-Path "$repoA\jobs\workers.json")) 'no workers.json written (-Slots accepted and ignored)'
    $cat = Get-Content -Raw "$repoA\jobs\categories.json" | ConvertFrom-Json
    Check ($null -ne $cat.categories.A) 'categories.json has a categories.A'
    $sj = Get-Content -Raw "$repoA\jobs\scheduler.json" | ConvertFrom-Json
    Check ($null -eq $sj.slots -and $sj.jobsDir -eq 'jobs' -and $sj.planner.outbox -eq 'jobs\.planner-outbox' -and $sj.project -eq 'Alpha') 'scheduler.json: no slots, jobsDir, planner.outbox, project'
    Check (Test-Path $pool) 'pool.json created'
    $p = Get-Content -Raw $pool | ConvertFrom-Json
    Check (@($p.projects | Where-Object name -eq 'Alpha').Count -eq 1 -and $p.projects[0].weight -eq 50 -and $p.projects[0].repo -eq (Resolve-Path $repoA).Path) 'Alpha is in the pool once, weight 50, repo as given'
    Check ($null -ne $p.pool.types.Claude) 'created pool file has the pool defaults'

    # 2. Twice: still once.
    $null = Run $local @('-Action', 'jobs', '-Repo', $repoA)
    $p = Get-Content -Raw $pool | ConvertFrom-Json
    Check (@($p.projects | Where-Object name -eq 'Alpha').Count -eq 1) 'a second run adds Alpha once only'

    # 3. A second project is appended; the first entry and the rest of the file are untouched.
    $before = Get-Content -Raw $pool
    $null = Run $local @('-Action', 'jobs', '-Repo', $repoB)
    $after = Get-Content -Raw $pool
    $p = Get-Content -Raw $pool | ConvertFrom-Json
    Check (@($p.projects).Count -eq 2 -and $p.projects[1].name -eq 'Beta') 'Beta appended after Alpha'
    Check ($after.Contains((($before -split "`n" | Where-Object { $_ -match '"name": "Alpha"' }) | Select-Object -First 1))) 'Alpha entry text unchanged'
    Check ($p.pool.maxWorkers -eq 8 -and $p.port -eq 8766) 'the rest of the pool file is unchanged'
    Check ($after.Contains('C:\\') -or $after.Contains('\\')) 'backslashes are escaped in the file'

    # 4. -NoPool leaves the pool file alone.
    $repoC = Join-Path $real 'Gamma'; New-Item -ItemType Directory -Force -Path $repoC | Out-Null
    $null = Run $local @('-Action', 'jobs', '-Repo', $repoC, '-NoPool')
    Check ((Get-Content -Raw $pool) -eq $after) '-NoPool does not touch the pool file'

    # 5. -Update keeps an existing lanes.json and workers.json, writes no categories.json beside lanes.json.
    $repoD = Join-Path $real 'Delta'; New-Item -ItemType Directory -Force -Path "$repoD\jobs" | Out-Null
    Set-Content -Path "$repoD\jobs\lanes.json" -Value '{"lanes":{"A":{"name":"x"}}}' -NoNewline
    Set-Content -Path "$repoD\jobs\workers.json" -Value '{"slots":2}' -NoNewline
    $null = Run $local @('-Action', 'jobs', '-Repo', $repoD, '-Update', '-NoPool')
    Check ((Get-Content -Raw "$repoD\jobs\lanes.json") -eq '{"lanes":{"A":{"name":"x"}}}') '-Update keeps lanes.json'
    Check ((Get-Content -Raw "$repoD\jobs\workers.json") -eq '{"slots":2}') '-Update keeps workers.json'
    Check (-not (Test-Path "$repoD\jobs\categories.json")) '-Update makes no categories.json beside an existing lanes.json'

    # 6. -Lanes still works as the old name for -Categories.
    $repoE = Join-Path $real 'Eps'; New-Item -ItemType Directory -Force -Path $repoE | Out-Null
    $null = Run $local @('-Action', 'jobs', '-Repo', $repoE, '-Lanes', 'A=core:src/;B=tools:tools/', '-NoPool')
    $cat = Get-Content -Raw "$repoE\jobs\categories.json" | ConvertFrom-Json
    Check ($null -ne $cat.categories.B -and $cat.categories.B.name -eq 'tools') '-Lanes is accepted as -Categories'

    # 7. Card 1034-B: tests and workers never join the live pool.
    $beforeAll = Get-Content -Raw $pool
    $repoW = Join-Path $real 'Worker'; New-Item -ItemType Directory -Force -Path $repoW | Out-Null
    foreach ($v in @('PTK_JOBS', 'AGENT_JOBS', 'MY_JOBS', 'PTK_CARD')) {
        $out = Run $local @('-Action', 'jobs', '-Repo', $repoW) @{ $v = '5' }
        Check (((Get-Content -Raw $pool) -eq $beforeAll) -and $out -match 'skipped\s+the pool join: running inside a worker') "worker environment ($v) -> no join, reason printed"
    }
    $repoT = Join-Path $fakeTemp 'InTemp'; New-Item -ItemType Directory -Force -Path $repoT | Out-Null
    $out = Run $local @('-Action', 'jobs', '-Repo', $repoT)
    Check (((Get-Content -Raw $pool) -eq $beforeAll) -and $out -match 'skipped\s+the pool join: .*temp folder') 'temp repo -> no join, reason printed'
    # Card 1088-B: TestTmp's scratch repositories live in <repo>\out\test-tmp\, not in the temp folder, and a
    # test process has its PTK_* variables stripped, so the folder itself is the only thing that says "test".
    $repoTT = Join-Path $real 'out\test-tmp\Scratch'; New-Item -ItemType Directory -Force -Path $repoTT | Out-Null
    $out = Run $local @('-Action', 'jobs', '-Repo', $repoTT)
    Check (((Get-Content -Raw $pool) -eq $beforeAll) -and $out -match 'skipped\s+the pool join: .*test scratch folder') 'repo under out\test-tmp -> no join, reason printed'
    $out = Run $local @('-Action', 'jobs', '-Repo', $repoTT, '-JoinPool')
    $p = Get-Content -Raw $pool | ConvertFrom-Json
    Check (@($p.projects | Where-Object name -eq 'Scratch').Count -eq 1) '-JoinPool forces the join for an out\test-tmp repo'
    $beforeAll = Get-Content -Raw $pool
    $out = Run $local @('-Action', 'jobs', '-Repo', $repoT, '-JoinPool')
    $p = Get-Content -Raw $pool | ConvertFrom-Json
    Check (@($p.projects | Where-Object name -eq 'InTemp').Count -eq 1) '-JoinPool forces the join for a temp repo'
    $out = Run $local @('-Action', 'jobs', '-Repo', $repoW, '-JoinPool') @{ PTK_JOBS = '5' }
    $p = Get-Content -Raw $pool | ConvertFrom-Json
    Check (@($p.projects | Where-Object name -eq 'Worker').Count -eq 1) '-JoinPool forces the join inside a worker'
    $out = Run $local @('-Action', 'jobs', '-Repo', $repoW)
    $p = Get-Content -Raw $pool | ConvertFrom-Json
    Check (@($p.projects | Where-Object name -eq 'Worker').Count -eq 1) 'a normal repo joins once (second run keeps one entry)'

    # 9. The printed Next list: the plan line comes before the workers line, then carding; the help says no docs/ai.
    $repoN = Join-Path $real 'NewOne'
    $out = Run $local @('-Action', 'new-project', '-Repo', $repoN, '-NoPool')
    $iPlan = $out.IndexOf('Have a planner write'); $iWork = $out.IndexOf('Set up the worker types'); $iCard = $out.IndexOf('Ask the planner to card')
    Check ($iPlan -ge 0 -and $iWork -gt $iPlan -and $iCard -gt $iWork) 'the Next list has the plan line before the workers line, then the carding line'
    $out = Run $local @('-Action', 'jobs', '-Repo', $repoA, '-Update', '-NoPool')
    Check ($out -notmatch 'gitattributes') '-Update closing message does not claim .gitattributes'
    $help = (Get-Help $kit -Full | Out-String)
    Check ($help -notmatch 'docs/\{ai' -and $help -match 'docs/\{design,reference\}') 'the help names docs/{design,reference}/ and no docs/ai'
    $gi = Get-Content -Raw "$repoN\.gitignore"
    Check ($gi -match '(?m)^jobs/\.logs/\r?$' -and $gi -match '(?m)^jobs/\.planner-outbox/\r?$') 'the new project .gitignore ignores jobs/.logs/ and jobs/.planner-outbox/'
    # out/: the platform folder's .gitkeep is committed, everything else under out/ is ignored.
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $null = & git -C $repoN init -q 2>&1
        New-Item -ItemType Directory -Force -Path "$repoN\out\windows\pkg" | Out-Null
        Set-Content "$repoN\out\windows\pkg\x.bin" 'x'; Set-Content "$repoN\out\x.txt" 'x'
        $null = & git -C $repoN add -A 2>&1
        $staged = @(& git -C $repoN diff --cached --name-only)
        Check (($staged -contains 'out/windows/.gitkeep') -and -not ($staged -like 'out/windows/pkg*') -and -not ($staged -contains 'out/x.txt')) 'a new project commits out/windows/.gitkeep and nothing else under out/'
    }
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
if ($fails) { Write-Host "$fails check(s) failed"; exit 1 }
Write-Host 'all checks passed'
