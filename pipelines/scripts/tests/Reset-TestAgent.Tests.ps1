# Resetting the shared test agent. Processes, Windows Installer and msiexec are mocked: nothing is stopped or uninstalled.
# Mocks read $fakeInstaller and $msiexecExitCode from the running test (Pester mocks see the caller's variables).

BeforeAll {
    $script:Reset = Join-Path $PSScriptRoot '..\Reset-TestAgent.ps1'

    # A Windows Installer COM object whose RelatedProducts returns the given product codes and remembers what it was asked.
    function New-Installer([string[]] $ProductCodes = @()) {
        $installer = [pscustomobject]@{ Products = $ProductCodes; AskedFor = $null }
        $installer | Add-Member -MemberType ScriptMethod -Name RelatedProducts -Value {
            param($upgradeCode)
            $this.AskedFor = $upgradeCode
            $this.Products
        }
        $installer
    }

    Mock New-Object -ParameterFilter { $ComObject -eq 'WindowsInstaller.Installer' } -MockWith { $fakeInstaller }
    Mock Get-Process {}
    Mock Stop-Process {}
    Mock Start-Process { [pscustomobject]@{ ExitCode = $msiexecExitCode } }
    Mock Write-Host {}
}

Describe 'Reset-TestAgent' {
    BeforeEach {
        $fakeInstaller = New-Installer
        $msiexecExitCode = 0
    }

    It 'does nothing on a clean agent' {
        & $Reset

        Should -Invoke Stop-Process -Times 0 -Exactly
        Should -Invoke Start-Process -Times 0 -Exactly
    }

    It 'stops a leftover DesktopApp' {
        Mock Get-Process -ParameterFilter { $Name -eq 'DesktopApp' } -MockWith { [pscustomobject]@{ Id = 4242 } }

        & $Reset

        Should -Invoke Stop-Process -Times 1 -Exactly -ParameterFilter { $Id -eq 4242 -and $Force }
    }

    It 'silently uninstalls every installed version' {
        $fakeInstaller = New-Installer '{11111111-1111-1111-1111-111111111111}', '{22222222-2222-2222-2222-222222222222}'

        & $Reset

        Should -Invoke Start-Process -Times 2 -Exactly
        foreach ($code in $fakeInstaller.Products) {
            Should -Invoke Start-Process -Times 1 -Exactly -ParameterFilter {
                $FilePath -eq 'msiexec.exe' -and "$ArgumentList" -eq "/x $code /qn /norestart" -and $Wait -and $PassThru
            }
        }
    }

    It 'accepts msiexec exit code <ExitCode>' -TestCases @(@{ ExitCode = 0 }, @{ ExitCode = 3010 }) {
        $fakeInstaller = New-Installer '{11111111-1111-1111-1111-111111111111}'
        $msiexecExitCode = $ExitCode

        { & $Reset } | Should -Not -Throw
    }

    It 'fails when an uninstall fails, so tests never run on a dirty agent' {
        $fakeInstaller = New-Installer '{11111111-1111-1111-1111-111111111111}'
        $msiexecExitCode = 1603

        { & $Reset } | Should -Throw '*failed with exit code 1603*'
    }

    It 'looks for the UpgradeCode the MSI is built with' {
        # Keeps the script's default in step with installer/DesktopApp.Installer/Package.wxs.
        $wxs = [xml](Get-Content (Join-Path $PSScriptRoot '..\..\..\installer\DesktopApp.Installer\Package.wxs') -Raw)

        & $Reset

        $fakeInstaller.AskedFor | Should -Be "{$($wxs.Wix.Package.UpgradeCode)}"
    }
}
