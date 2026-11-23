# The signature check run after signing in CI and before every deployment.

BeforeAll {
    $script:Check = Join-Path $PSScriptRoot '..\Assert-Signed.ps1'

    Mock Write-Host {} # Keeps the ##vso error annotations of failing cases out of the real pipeline log.

    # A published app folder: our binaries plus a runtime file and a config file.
    function New-AppFolder {
        $folder = Join-Path $TestDrive ([guid]::NewGuid())
        New-Item -ItemType Directory $folder | Out-Null
        foreach ($name in 'DesktopApp.exe', 'DesktopApp.dll', 'DesktopApp.Core.dll', 'coreclr.dll', 'DesktopApp.deps.json') {
            Set-Content -Path (Join-Path $folder $name) -Value 'binary'
        }
        $folder
    }
}

Describe 'Assert-Signed' {
    BeforeAll {
        # What Get-AuthenticodeSignature returns, reduced to the members the script reads.
        function New-Signature([string] $Status = 'Valid', [string] $Signer = 'Contoso', [switch] $NoTimestamp) {
            $certificate = [pscustomobject]@{ Subject = "CN=$Signer, O=$Signer, C=US" }
            $certificate | Add-Member -MemberType ScriptMethod -Name GetNameInfo -Value { $this.Subject -replace '^CN=([^,]+).*$', '$1' }
            [pscustomobject]@{
                Status                 = $Status
                StatusMessage          = "Status is $Status."
                SignerCertificate      = $certificate
                TimeStamperCertificate = $(if ($NoTimestamp) { $null } else { $certificate })
            }
        }

        # Signatures per file name, from $fakeSignatures in the running test.
        Mock Get-AuthenticodeSignature { $fakeSignatures[[IO.Path]::GetFileName("$FilePath")] }
    }

    BeforeEach {
        $folder = New-AppFolder
        $ours = 'DesktopApp.exe DesktopApp*.dll'
        $fakeSignatures = @{ 'DesktopApp.exe' = New-Signature; 'DesktopApp.dll' = New-Signature; 'DesktopApp.Core.dll' = New-Signature }
    }

    It 'accepts files with a valid, timestamped signature from the publisher' {
        { & $Check -Folder $folder -Files $ours } | Should -Not -Throw
    }

    It 'checks exactly the files the space-separated patterns match' {
        & $Check -Folder $folder -Files $ours

        Should -Invoke Get-AuthenticodeSignature -Times 3 -Exactly
        Should -Invoke Get-AuthenticodeSignature -Times 0 -Exactly -ParameterFilter { "$FilePath" -like '*coreclr.dll' -or "$FilePath" -like '*.json' }
    }

    It 'rejects a file whose signature is <Status>' -TestCases @(
        @{ Status = 'NotSigned' }
        @{ Status = 'HashMismatch' }
        @{ Status = 'NotTrusted' }
        @{ Status = 'UnknownError' }
    ) {
        $fakeSignatures['DesktopApp.dll'] = New-Signature -Status $Status

        { & $Check -Folder $folder -Files $ours } | Should -Throw '1 of 3 file(s) failed the signature check.'
        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter { $Object -like "##vso``[task.logissue type=error``]DesktopApp.dll: signature is $Status*" }
    }

    It 'rejects a valid signature from another publisher' {
        $fakeSignatures['DesktopApp.exe'] = New-Signature -Signer 'Fabrikam'

        { & $Check -Folder $folder -Files $ours } | Should -Throw '1 of 3 file(s) failed the signature check.'
        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter { $Object -like '*DesktopApp.exe: signed by *Fabrikam*expected CN=Contoso*' }
    }

    It 'accepts another publisher when it is the expected one' {
        $fakeSignatures['coreclr.dll'] = New-Signature -Signer '.NET'

        { & $Check -Folder $folder -Files 'coreclr.dll' -Publisher '.NET' } | Should -Not -Throw
    }

    It 'rejects a signature without a timestamp' {
        $fakeSignatures['DesktopApp.exe'] = New-Signature -NoTimestamp

        { & $Check -Folder $folder -Files $ours } | Should -Throw '1 of 3 file(s) failed the signature check.'
        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter { $Object -like '*DesktopApp.exe: not timestamped*' }
    }

    It 'fails when no file matches, so an empty artifact never passes' {
        { & $Check -Folder $folder -Files '*.msi' } | Should -Throw "No files matching '*.msi'*"
    }
}

Describe 'Assert-Signed with the real signature check' {
    It 'rejects a real unsigned file' {
        $folder = New-AppFolder
        Set-Content -Path (Join-Path $folder 'Unsigned.ps1') -Value 'Write-Output 1'

        { & $Check -Folder $folder -Files 'Unsigned.ps1' } | Should -Throw '1 of 1 file(s) failed the signature check.'
        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter { $Object -like '*Unsigned.ps1: signature is NotSigned*' }
    }
}
