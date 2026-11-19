# DesktopApp

A small WPF desktop app (a demo electrical schematic editor) with a complete Azure DevOps delivery setup. It includes unit, integration and UI tests, a WiX MSI installer, and PR, CI and release pipelines.

The app is a shell, not a real CAD tool. Its UI exists so that there is something realistic to automate. The repo is about how a Windows desktop product gets built, tested, packaged and released.

## What the app does

- **Project explorer**: a page tree. You can add and remove pages. Removing a page that has devices on it asks you to confirm first.
- **Symbol library**: electrical symbols. You can filter them by category or search by name, and insert them on the page.
- **Drawing area**: a page with a grid, snap, zoom and a title block. Placed devices get IEC 81346 designations (`-K1`, `-Q2`, …). With the *Place symbol* tool, you click the page to place a device. With *Select*, you click a device to select it.
- **Device properties**: edit a device's designation, description, manufacturer and article number. Designations are checked for format and uniqueness.
- **Devices / Messages tabs**: a bill-of-materials grid and an activity log. The last message is also shown in the status bar.
- **New project dialog** (Ctrl+N): project name (validated), customer, standard, page format and an optional cover sheet.
- **Ohm's law calculator**: enter a voltage and a resistance to get the current (I = U / R). The last values are remembered in `%LOCALAPPDATA%\DesktopApp\last-circuit.json`.

Unhandled errors are written to `crash.log` in the same folder, and the app shows an error message and exits with code 1. If the environment variable `DESKTOPAPP_DATA_DIR` is set, the app uses that folder instead, which is how tests keep away from real user data. The `--simulate-crash` argument triggers a crash on purpose, so tests can check the crash handling.

Some features have controls but no implementation: Open, Save, Print, Export, wire drawing, project check and options. They log *"… is not available in this version."* Undo and Redo are always disabled.

Every control has an `x:Name`, and WPF uses it as the UI Automation id. Placed devices use the id `Device<designation>`, for example `Device-K1`.

## Repository layout

| Path | Contents |
|------|----------|
| [src/DesktopApp.Core](src/DesktopApp.Core) | Domain logic (`OhmsLaw`, `Circuit`, `Project`, `Symbol`, `Device`, `Designation`). Plain `net10.0`, no UI. |
| [src/DesktopApp.Storage](src/DesktopApp.Storage) | `CircuitStore`, which saves the last circuit as JSON (a corrupt or locked file is ignored, not fatal), and `CrashLog`. |
| [src/DesktopApp](src/DesktopApp) | WPF UI (`net10.0-windows`), published self-contained for `win-x64`. |
| [tests/DesktopApp.Core.Tests](tests/DesktopApp.Core.Tests) | Unit tests (NUnit), one class at a time. |
| [tests/DesktopApp.IntegrationTests](tests/DesktopApp.IntegrationTests) | Integration tests: storage and crash log against the real file system (including locked, read-only and corrupt files), and editor workflows across `Project`, `SymbolLibrary` and `Designation`. |
| [tests/DesktopApp.UITests](tests/DesktopApp.UITests) | UI and app tests that drive the published `DesktopApp.exe` through UI Automation (FlaUI). These cover editor features, startup, restart, bad saved data, bad input, crash handling, and telemetry with a malformed or unreachable endpoint. Every test fails if the app crashed or did not exit cleanly. |
| [tests/DesktopApp.Installer.Tests](tests/DesktopApp.Installer.Tests) | Installer tests: the MSI's tables, its extracted files compared with the published app, and a real install, repair and uninstall cycle (admin only). |
| [installer/DesktopApp.Installer](installer/DesktopApp.Installer) | WiX v7 MSI. It harvests the published app folder. It is not part of the solution. |
| [pipelines](pipelines) | Azure Pipelines YAML for PR, CI and release. |

Package versions are managed centrally in [Directory.Packages.props](Directory.Packages.props). Nullable and implicit usings are switched on for every project in [Directory.Build.props](Directory.Build.props).

## Prerequisites

- Windows. The app and UI tests need WPF and a desktop session.
- The .NET SDK pinned in [global.json](global.json) (11.0.100, with roll-forward to the latest feature band).
- The .NET 10 runtime. The projects target `net10.0`, so the test hosts need it.

## Build and run

```powershell
dotnet build DesktopApp.slnx
dotnet run --project src/DesktopApp
```

## Tests

```powershell
dotnet test tests/DesktopApp.Core.Tests
dotnet test tests/DesktopApp.IntegrationTests
```

The UI and app tests launch a real exe. Each test gets its own temp data folder. Publish the app first, then point `DESKTOPAPP_EXE` at it:

```powershell
dotnet publish src/DesktopApp -c Release -o artifacts/app
$env:DESKTOPAPP_EXE = "$PWD\artifacts\app\DesktopApp.exe"
dotnet test tests/DesktopApp.UITests
```

## Installer

By default the installer project reads the app from `artifacts/app`. Publish the app as shown above, then run:

```powershell
dotnet build installer/DesktopApp.Installer/DesktopApp.Installer.wixproj -c Release -p:AppVersion=1.0.0
```

The output is `DesktopApp-<version>.msi`. It installs to `Program Files\DesktopApp`, and newer versions replace older ones through a major upgrade. Downgrades are blocked. Never change the `UpgradeCode` in [Package.wxs](installer/DesktopApp.Installer/Package.wxs). A test fails if it changes.

To test the MSI:

```powershell
$env:DESKTOPAPP_MSI = "$PWD\artifacts\installer\DesktopApp-1.0.0.msi"   # built with -o artifacts/installer
$env:DESKTOPAPP_APP_DIR = "$PWD\artifacts\app"                          # the folder the MSI was built from
$env:DESKTOPAPP_VERSION = "1.0.0"                                         # optional
dotnet test tests/DesktopApp.Installer.Tests
```

Without admin rights, the real install tests are skipped. In an elevated shell, they install the MSI into Program Files and uninstall it again, so run them only on a machine where that is fine.

> WiX v7 requires accepting the OSMF EULA (`AcceptEula` in the `.wixproj`). Revenue-generating companies have to pay the Open Source Maintenance Fee. Clear this with legal or procurement before you use it commercially.

## Pipelines

```
PR ──► pr.yml        build + unit + integration tests (branch policy on main, release/*)

merge ──► ci.yml     Build ──► UITests ──► Package (MSI) ──► InstallerTests
                     (main, release/*)

CI run on release/* ──► release.yml   approval on DesktopApp-Production ──► universal package
```

- **[pr.yml](pipelines/pr.yml)**: Azure Repos ignores YAML `pr:` triggers, so this pipeline is wired up as a **Build Validation branch policy**. UI tests are left out because the interactive agent is scarce.
- **[ci.yml](pipelines/ci.yml)**: It builds once and versions the build as `1.0.<rev>`. It publishes the `app` and `uitests` artifacts and then runs the UI tests against those exact binaries on the `Windows-UI-Interactive` pool. That pool's agent runs in an auto-logged-on interactive session, not as a service. The MSI is built only after the UI tests pass. It is then tested on the same pool: its tables are checked, its files are compared with the `app` artifact, and it is installed, repaired and uninstalled for real. If the installer tests fail, the CI run fails, so the MSI is never released.
- **[release.yml](pipelines/release.yml)**: It is triggered by a successful CI run on `release/*`. Nothing is rebuilt: the tested installer is published as a universal package to the `DesktopApp/desktop-releases` feed. Approvals are configured on the `DesktopApp-Production` environment.
- **[templates/build-and-test.yml](pipelines/templates/build-and-test.yml)**: These are the restore, build, unit test and integration test steps. PR and CI share them, so a PR is checked the same way that main is built.

### Quality checks

- **Warnings are errors.** The pipeline builds with `-warnaserror`, so any compiler or .NET analyzer warning fails the PR. Local builds stay lenient.
- **Code coverage.** The unit and integration tests run with [coverage.runsettings](coverage.runsettings), which uses Microsoft.CodeCoverage (already part of `Microsoft.NET.Test.Sdk`) and leaves the test assemblies out of the numbers. Results appear on the run's *Code Coverage* tab.
- **Diff coverage on PRs.** [azurepipelines-coverage.yml](azurepipelines-coverage.yml) makes Azure Repos post a `codecoverage` status that requires 70 % of the changed lines to be covered. To block merges below that, make the status required in the branch policy.

### Security checks

- **Vulnerable packages.** Restore runs with `-warnaserror`, so NuGet audit fails the PR if any direct or transitive package has a known advisory (NU1901–NU1904). Run `dotnet list package --vulnerable --include-transitive` to see the details.
- **Static analysis (SAST).** CI runs CodeQL for C# around the build (`codeAnalysis: true`). Findings appear under *Repos → Advanced Security*. PRs skip it to keep them fast.
- **Secrets.** Turn on secret scanning with push protection in the repo's Advanced Security settings, so a committed key is blocked before it reaches the server. There is no pipeline step for this.

Both CodeQL and secret scanning need GitHub Advanced Security for Azure DevOps enabled on the repo. NuGet audit needs nothing extra.

### Code signing

CI signs twice with [templates/sign.yml](pipelines/templates/sign.yml), which runs Microsoft's `sign` CLI against Azure Trusted Signing:

1. **App binaries** (`DesktopApp.exe`, `DesktopApp*.dll`) right after `dotnet publish`, before the `app` artifact is published. The UI tests, the MSI and the installer payload tests all see the signed files. The .NET runtime files in the self-contained output are already signed by Microsoft and are left alone.
2. **The MSI**, right after it is built and before the installer tests. Release publishes exactly this signed MSI.

The private key stays in Azure and the agent holds no secret: the `DesktopApp-CodeSigning` service connection uses workload identity federation. Signatures are timestamped, so they stay valid after the certificate expires. PR builds never sign, so code that has not been reviewed is never signed.

### Azure DevOps setup required

- Agent pools `Windows-Build` and `Windows-UI-Interactive`. Configure the UI agent with `--runAsAutoLogon`. Its account must be a local administrator, because the installer tests install into Program Files. Use a disposable or regularly reset machine.
- A pipeline named `DesktopApp-CI` created from `ci.yml`, because `release.yml` refers to it by that name.
- A Build Validation policy that runs `pr.yml` on `main` and `release/*`. Optionally, also a required status policy for `codecoverage`.
- An environment named `DesktopApp-Production` that has the approvals you want.
- GitHub Advanced Security for Azure DevOps enabled on the repo (CodeQL and secret scanning).
- An Azure Trusted Signing account `desktopapp-signing` with certificate profile `desktopapp-public`, plus an Azure Resource Manager service connection `DesktopApp-CodeSigning` (workload identity federation) whose identity has the *Trusted Signing Certificate Profile Signer* role. Allow only `DesktopApp-CI` to use the connection, and add a branch control check for `main` and `release/*`.
- The universal package feed `DesktopApp/desktop-releases`.
- A variable group `DesktopApp-Telemetry` with `AppInsightsConnectionString` (see [Telemetry](#telemetry-application-insights)).

## Telemetry (Application Insights)

The app sends telemetry to Azure Application Insights through OpenTelemetry and the Azure Monitor exporter ([Telemetry.cs](src/DesktopApp/Telemetry.cs)). If it is offline, telemetry is kept on disk and sent on a later start.

| What | Where in App Insights |
|------|-----------------------|
| Crashes (unhandled exceptions, with stack trace) | `exceptions`, `severityLevel == 4` |
| Handled errors (e.g. the last circuit could not be saved), error type only | `traces`, `severityLevel == 2` |
| `SessionStarted` (app version, OS, .NET, CPU count, RAM) and `SessionEnded` (exit code, duration) | `customEvents` |
| `FeatureRequested`: someone used a feature that is not implemented | `customEvents` |
| CPU % (`desktopapp.process.cpu.percent`), startup time, devices placed per symbol | `customMetrics` |
| .NET runtime: working set, GC, heap, thread pool, lock contention, exceptions (`dotnet.*`) | `customMetrics` |

Every run gets a random session id as `cloud_RoleInstance`. The machine name is not sent, and neither are project or customer names. Handled errors send only the error type. Crashes send the full exception, because the stack trace is needed to fix them. A crash message can contain a local file path, and with it the Windows user name. Say so in the privacy statement.

**Turning it on.** Telemetry is off unless there is a connection string:
- `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable (wins), or
- built in: the MSBuild property `AppInsightsConnectionString`. CI sets it from the `DesktopApp-Telemetry` variable group.

`DESKTOPAPP_TELEMETRY_OPTOUT=1` (or `true`, any case) turns it off. The UI and installer tests set it, so test runs and simulated crashes never reach production data. The exception is [TelemetryTests](tests/DesktopApp.UITests/TelemetryTests.cs): it turns telemetry on against a malformed connection string and against a localhost port that refuses connections. It checks that the app still starts, exits cleanly and logs crashes, and that unsent data is kept on disk.

**Azure side.** [monitoring/main.bicep](monitoring/main.bicep) creates a Log Analytics workspace, Application Insights and a **DesktopApp health** workbook. The workbook shows sessions, crash-free sessions per version, crashes, top errors, CPU, memory, startup time, versions/OS, requested features and symbol usage. Pin any tile to an Azure dashboard.

```powershell
az deployment group create -g <resource-group> -f monitoring/main.bicep --query properties.outputs.connectionString.value
```

Put the output into the `AppInsightsConnectionString` variable of a variable group named `DesktopApp-Telemetry` (CI needs that group). To try it locally, set `APPLICATIONINSIGHTS_CONNECTION_STRING` and run the app.

## NuGet feed

[nuget.config](nuget.config) points at nuget.org so that the repo restores anywhere. In a real setup, replace it with a single internal Azure Artifacts feed that uses nuget.org as an upstream source, to block dependency confusion. The pipelines already run `NuGetAuthenticate@1`.
