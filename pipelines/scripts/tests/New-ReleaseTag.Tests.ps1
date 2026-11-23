# Tagging the released commit. The Git REST API is mocked.

BeforeAll {
    $script:Tag = Join-Path $PSScriptRoot '..\New-ReleaseTag.ps1'

    $script:SavedEnv = @{}
    foreach ($name in 'SYSTEM_COLLECTIONURI', 'SYSTEM_TEAMPROJECTID', 'SYSTEM_ACCESSTOKEN', 'BUILD_REPOSITORY_ID', 'BUILD_BUILDNUMBER') {
        $script:SavedEnv[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    $env:SYSTEM_COLLECTIONURI = 'https://dev.azure.com/contoso/'
    $env:SYSTEM_TEAMPROJECTID = 'project-id'
    $env:SYSTEM_ACCESSTOKEN = 'job-token'
    $env:BUILD_REPOSITORY_ID = 'repo-id'
    $env:BUILD_BUILDNUMBER = '20261123.1'

    Mock Write-Host {}
}

AfterAll {
    foreach ($entry in $script:SavedEnv.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
}

Describe 'New-ReleaseTag' {
    It 'creates the annotated tag "v" + version on the released commit' {
        Mock Invoke-RestMethod { [pscustomobject]@{ name = 'v1.0.42' } }

        & $Tag -Version 1.0.42 -Commit 0123abcd

        Should -Invoke Invoke-RestMethod -Times 1 -Exactly -ParameterFilter {
            $request = $Body | ConvertFrom-Json
            $Method -eq 'Post' -and
            $Uri -eq 'https://dev.azure.com/contoso/project-id/_apis/git/repositories/repo-id/annotatedtags?api-version=7.1' -and
            $Headers.Authorization -eq 'Bearer job-token' -and
            $ContentType -eq 'application/json' -and
            $request.name -eq 'v1.0.42' -and
            $request.taggedObject.objectId -eq '0123abcd' -and
            $request.message -like '*1.0.42*20261123.1*'
        }
    }

    It 'fails when the API refuses, e.g. because the tag already exists' {
        Mock Invoke-RestMethod { throw 'TF401289: The tag v1.0.42 already exists.' }

        { & $Tag -Version 1.0.42 -Commit 0123abcd } | Should -Throw '*already exists*'
    }
}
