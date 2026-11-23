# DesktopApp

A demo WPF electrical schematic editor with a complete Azure DevOps delivery setup: unit, integration and UI tests, a WiX MSI, and PR, CI and release pipelines. The app exists so the pipelines have something realistic to build, test and ship.

**Why it is built this way:** [CONCEPT.md](CONCEPT.md). This README only covers setup and running.

## Repository layout

| Path | Contents |
|------|----------|
| [src/DesktopApp.Core](src/DesktopApp.Core) | Domain logic, no UI (`net10.0`) |
| [src/DesktopApp.Storage](src/DesktopApp.Storage) | Saved circuit (JSON) and crash log |
| [src/DesktopApp](src/DesktopApp) | WPF app (`net10.0-windows`, self-contained `win-x64`) |
| [tests](tests) | `Core.Tests` (unit), `IntegrationTests`, `UITests` (FlaUI, drives the published exe), `Installer.Tests` (MSI tables, payload, real install) |
| [installer/DesktopApp.Installer](installer/DesktopApp.Installer) | WiX v7 MSI built from the published app (not part of the solution) |
| [pipelines](pipelines) | `pr.yml`, `ci.yml`, `release.yml`, shared `templates/`, and `scripts/` with Pester tests |
| [monitoring/main.bicep](monitoring/main.bicep) | Application Insights and the health workbook |

## Prerequisites

- Windows with a desktop session (WPF and UI tests need one).
- The .NET SDK pinned in [global.json](global.json), plus the .NET 10 runtime (the projects target `net10.0`).

## Build and run

```powershell
dotnet build DesktopApp.slnx
dotnet run --project src/DesktopApp
```

The app keeps its last values and `crash.log` in `%LOCALAPPDATA%\DesktopApp`.

| Setting | Effect |
|---|---|
| `DESKTOPAPP_DATA_DIR` | Use this folder for app data instead (tests use temp folders) |
| `--simulate-crash` | Crash right after start, to test crash handling |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Send telemetry there (see [Telemetry](#telemetry)) |
| `DESKTOPAPP_TELEMETRY_OPTOUT=1` | Never send telemetry |

## Tests

```powershell
# Unit and integration tests
dotnet test tests/DesktopApp.Core.Tests
dotnet test tests/DesktopApp.IntegrationTests

# UI tests drive the published exe, so publish first. They take over mouse and keyboard while they run.
dotnet publish src/DesktopApp -c Release -o artifacts/app
$env:DESKTOPAPP_EXE = "$PWD\artifacts\app\DesktopApp.exe"
dotnet test tests/DesktopApp.UITests

# Pipeline script tests (Windows PowerShell 5.1; fetches Pester 5.7.1 if it is missing)
powershell -NoProfile -File pipelines/scripts/tests/Invoke-Tests.ps1
```

UI tests find controls by their `x:Name`, which WPF uses as the UI Automation id. Placed devices are `Device<designation>`, for example `Device-K1`. A failed UI test gets a screenshot and the crash log attached.

## Installer

Publish the app as above, then:

```powershell
dotnet build installer/DesktopApp.Installer/DesktopApp.Installer.wixproj -c Release -p:AppVersion=1.0.0 -o artifacts/installer
```

This packages `artifacts/app` into `artifacts/installer/DesktopApp-1.0.0.msi`. Never change the `UpgradeCode` in [Package.wxs](installer/DesktopApp.Installer/Package.wxs): it ties all versions together for upgrades, and a test fails if it changes.

To test the MSI:

```powershell
$env:DESKTOPAPP_MSI = "$PWD\artifacts\installer\DesktopApp-1.0.0.msi"
$env:DESKTOPAPP_APP_DIR = "$PWD\artifacts\app"   # the folder the MSI was built from
$env:DESKTOPAPP_VERSION = "1.0.0"                # optional
dotnet test tests/DesktopApp.Installer.Tests
```

The real install, repair and uninstall tests only run in an elevated shell, and they change Program Files. Without admin rights they are skipped.

> WiX v7 requires accepting the OSMF EULA (`AcceptEula` in the `.wixproj`), and revenue-generating companies must pay the Open Source Maintenance Fee. Clear this with legal or procurement first.

## Azure DevOps setup

The pipelines need the following. All names are placeholders.

1. **Agent pools**
   - `Windows-PR`: separate machines, because it runs unreviewed code. Ideally reimaged after each job.
   - `Windows-Build`: builds and signs.
   - `Windows-UI-Interactive`: agent configured with `--runAsAutoLogon` and an account that is a local administrator (the installer tests install into Program Files). Use a disposable or regularly reset VM.
2. **Pipelines** from `pr.yml`, `ci.yml` and `release.yml`. Name the CI pipeline `DesktopApp-CI`, because `release.yml` refers to it by that name.
3. **Branch policy on `main`:** Build Validation running `pr.yml` with the path filter `/*; !*.md; !/monitoring/*`. Optionally make the `codecoverage` status required.
4. **Environment `DesktopApp-Production`** with these checks:
   - Approvals, with *approvers can't approve their own runs*.
   - Branch control allowing only `refs/heads/main`, with *Verify branch protection*.
   - Exclusive lock.

   Environments added later (`DesktopApp-Test`, `DesktopApp-PrP`) get the same branch control.
5. **Code signing:**
   - An Azure Trusted Signing account `desktopapp-signing` with certificate profile `desktopapp-public`.
   - An Azure Resource Manager service connection `DesktopApp-CodeSigning` that uses workload identity federation and has the *Trusted Signing Certificate Profile Signer* role.
   - Allow only `DesktopApp-CI` to use the connection, and add a branch control check for `main`.
6. **Artifacts:** the universal package feed `DesktopApp/desktop-releases` (only the release pipeline may publish to it) and the Azure Artifacts symbol server.
7. **Permissions:** *Create tag* on the repository for the project's build service identity, so the release can tag `v<version>`.
8. **Security:** GitHub Advanced Security for Azure DevOps on the repo, for CodeQL and secret scanning with push protection.
9. **Variable group `DesktopApp-Telemetry`** with `AppInsightsConnectionString` (see below).

[nuget.config](nuget.config) points at nuget.org so the repo restores anywhere. For production, replace it with an internal Azure Artifacts feed that has nuget.org as its upstream.

## Telemetry

Telemetry is off unless a connection string is set. To create the Azure side:

```powershell
az deployment group create -g <resource-group> -f monitoring/main.bicep --query properties.outputs.connectionString.value
```

Put the output into `AppInsightsConnectionString` in the `DesktopApp-Telemetry` variable group, and CI builds it into the app. To try it locally, set `APPLICATIONINSIGHTS_CONNECTION_STRING` and run the app. The **DesktopApp health** workbook in the resource group shows sessions, crash-free sessions per version, errors and feature usage.

Crash reports include the stack trace, which can contain a local path and with it the Windows user name. Mention this in the privacy statement.
