# The release gate: which CI runs release.yml accepts. The Azure DevOps REST API is mocked.
# Mocks read $fakeRun and $fakeTimeline from the running test (Pester mocks see the caller's variables).

BeforeAll {
    $script:Gate = Join-Path $PSScriptRoot '..\Assert-ReleaseCandidate.ps1'

    $script:SavedEnv = @{}
    foreach ($name in 'SYSTEM_COLLECTIONURI', 'SYSTEM_TEAMPROJECTID', 'SYSTEM_ACCESSTOKEN') {
        $script:SavedEnv[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    $env:SYSTEM_COLLECTIONURI = 'https://dev.azure.com/contoso/'
    $env:SYSTEM_TEAMPROJECTID = 'project-id'
    $env:SYSTEM_ACCESSTOKEN = 'job-token'

    # A CI run as the REST API returns it: the build, and its timeline with one record per stage.
    function New-CiRun {
        param(
            [string] $Branch = 'refs/heads/main',
            [string] $Status = 'completed',
            [string] $Result = 'succeeded',
            [hashtable] $Stages = @{ Build = 'succeeded'; UITests = 'succeeded'; Package = 'succeeded'; InstallerTests = 'succeeded' }
        )
        $records = @(foreach ($stage in $Stages.GetEnumerator()) { [pscustomobject]@{ type = 'Stage'; identifier = $stage.Key; result = $stage.Value } })
        # Jobs share names with stages; only Stage records may count.
        $records += [pscustomobject]@{ type = 'Job'; identifier = 'InstallerTests'; result = 'succeeded' }
        [pscustomobject]@{ sourceBranch = $Branch; status = $Status; result = $Result; buildNumber = '1.0.42' }
        [pscustomobject]@{ records = $records }
    }

    Mock Invoke-RestMethod -ParameterFilter { $Uri -like '*/_apis/build/builds/42?api-version=7.1' } -MockWith { $fakeRun }
    Mock Invoke-RestMethod -ParameterFilter { $Uri -like '*/_apis/build/builds/42/timeline?api-version=7.1' } -MockWith { $fakeTimeline }
    Mock Write-Host {} # Keeps the ##vso error annotations of failing cases out of the real pipeline log.
}

AfterAll {
    foreach ($entry in $script:SavedEnv.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
}

Describe 'Assert-ReleaseCandidate' {
    It 'accepts a fully succeeded main run whose build and test stages all succeeded' {
        $fakeRun, $fakeTimeline = New-CiRun

        { & $Gate -RunId 42 } | Should -Not -Throw
    }

    It 'reads the run and its timeline from the project with the job token' {
        $fakeRun, $fakeTimeline = New-CiRun

        & $Gate -RunId 42

        Should -Invoke Invoke-RestMethod -Times 2 -Exactly -ParameterFilter {
            $Uri -like 'https://dev.azure.com/contoso/project-id/_apis/build/builds/42*' -and $Headers.Authorization -eq 'Bearer job-token'
        }
    }

    It 'rejects <Case>' -TestCases @(
        @{ Case = 'a feature branch'; Run = @{ Branch = 'refs/heads/feature/login' } }
        @{ Case = 'a release branch (not deployable today)'; Run = @{ Branch = 'refs/heads/release/1.0' } }
        @{ Case = 'a branch that only starts with main'; Run = @{ Branch = 'refs/heads/mainline' } }
        @{ Case = 'a partially succeeded run'; Run = @{ Result = 'partiallySucceeded' } }
        @{ Case = 'a failed run'; Run = @{ Result = 'failed' } }
        @{ Case = 'a canceled run'; Run = @{ Result = 'canceled' } }
        @{ Case = 'a run still in progress'; Run = @{ Status = 'inProgress'; Result = '' } }
        @{ Case = 'skipped UI tests in a green run'; Run = @{ Stages = @{ Build = 'succeeded'; UITests = 'skipped'; Package = 'succeeded'; InstallerTests = 'succeeded' } } }
        @{ Case = 'a missing installer test stage'; Run = @{ Stages = @{ Build = 'succeeded'; UITests = 'succeeded'; Package = 'succeeded' } } }
        @{ Case = 'installer tests that succeeded with issues'; Run = @{ Stages = @{ Build = 'succeeded'; UITests = 'succeeded'; Package = 'succeeded'; InstallerTests = 'succeededWithIssues' } } }
        @{ Case = 'a failed package stage'; Run = @{ Stages = @{ Build = 'succeeded'; UITests = 'succeeded'; Package = 'failed'; InstallerTests = 'succeeded' } } }
    ) {
        $fakeRun, $fakeTimeline = New-CiRun @Run

        { & $Gate -RunId 42 } | Should -Throw 'CI run 42 (1.0.42) is not deployable.'
    }

    It 'accepts another branch only when it is passed as deployable' {
        $fakeRun, $fakeTimeline = New-CiRun -Branch 'refs/heads/release/1.0'

        { & $Gate -RunId 42 -Branches 'refs/heads/main', 'refs/heads/release/*' } | Should -Not -Throw
    }

    It 'reports every problem as a pipeline error, not just the first' {
        $fakeRun, $fakeTimeline = New-CiRun -Branch 'refs/heads/feature/x' -Result 'failed'

        { & $Gate -RunId 42 } | Should -Throw

        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter { $Object -like '##vso`[task.logissue type=error`]*built from refs/heads/feature/x*' }
        Should -Invoke Write-Host -Times 1 -Exactly -ParameterFilter { $Object -like "##vso``[task.logissue type=error``]*result 'failed'*" }
    }
}
