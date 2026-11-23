<#
.SYNOPSIS
  Fails unless a CI run may be deployed: it ran on a deployable branch, finished with result "succeeded"
  (not "partiallySucceeded"), and every required stage ran and succeeded (none skipped).
  Reads the run through the Azure DevOps REST API with the job token, so SYSTEM_ACCESSTOKEN must be mapped in.
#>
param(
    [Parameter(Mandatory)] [int] $RunId,
    # Branch patterns (PowerShell -like) whose runs may be deployed.
    [string[]] $Branches = @('refs/heads/main'),
    # ci.yml stage names. Together they prove the run was built, signed, and passed unit, integration, UI and installer tests.
    [string[]] $Stages = @('Build', 'UITests', 'Package', 'InstallerTests')
)

$ErrorActionPreference = 'Stop'

$runUri = "$($env:SYSTEM_COLLECTIONURI)$($env:SYSTEM_TEAMPROJECTID)/_apis/build/builds/$RunId"
$headers = @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN" }
$run = Invoke-RestMethod -Uri "$($runUri)?api-version=7.1" -Headers $headers
$timeline = Invoke-RestMethod -Uri "$($runUri)/timeline?api-version=7.1" -Headers $headers

$problems = @()
if (-not ($Branches | Where-Object { $run.sourceBranch -like $_ })) {
    $problems += "CI run $RunId was built from $($run.sourceBranch). Only $($Branches -join ', ') may be deployed."
}
if ($run.status -ne 'completed' -or $run.result -ne 'succeeded') {
    $problems += "CI run $RunId is '$($run.status)' with result '$($run.result)'. Only runs that fully succeeded may be deployed."
}
foreach ($stage in $Stages) {
    $record = $timeline.records | Where-Object { $_.type -eq 'Stage' -and $_.identifier -eq $stage } | Select-Object -First 1
    if (-not $record) { $problems += "CI run $RunId has no stage '$stage'." }
    elseif ($record.result -ne 'succeeded') { $problems += "Stage '$stage' of CI run $RunId did not succeed: $($record.result)." }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "##vso[task.logissue type=error]$_" }
    throw "CI run $RunId ($($run.buildNumber)) is not deployable."
}
Write-Host "CI run $RunId ($($run.buildNumber)) from $($run.sourceBranch) is deployable: stages $($Stages -join ', ') succeeded."
