<#
.SYNOPSIS
  Puts the shared interactive test agent back into a known state: no running DesktopApp, no installed DesktopApp.
  Runs at the start of every UI and installer test job, because a cancelled or timed-out job skips the tests' cleanup.
#>
param(
    # The MSI's UpgradeCode (Package.wxs). It finds every installed version, whatever its ProductCode.
    [string] $UpgradeCode = '{E78657E3-6E16-4A06-A699-B8E469E2843E}'
)

$ErrorActionPreference = 'Stop'

Get-Process -Name DesktopApp -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping leftover DesktopApp (PID $($_.Id))."
    $_ | Stop-Process -Force
}

$installer = New-Object -ComObject WindowsInstaller.Installer
foreach ($productCode in @($installer.RelatedProducts($UpgradeCode))) {
    Write-Host "Uninstalling leftover DesktopApp $productCode."
    $msiexec = Start-Process msiexec.exe -ArgumentList "/x $productCode /qn /norestart" -Wait -PassThru
    if ($msiexec.ExitCode -notin 0, 3010) { throw "msiexec /x $productCode failed with exit code $($msiexec.ExitCode)." }
}
Write-Host 'Test agent is clean.'
