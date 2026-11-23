<#
.SYNOPSIS
  Runs the Pester tests of the pipeline scripts in Windows PowerShell 5.1, the shell the pipelines run them in.
  Uses Pester 5.7.1. If it is not installed, downloads that exact package from the PowerShell Gallery and checks its
  SHA-256 first (no PowerShellGet or NuGet provider needed, nothing installed on the machine).
.EXAMPLE
  powershell -NoProfile -File pipelines/scripts/tests/Invoke-Tests.ps1
#>
param(
    # Writes NUnit XML results here when set (for PublishTestResults).
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$version = '5.7.1'
$sha256 = '4A27904C6814A5FBE4758F8E49861F6A1994AEE77B71165A5C43C0371BA6C580'

$module = Get-Module -ListAvailable Pester | Where-Object Version -EQ $version | Select-Object -First 1 -ExpandProperty Path
if (-not $module) {
    # shortcut: pulls from the public gallery; with an internal feed, point the URL at its PowerShell upstream instead.
    $folder = Join-Path ([IO.Path]::GetTempPath()) "Pester.$version"
    $module = Join-Path $folder 'Pester.psd1'
    if (-not (Test-Path $module)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $package = "$folder.zip"
        Invoke-WebRequest -Uri "https://www.powershellgallery.com/api/v2/package/Pester/$version" -OutFile $package -UseBasicParsing
        $actual = (Get-FileHash -Path $package -Algorithm SHA256).Hash
        if ($actual -ne $sha256) { throw "Pester $version package hash is $actual, expected $sha256." }
        Expand-Archive -Path $package -DestinationPath $folder -Force
    }
}
Import-Module $module -Force

$config = New-PesterConfiguration
$config.Run.Path = $PSScriptRoot
$config.Run.Exit = $true # Exit code = number of failed tests, so the pipeline step fails.
$config.Output.Verbosity = 'Detailed'
if ($ResultPath) {
    $config.TestResult.Enabled = $true
    $config.TestResult.OutputFormat = 'NUnitXml'
    $config.TestResult.OutputPath = $ResultPath
}
Invoke-Pester -Configuration $config
