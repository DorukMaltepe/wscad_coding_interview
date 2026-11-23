<#
.SYNOPSIS
  Fails unless every matching file has a valid, timestamped Authenticode signature from our publisher.
  Runs right after signing in CI, and again in release.yml before anything is deployed.
.EXAMPLE
  Assert-Signed.ps1 -Folder artifacts/app -Files 'DesktopApp.exe DesktopApp*.dll'
#>
param(
    [Parameter(Mandatory)] [string] $Folder,
    # File names or wildcards in the folder, separated by spaces (the same format as the sign template's `files`).
    [Parameter(Mandatory)] [string] $Files,
    # Placeholder: the CN of the Trusted Signing certificate profile `desktopapp-public`.
    [string] $Publisher = 'Contoso'
)

$ErrorActionPreference = 'Stop'

$found = @($Files -split '\s+' | Where-Object { $_ } | ForEach-Object { Get-ChildItem -Path $Folder -Filter $_ -File })
if ($found.Count -eq 0) { throw "No files matching '$Files' in $Folder." }

$problems = @(foreach ($file in $found) {
    $signature = Get-AuthenticodeSignature -FilePath $file.FullName
    if ($signature.Status -ne 'Valid') {
        "$($file.Name): signature is $($signature.Status). $($signature.StatusMessage)"
    }
    elseif ($signature.SignerCertificate.GetNameInfo('SimpleName', $false) -ne $Publisher) {
        "$($file.Name): signed by '$($signature.SignerCertificate.Subject)', expected CN=$Publisher."
    }
    elseif (-not $signature.TimeStamperCertificate) {
        # Trusted Signing certificates live for days: without a timestamp the signature soon stops being valid.
        "$($file.Name): not timestamped."
    }
    else {
        Write-Host "$($file.Name): signed by $Publisher and timestamped."
    }
})

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "##vso[task.logissue type=error]$_" }
    throw "$($problems.Count) of $($found.Count) file(s) failed the signature check."
}
