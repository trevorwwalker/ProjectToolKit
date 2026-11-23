<#
.SYNOPSIS
    ProjectToolkit's setup actions: lay a new project down from the canonical layout, or set up or
    refresh a project's job queue.

.DESCRIPTION
    ProjectToolkit is a kit of three things, not one application:

      1. The canonical project layout, with the templates and documentation that go with it
         (assets/templates/, docs/workflow.md, docs/agent-lessons.md).
      2. The job queue system that AI agents work: jobs/, its protocol, templates and config.
      3. The scheduler, the application that runs a queue: code\src\Scheduler.Host (scheduler-next.exe). It is run from the
         kit and is never copied into a project. Publish the runnable folder once with
         `dotnet publish code\src\Scheduler.Host -c Release -o out\windows\scheduler` and start
         `out\windows\scheduler\scheduler-tray.exe <project folder>`; the page's firewall rule is bound to that exe path.

    This script is the entry point for the two setup actions. Each is independent; the first does the
    second as part of standing up a project.

      -Action new-project   A new project. Lays down the canonical layout (assets/, bin/,
                            code/{extern,lib,src,tools}/, extern/bin/, out/,
                            test/{unit,integration,data}/, docs/{ai,design,reference}/)
                            with README.md, .gitignore, .gitattributes, docs/conventions.md and the
                            two design documents, then sets up the job queue. This is what starts a
                            project.

      -Action jobs          The job queue system on its own, in a repository that is already there:
                            jobs/ (the queue folders, the generic README, project.md, categories.json,
                            models.json, scheduler.json, the card and plan templates, the requests
                            folder), docs/ai/ and docs/known_gaps.md, then joins the project to the
                            scheduler pool (%LOCALAPPDATA%\ProjectToolkit\pool.json; -NoPool skips). Use it to put a
                            queue into an existing repository, and with -Update to pull kit
                            improvements into a project that already has one.

    Nothing already in the repository is overwritten unless -Force is given. -Update refreshes only
    the text the kit owns (the jobs README, the card, plan and request templates, docs/ai/README.md,
    .gitattributes) and fills in any folder or file the project is missing. README.md, .gitignore,
    docs/conventions.md, docs/design/ and the queue's project.md, categories.json (or an older lanes.json), an old
    workers.json and scheduler.json become the project's own and are left alone.

.PARAMETER Action
    'new-project' lays down the canonical layout and then does 'jobs'. 'jobs' does the queue only,
    which is what an existing repository needs.

.PARAMETER Categories
    The categories, as "A=name:path,path;B=name:path,...", for example
    "A=core:code/src/,test/;B=front end:code/src/ui/;C=tools:code/tools/". Paths are from the
    repository root, which the canonical layout puts the code under (code/, test/). One category is
    fine to start with. Only used when categories.json is created. -Lanes is the old name for it.

.PARAMETER Slots
    Accepted and ignored: the worker types are the pool file's, so no workers.json is written.

.PARAMETER NoPool
    Do not add the project to the scheduler pool. Without it (-JoinPool, the default), the last step
    adds { name, repo, weight 50 } to %LOCALAPPDATA%\ProjectToolkit\pool.json if the project is not in it.
    The join is skipped (with the reason printed) inside a worker (a *_JOBS variable or PTK_CARD is set), for a
    repository under the temp folder or under any out\test-tmp\ folder (a test's scratch root, card 1088-B), or
    without jobs/scheduler.json; -JoinPool forces it past the first two.

.PARAMETER Modules
    Comma-separated module folder names to create under code/src/ (app, platform, graphics, ...). The
    module set is a design decision: leave it out and fill it in by hand once the design document
    settles it. OS-specific code goes in code/src/platform/<platform>/, shared code in
    code/src/platform/common/. Only used by -Action new-project.

.EXAMPLE
    .\bin\projecttoolkit.ps1 -Action new-project -Repo C:\Projects\Foo `
        -Categories "A=core:code/src/,test/;B=tools:code/tools/" -Modules "app,platform,systems" `
        -Gate "cmake -S code -B build;;cmake --build build --parallel {jobs};;ctest --test-dir build -j {jobs}"

.EXAMPLE
    .\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\Foo        # add a queue to a live repo

.EXAMPLE
    .\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\Foo -Update   # pull kit improvements in
#>
param(
    [Parameter(Mandatory)][ValidateSet('new-project', 'jobs')][string]$Action,
    [Parameter(Mandatory)][string]$Repo,
    [string]$Project = '',
    [Alias('Lanes')][string]$Categories = 'A=core:code/src/,test/',
    [string]$Modules = '',
    [int]$Slots = 0,
    [string[]]$Gate = @(),
    [string]$Model = 'spark',
    [string]$Endpoint = 'http://spark.local:18400/v1',
    [string]$JobsEnvVar = 'AGENT_JOBS',
    [switch]$Force,
    [switch]$Update,
    [switch]$JoinPool,
    [switch]$NoPool
)

$ErrorActionPreference = 'Stop'
if ($Slots) { Write-Host "note     -Slots is ignored: the worker types are the pool file's, and no workers.json is written" }
# Gates may come as an array (from PowerShell) or as one string with ';;' between commands (from
# cmd or powershell -File, which can't pass arrays).
$Gate = @($Gate | ForEach-Object { $_ -split ';;' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Kit = Split-Path -Parent $PSScriptRoot    # this script lives in the kit's bin/
$T = Join-Path $Kit 'assets/templates'
if (-not (Test-Path $Repo)) { New-Item -ItemType Directory -Force -Path $Repo | Out-Null }
$Repo = (Resolve-Path $Repo).Path
if (-not (Test-Path (Join-Path $Repo '.git'))) {
    & git.exe -C $Repo init -b main | Out-Null
    Write-Host "git repository created in $Repo"
}
# Where the queue lives: jobs/ (since 2026-11-18). A project set up before then keeps docs/jobs/
# until it is moved (git mv docs/jobs jobs, jobsDir in its scheduler.json); -Update never makes a
# second queue beside it.
$Q = if ((Test-Path (Join-Path $Repo 'docs/jobs/scheduler.json')) -and -not (Test-Path (Join-Path $Repo 'jobs'))) { 'docs/jobs' } else { 'jobs' }
if (-not $Project) {
    # A project already set up knows its own name; a new one takes the folder's.
    $sj = Join-Path $Repo "$Q/scheduler.json"
    if (Test-Path $sj) {
        try { $p = (Get-Content -Raw -Encoding UTF8 -Path $sj | ConvertFrom-Json).project; if ($p) { $Project = [string]$p } } catch { }
    }
    if (-not $Project) { $Project = Split-Path -Leaf $Repo }
}
$Safe = ($Project -replace '[^\w.-]', '_')

#
# Shared helpers.
#
function Put([string]$rel, [string]$text, [bool]$generic = $false) {
    $path = Join-Path $Repo $rel
    $exists = Test-Path $path
    if ($exists -and -not $Force -and -not ($Update -and $generic)) { Write-Host "kept     $rel"; return }
    # The project's .gitattributes says eol=lf, so kit text is written as LF; the templates are
    # stored however git checked them out on this PC.
    $text = ($text -replace "`r`n", "`n") -replace "`r", "`n"
    if ($exists -and (Get-Content -Raw -Encoding UTF8 -Path $path) -eq $text) { Write-Host "same     $rel"; return }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "$(if ($exists) { 'updated ' } else { 'created ' }) $rel"
}
function Template([string]$rel) { return [System.IO.File]::ReadAllText((Join-Path $T $rel)) }

# A folder with no files does not survive in git, so a kept folder gets a .gitkeep, removed once real
# files land in it (except under out/, which is git-ignored wholesale and so always keeps it).
function Keep([string]$rel) {
    $dir = Join-Path $Repo ($rel.TrimEnd('/') -replace '/', '\')
    if (-not (Test-Path $dir)) { $null = New-Item -ItemType Directory -Force -Path $dir }
    $script:Kept[$rel.TrimEnd('/')] = $true
    $keep = Join-Path $dir '.gitkeep'
    $real = @(Get-ChildItem $dir -Force | Where-Object { $_.Name -ne '.gitkeep' })
    if (-not $real.Count -and -not (Test-Path $keep)) {
        [System.IO.File]::WriteAllText($keep, '', (New-Object System.Text.UTF8Encoding $false))
    } elseif ($real.Count -and (Test-Path $keep)) {
        Remove-Item $keep -Force
    }
}
# Every folder Keep has touched; run over them again at the end, once the project's files are in
# place, so no folder that has content is left carrying a .gitkeep.
$script:Kept = [ordered]@{}

function Q([string]$s) { return '"' + (($s -replace '\\', '\\') -replace '"', '\"') + '"' }
function QList($a) { return '[' + ((@($a) | Where-Object { $_ } | ForEach-Object { Q $_ }) -join ', ') + ']' }
function Categories-Json($cats) {
    $about = 'The categories of this project''s job queue: what each is for and which files it owns. One worker per category at a time, and categories own different files, so two workers never edit the same file. A path ending in / is a folder and everything under it; * matches any characters. The scheduler reads this file from main; a category redefined while it has cards in queue/ or active/ is held until the developer accepts the change on the scheduler page.'
    $out = @('{', "  `"about`": $(Q $about),", '  "categories": {')
    $keys = @($cats.Keys)
    for ($i = 0; $i -lt $keys.Count; $i++) {
        $l = $cats[$keys[$i]]
        $out += "    `"$($keys[$i])`": {"
        $out += "      `"name`": $(Q $l.name),"
        $out += "      `"purpose`": $(Q $l.purpose),"
        $out += "      `"owns`": $(QList $l.owns),"
        $out += "      `"except`": $(QList $l.except)"
        $out += "    }$(if ($i -lt $keys.Count - 1) { ',' })"
    }
    $out += @('  },', '  "shared": {',
        "    `"about`": $(Q 'Any category may add to these files but not rewrite them. Pull right before editing one and push right after.'),",
        "    `"addOnly`": $(QList @('.gitignore', 'docs/known_gaps.md')),", '    "appendOnly": []', '  }', '}', '')
    return ($out -join "`n")
}

# Why the pool join must not happen, or '' when it may. Only a person setting up a real project joins the live
# pool: a worker (a *_JOBS variable or PTK_CARD in its environment) and a repository under the temp folder or
# under a test's out\test-tmp\ scratch root (tests, scratch repos) never do, and a folder without
# jobs/scheduler.json is not a project yet.
# -JoinPool skips the worker, temp and test-tmp checks, not the folder check.
function Get-PoolSkipReason {
    if (-not (Test-Path -LiteralPath (Join-Path $Repo 'jobs\scheduler.json'))) { return "$Repo has no jobs/scheduler.json" }
    if ($JoinPool) { return '' }
    $names = @(Get-ChildItem Env: | Where-Object { ($_.Name -like '*_JOBS' -or $_.Name -eq 'PTK_CARD') -and $_.Value } | ForEach-Object Name)
    if ($names.Count) { return "running inside a worker ($($names[0]) is set)" }
    $repoFull = [IO.Path]::GetFullPath($Repo).TrimEnd('\', '/')
    foreach ($t in @($env:TEMP, [IO.Path]::GetTempPath())) {
        if (-not $t) { continue }
        $tf = [IO.Path]::GetFullPath($t).TrimEnd('\', '/')
        if ($repoFull -ieq $tf -or $repoFull.StartsWith($tf + '\', [StringComparison]::OrdinalIgnoreCase)) { return "$Repo is under the temp folder $tf" }
    }
    # TestTmp (test/unit/TestSupport/TestTmp.cs, card 1042-B) puts a test's scratch repositories under
    # <repo>\out\test-tmp\, where the temp check above no longer sees them; and a test process has its PTK_*
    # variables stripped (card 1070-B), so the worker check cannot be relied on either. Any repository inside
    # one of those folders is a test's, never a project (card 1088-B).
    $parts = $repoFull -split '[\\/]'
    for ($i = 1; $i -lt $parts.Count; $i++) {
        if ($parts[$i] -ieq 'test-tmp' -and $parts[$i - 1] -ieq 'out') {
            return "$Repo is a test scratch folder under $(($parts[0..$i] -join '\'))"
        }
    }
    return ''
}

# Join the scheduler pool: add { name, repo, weight } to pool.json once. Other entries and the rest of
# the file are never touched: the new entry is spliced in as text, so the file's formatting survives.
function Join-Pool {
    $dir = Join-Path $env:LOCALAPPDATA 'ProjectToolkit'
    $file = Join-Path $dir 'pool.json'
    $entry = '{ "name": ' + (Q $Project) + ', "repo": ' + (Q $Repo) + ', "weight": 50 }'
    if (-not (Test-Path $file)) {
        $lines = @(
            '{',
            '  "about": "The scheduler''s pool: the projects it serves and the worker types it runs.",',
            '  "projects": [',
            "    $entry",
            '  ],',
            '  "port": 8766,',
            '  "listen": "lan",',
            '  "remoteControl": true,',
            '  "pool": {',
            '    "maxWorkers": 8,',
            '    "types": {',
            '      "Spark":  { "max": 6, "agent": "", "premium": false },',
            '      "Sonnet": { "max": 6, "agent": "claude", "model": "claude-sonnet-5-5", "premium": true },',
            '      "Opus":   { "max": 6, "agent": "claude", "model": "claude-opus-5-5",   "premium": true }',
            '    }',
            '  }',
            '}', '')
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        [System.IO.File]::WriteAllText($file, ($lines -join "`n"), (New-Object System.Text.UTF8Encoding $false))
        Write-Host "created  $file (with the pool defaults) and added $Project"
    } else {
        $bytes = [System.IO.File]::ReadAllBytes($file)
        $text = [System.IO.File]::ReadAllText($file)
        $json = $text | ConvertFrom-Json
        $same = @($json.projects | Where-Object { $_.name -eq $Project -or ([string]$_.repo).TrimEnd('\') -ieq $Repo.TrimEnd('\') })
        if ($same.Count) { Write-Host "kept     $file ($Project is already in the pool)"; return }
        # Splice the entry in after the last project in the "projects" array.
        $m = [regex]::Match($text, '"projects"\s*:\s*\[(?<body>.*?)\]', 'Singleline')
        if (-not $m.Success) { throw "$file has no projects array" }
        $g = $m.Groups['body']
        $trimmed = $g.Value.TrimEnd()
        $indent = if ($trimmed -match '\n([ \t]+)\S[^\n]*$') { $Matches[1] } else { '    ' }
        $sep = if ($trimmed.Trim()) { ',' } else { '' }
        $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
        $text = $text.Substring(0, $g.Index) + $trimmed + $sep + $nl + $indent + $entry + $nl + '  ' + $text.Substring($g.Index + $g.Length)
        $null = $text | ConvertFrom-Json   # still valid, or stop before writing
        $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        [System.IO.File]::WriteAllText($file, $text, (New-Object System.Text.UTF8Encoding $bom))
        Write-Host "updated  $file (added $Project)"
    }
    Write-Host "         the running pool picks $Project up at its next tick once this project's jobs/ is pushed to main"
}

#
# Action 1's first half: the canonical layout, docs/conventions.md section 1.
#
function Install-CanonicalLayout {
    # Folders every project has, and what each is for (the reasoning is in assets/templates/conventions.md).
    $LayoutDirs = @(
        'assets', 'bin', 'code/extern', 'code/lib', 'code/src', 'code/tools', 'extern/bin', 'out',
        'test/unit', 'test/integration', 'test/data',
        'docs/reference'
    )
    foreach ($d in $LayoutDirs) { Keep $d }
    foreach ($d in @('out/windows', 'extern/bin')) { Keep $d }   # git-ignored, so the .gitkeep stays
    # One folder per module under code/src/, once the design has named them.  OS-specific code goes
    # under code/src/platform/<platform>/ with the shared part in code/src/platform/common/.
    foreach ($m in ($Modules -split ',' | Where-Object { $_.Trim() })) {
        $name = $m.Trim() -replace '[^\w.-]', '_'
        if (-not $name) { continue }
        Keep "code/src/$name"
        if ($name -eq 'platform') { Keep 'code/src/platform/common' }
    }
    # The files that go with the layout.  Only .gitattributes is kit-owned (refreshed with -Update);
    # the rest become the project's own the moment they exist.
    Put '.gitignore'                  (Template 'gitignore')
    Put '.gitattributes'              (Template 'gitattributes') $true
    Put 'README.md'                   ((Template 'readme.md').Replace('{{PROJECT}}', $Project))
    Put 'docs/conventions.md'         ((Template 'conventions.md').Replace('{{PROJECT}}', $Project))
    Put "docs/design/gdd/$Safe.md"           ((Template 'design/gdd.md').Replace('{{PROJECT}}', $Project))
    Put "docs/design/tdd/$Safe-technical.md" ((Template 'design/tdd.md').Replace('{{PROJECT}}', $Project))
}

#
# Action 2: the job queue system.  Run on its own for an existing repository, and by action 1 as
# part of standing up a project.
#
function Install-JobQueue {
    # The queue's folders (its README.md's table says what each holds) and the gaps list.  Done
    # before -Update returns, so a project that lost a folder gets it back.
    foreach ($d in 'queue', 'active', 'review', 'done', 'plans', 'archive', 'requests') { Keep "$Q/$d" }
    Keep 'docs/ai'
    Put 'docs/known_gaps.md' "# Known gaps`n`nKnown differences from the specification, and assumptions made where it is silent.`n"

    # The generic files: refreshed by -Update.
    Put "$Q/README.md"            (Template 'jobs/README.md') $true
    Put "$Q/card-template.md"     (Template 'jobs/card-template.md') $true
    Put "$Q/plan-template.md"     (Template 'jobs/plan-template.md') $true
    Put "$Q/card-template-feature.md" (Template 'jobs/card-template-feature.md') $true
    Put "$Q/card-template-bug.md"     (Template 'jobs/card-template-bug.md') $true
    Put "$Q/attachments/README.md"    (Template 'jobs/attachments/README.md') $true
    Put "$Q/requests/README.md"   (Template 'jobs/requests/README.md') $true
    Put 'docs/ai/README.md'              (Template 'ai/README.md') $true

    if ($Update) {
        # A project with neither categories.json nor the older lanes.json: make categories.json once
        # from scheduler.json's lanes. An existing lanes.json or categories.json, and an old
        # workers.json, are the project's own and stay.
        $old = $null
        $cfgPath = Join-Path $Repo "$Q/scheduler.json"
        if (Test-Path $cfgPath) { try { $old = Get-Content -Raw -Encoding UTF8 -Path $cfgPath | ConvertFrom-Json } catch { } }
        if (-not (Test-Path (Join-Path $Repo "$Q/categories.json")) -and -not (Test-Path (Join-Path $Repo "$Q/lanes.json"))) {
            $ls = [ordered]@{}
            if ($old -and $old.lanes) { foreach ($p in $old.lanes.PSObject.Properties) { $ls[$p.Name] = [pscustomobject]@{ name = [string]$p.Value; purpose = ''; owns = @(); except = @() } } }
            Put "$Q/categories.json" (Categories-Json $ls)
            Write-Host '         fill in each category''s purpose, owns and except in categories.json (project.md''s old table has them), then drop lanes from scheduler.json'
        }
        return
    }

    # Categories: "A=core:src/,test/;B=front end:ui/"
    $catList = @()
    foreach ($part in ($Categories -split ';' | Where-Object { $_.Trim() })) {
        if ($part -notmatch '^\s*([A-Z])\s*=\s*([^:]+?)\s*(?::\s*(.*))?$') { throw "bad category '$part' (want L=name:path,path)" }
        $catList += [pscustomobject]@{ Letter = $Matches[1]; Name = $Matches[2]; Owns = @(($Matches[3] -split ',') | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }) }
    }

    $ls = [ordered]@{}
    foreach ($l in $catList) { $ls[$l.Letter] = [pscustomobject]@{ name = $l.Name; purpose = ''; owns = $l.Owns; except = @() } }
    # An older project keeps its lanes.json (the scheduler reads it when there is no categories.json).
    if (-not (Test-Path (Join-Path $Repo "$Q/lanes.json")) -or $Force) { Put "$Q/categories.json" (Categories-Json $ls) }
    else { Write-Host "kept     $Q/lanes.json (the older name of categories.json)" }
    Put "$Q/models.json" (Template 'jobs/models.json')
    $gates = if ($Gate.Count) { ($Gate | ForEach-Object { '- `' + $_ + '`' }) -join "`n" } else { '- (to be filled in by the planner: every build configuration, and every test suite)' }
    $proj = (Template 'jobs/project.md').Replace('{{PROJECT}}', $Project).Replace('{{GATES}}', $gates).Replace('{{LOCATIONS}}', '').Replace('{{BRIEFS}}', '(none yet)')
    Put "$Q/project.md" $proj

    $envObj = [ordered]@{}
    if ($Endpoint) { $envObj['COPILOT_PROVIDER_BASE_URL'] = '{endpoint}'; $envObj['COPILOT_PROVIDER_TYPE'] = 'openai'; $envObj['COPILOT_MODEL'] = '{model}' }
    $cfg = [ordered]@{
        project = $Project
        jobsDir = $Q; remote = 'origin'; branch = 'main'; port = 8765
        planner = [ordered]@{ outbox = "$Q\.planner-outbox" }
        jobsEnvVar = $JobsEnvVar; workerWindow = 'Hidden'
        worker = [ordered]@{
            cli = 'copilot'; args = @('-p', '{prompt}', '--allow-all', '--no-ask-user', '--output-format=json', '--model={model}')
            model = $Model; endpoint = $Endpoint; env = $envObj
            quotaPatterns = @('exceeded your (monthly )?quota', 'quota exceeded', 'rate limit') }
        agents = [ordered]@{
            claude = [ordered]@{
                cli = 'claude'
                args = @('-p', '{prompt}', '--output-format', 'stream-json', '--verbose', '--include-partial-messages', '--model', '{model}', '--dangerously-skip-permissions')
                format = 'claude'; model = 'claude-sonnet-5-5'
                quotaPatterns = @('usage limit', 'rate limit', 'rate_limit_error', 'credit balance is too low', 'Failed to authenticate') } }
        health = [ordered]@{ command = ($Gate -join ' && ') }
        promptExtra = ''
        limits = [ordered]@{ pollSeconds = 60; maxRetries = 2; stallMinutes = 30; warnMinutes = 20; stopGraceMinutes = 20; maxMinutes = 240 }
    }
    Put "$Q/scheduler.json" ($cfg | ConvertTo-Json -Depth 6)
}

#
# Dispatch.
#
if ($Action -eq 'new-project') {
    Write-Host "== canonical layout in $Repo"
    Install-CanonicalLayout
}
Write-Host "== job queue in $Repo"
Install-JobQueue
# Second pass over every folder Keep made: now the files are in place, a folder that has content
# loses the .gitkeep it was given, and one that lost its files gets one back.
foreach ($d in @($script:Kept.Keys)) { Keep $d }
if (-not $NoPool) {
    Write-Host "== scheduler pool"
    $skip = Get-PoolSkipReason
    if ($skip) { Write-Host "skipped  the pool join: $skip (-JoinPool forces it)" } else { Join-Pool }
}

Write-Host ''
if ($Update) {
    Write-Host "Refreshed the kit's own text in $Repo (the jobs README, the card, plan and request"
    Write-Host 'templates, docs/ai/README.md, .gitattributes) and filled in any folder or file it was missing.'
    Write-Host 'Left alone, because they are the project''s own now: README.md, .gitignore, docs/conventions.md,'
    Write-Host 'docs/design/, jobs/{project,categories,lanes,workers,scheduler}.json.'
    return
}
if ($Action -eq 'new-project') {
    Write-Host "$Project is set up in $Repo with the canonical layout and a job queue. Next:"
    Write-Host '  1. Fill in README.md (what the project is, what is in the repository now) and the **fill in**'
    Write-Host '     sections of docs/conventions.md (the project rules: naming, formatting, errors, logging).'
    Write-Host "  2. Write the design: docs/design/gdd/$Safe.md first (what it must do), then"
    Write-Host "     docs/design/tdd/$Safe-technical.md (how).  Name the modules there, then re-run with"
    Write-Host '     -Action new-project -Modules "app,platform,systems,..." to create code/src/<module>/ folders.'
    $n = 3
} else {
    Write-Host "The job queue is set up in $Repo (jobs/, docs/ai/). It does not touch anything else"
    Write-Host 'in the repository; a project with no layout yet can be given one with -Action new-project.'
    Write-Host 'Next:'
    $n = 1
}
Write-Host "  $n. Fill in jobs/categories.json with the planner: each category's purpose, what it owns and its exceptions, and the shared files."
Write-Host "  $($n + 1). Fill in jobs/project.md (gates, locations, briefs). In jobs/scheduler.json set project (it must equal the name in the pool file) and health.command (the gates, joined with &&)."
Write-Host "  $($n + 2). Write the first plan in docs/ai/ and card it into jobs/queue/."
Write-Host "  $($n + 3). Commit and push to main. The pool joins the project at its next tick; there is no scheduler to start for it."
if ($NoPool) { Write-Host "     (-NoPool was given: add $Project to %LOCALAPPDATA%\ProjectToolkit\pool.json by hand to join the pool.)" }
