<#
.SYNOPSIS
  Creates the annotated tag v<version> on the commit a release was built from, so every shipped version maps to
  its source. Uses the Git REST API with the job token (SYSTEM_ACCESSTOKEN must be mapped in), so the agent needs
  neither the commit in a shallow clone nor push credentials. Fails if the tag already exists.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Commit
)

$ErrorActionPreference = 'Stop'

$uri = "$($env:SYSTEM_COLLECTIONURI)$($env:SYSTEM_TEAMPROJECTID)/_apis/git/repositories/$($env:BUILD_REPOSITORY_ID)/annotatedtags?api-version=7.1"
$body = @{
    name         = "v$Version"
    taggedObject = @{ objectId = $Commit }
    message      = "DesktopApp $Version, released by pipeline run $env:BUILD_BUILDNUMBER."
} | ConvertTo-Json

$tag = Invoke-RestMethod -Method Post -Uri $uri -Headers @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN" } -ContentType 'application/json' -Body $body
Write-Host "Tagged $Commit as $($tag.name)."
