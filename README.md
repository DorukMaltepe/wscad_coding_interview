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
| [tests/DesktopApp.UITests](tests/DesktopApp.UITests) | UI and app tests that drive the published `DesktopApp.exe` through UI Automation (FlaUI). These cover editor features, startup, restart, bad saved data, bad input and crash handling. Every test fails if the app crashed or did not exit cleanly. |
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

### Azure DevOps setup required

- Agent pools `Windows-Build` and `Windows-UI-Interactive`. Configure the UI agent with `--runAsAutoLogon`. Its account must be a local administrator, because the installer tests install into Program Files. Use a disposable or regularly reset machine.
- A pipeline named `DesktopApp-CI` created from `ci.yml`, because `release.yml` refers to it by that name.
- A Build Validation policy that runs `pr.yml` on `main` and `release/*`.
- An environment named `DesktopApp-Production` that has the approvals you want.
- The universal package feed `DesktopApp/desktop-releases`.

## NuGet feed

[nuget.config](nuget.config) points at nuget.org so that the repo restores anywhere. In a real setup, replace it with a single internal Azure Artifacts feed that uses nuget.org as an upstream source, to block dependency confusion. The pipelines already run `NuGetAuthenticate@1`.
