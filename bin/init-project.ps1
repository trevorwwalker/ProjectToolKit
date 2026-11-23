<#
.SYNOPSIS
    Forwarder: the setup script is projecttoolkit.ps1 now, which has two actions.

.DESCRIPTION
    ProjectToolkit sets up more than a scheduler, so the one conflated script became a dispatcher with
    two independent actions:

      -Action new-project   The canonical layout and its files, and the job queue. Starts a project.
      -Action jobs          The job queue alone; -Update refreshes it from the kit.

    This file exists so an older instruction that names init-project.ps1 - a project's copy of
    a project's jobs/README.md, or a habit - still works. It only forwards. New work should call
    projecttoolkit.ps1 directly.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repo,
    [string]$Action,
    [string]$Project,
    [Alias('Lanes')][string]$Categories,
    [string]$Modules,
    [int]$Slots,
    [string[]]$Gate,
    [string]$Model,
    [string]$Endpoint,
    [string]$JobsEnvVar,
    [switch]$Force,
    [switch]$Update,
    [switch]$JoinPool,
    [switch]$NoPool
)
$use = if ($Action) { $Action } else { 'new-project' }
Write-Host "init-project.ps1 is now projecttoolkit.ps1 -Action $use (or -Action jobs for the queue alone)."
$rest = @{}
foreach ($k in $PSBoundParameters.Keys) { if ($k -ne 'Repo') { $rest[$k] = $PSBoundParameters[$k] } }
$rest['Action'] = $use
& (Join-Path $PSScriptRoot 'projecttoolkit.ps1') -Repo $Repo @rest
