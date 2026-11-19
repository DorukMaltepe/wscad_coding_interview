# DesktopApp CI/CD – Concept

## 1. Overall flow

```
feature/* ──PR──► main, release/*            pr.yml (branch policy): restore + audit, build -warnaserror,
                                              unit + integration tests, coverage. No signing, no UI tests.
merge ──► ci.yml  "DesktopApp-CI"  version 1.0.<rev>
          Build ──────────► UITests ──────────────► Package ──────────► InstallerTests
          Windows-Build      Windows-UI-Interactive   Windows-Build       Windows-UI-Interactive
          + CodeQL, publish  tests the exact          MSI from the app    MSI tables, payload vs. app,
          + sign app         signed binaries          artifact + sign     install / repair / uninstall
CI run on release/* succeeded ──► release.yml: approval on DesktopApp-Production ──► signed MSI to feed
```

There are three pipelines with separate triggers and permissions: **validation** (`pr.yml`), **CI** (`ci.yml`) and **release** (`release.yml`). PR and CI share `templates/build-and-test.yml`, so a PR is checked exactly the way main is built. CI and release share `templates/sign.yml`.

## 2. Key design decisions

| Decision | Why | Trade-off |
|---|---|---|
| **Build once, promote the same bits.** The MSI is built from the `app` artifact, and release only re-publishes the tested MSI. | What was tested is what ships. No "works in CI, broken in release" rebuilds. | Artifacts of release runs must be retained. |
| **UI tests run after merge, not on PRs.** | The interactive agent is scarce and slow. PRs stay fast (minutes). | A UI regression can reach main. It is still caught before an MSI exists, then fixed or reverted. |
| **Sign before the artifact is published.** | UI tests, the MSI and the installer tests all run on the signed files, so signing problems surface in CI. | Every CI build uses the signing service, not only releases. |
| **Real install tests on the interactive agent.** | Catches upgrade, file and repair problems that table checks miss. | The agent needs admin rights and a resettable machine. |
| **Gates fail closed.** `-warnaserror` on build and restore (NuGet audit), and `DESKTOPAPP_REQUIRE_ELEVATED` so a skipped install test cannot pass. | Silent passes are worse than red builds. | Occasional friction when an analyzer or advisory is added upstream. |
| **Self-contained `win-x64` publish.** | No .NET runtime prerequisite on customer machines. | Bigger installer, and runtime patches need a new release. |

## 3. Branching, pull request and release strategy

- **Trunk-based with short-lived feature branches** into `main`, and **`release/x.y`** branches cut for stabilisation. Fixes land on `main` first and are cherry-picked into the release branch.
- **Branch policies on `main` and `release/*`:** at least one reviewer, linked work item, resolved comments, squash merge, `pr.yml` as Build Validation (Azure Repos ignores YAML `pr:` triggers), and the `codecoverage` status (70 % of changed lines, `azurepipelines-coverage.yml`) as required.
- **CI** triggers on `main` and `release/*` with `batch: true`, so a burst of merges produces one run instead of queueing the UI agent.
- **Release:** every green CI run on `release/*` is a release candidate. `release.yml` is triggered by that run through a pipeline resource and waits for approval on the `DesktopApp-Production` environment. Main builds are never released.
- **Versioning:** the run name `1.0.<rev>` becomes the assembly version, the MSI version and the package version, so one number traces a customer's install back to a commit. `majorMinor` is bumped when a release branch is cut.

## 4. Build, testing, packaging and artifacts

- **Restore and build:** central package versions (`Directory.Packages.props`), SDK pinned in `global.json`, NuGet audit on restore, CodeQL on CI builds only (too slow for PRs).
- **Tests:** NUnit unit and integration tests with coverage on the build agent. FlaUI UI and app-lifecycle tests, then installer tests, on the interactive agent. Results are published with `PublishTestResults@2`, also when tests fail.
- **Packaging:** the WiX MSI harvests the whole `app` artifact, so new files need no installer change. The `UpgradeCode` is fixed (a test enforces it), major upgrades replace old versions, downgrades are blocked.
- **Artifacts:** `app`, `uitests`, `installertests` and `installer`. The test artifacts let interactive stages run without checkout or rebuild. Released MSIs go to the universal feed `desktop-releases`, the hand-off point to distribution.

## 5. Secrets, permissions and service connections

- **No secrets in the repo or on agents.** Signing uses Azure Trusted Signing: the key stays in Azure, and the `DesktopApp-CodeSigning` service connection uses workload identity federation (no stored client secret). It is restricted to `DesktopApp-CI` with a branch control check for `main` and `release/*`, and PR builds never sign, so unreviewed code is never signed.
- **Feeds:** `NuGetAuthenticate@1` uses the job token. Only the release pipeline may publish to `desktop-releases`; everyone else reads.
- **Telemetry connection string** comes from the `DesktopApp-Telemetry` variable group. It is an ingestion key that ends up in the binary anyway, so it is configuration, not a secret. Real secrets would go in a Key Vault-linked variable group.
- **Secret scanning with push protection** (GitHub Advanced Security) blocks committed keys before they reach the server.

## 6. Software protection, licensing, installer and updates

- **Obfuscation / protection** goes in the Build stage **after `dotnet publish`, before signing**: it changes the binaries, so it must precede the signature, and the UI tests then run on the protected build that ships.
- **Licensing:** the app holds only license verification (public key or vendor SDK). The license *signing* key stays in the licensing backend or an HSM, never in a pipeline. Tests get a test license through an environment variable, like `DESKTOPAPP_DATA_DIR` today.
- **Installer:** the signed MSI is the release unit. A WiX Burn bundle is only needed if prerequisites appear (e.g. a license-dongle driver).
- **Updates:** a production release stage would upload the MSI to blob storage or a CDN and publish an update manifest (version, URL, SHA-256) that the app checks on start, rolled out in rings (internal → beta → GA) as separate environments with their own approvals.

## 7. Logging, telemetry and monitoring

The app sends crashes, handled errors, session events, startup time, CPU, memory and feature usage to Application Insights through OpenTelemetry (`Telemetry.cs`); `monitoring/main.bicep` deploys it with a *DesktopApp health* workbook. Privacy: random session id, no machine, project or customer names, opt-out via `DESKTOPAPP_TELEMETRY_OPTOUT`, and test runs opt out.

- **Crash-free sessions per version** shows within hours whether a release is worse than the previous one, and can **gate ring promotion** through an Azure Monitor alert check on the next ring's environment.
- **Pipeline side:** test trends and flaky-test detection in Azure DevOps Analytics, and an alert when the interactive agent goes offline (a single point of failure).

## 8. Assumptions

- Azure Repos Git and Azure Pipelines. Self-hosted pools `Windows-Build` and `Windows-UI-Interactive` already exist. The UI agent runs with `--runAsAutoLogon`, as a local admin, on a disposable VM.
- One product, x64 only, released as an MSI. Distribution to customers is out of scope, and the internal feed is the end of the pipeline.
- Azure Trusted Signing and GitHub Advanced Security for Azure DevOps are available. All names are placeholders.
- The WiX v7 OSMF fee has been cleared by legal or procurement.

## 9. Improvements for a real production setup

1. **Release guard:** check `resources.pipeline.ci.sourceBranch` in `release.yml` so a manual run cannot release a main build, add branch control on `DesktopApp-Production`, and tag the released commit.
2. **Versioning:** semantic versions from tags (e.g. GitVersion), with pre-release suffixes on main builds.
3. **UI agent capacity:** ephemeral VM scale set agents with a reset image, test sharding, and a UI smoke subset on PRs.
4. **Supply chain:** one internal feed with nuget.org as upstream, NuGet lock files, an SBOM, a malware scan of the MSI.
5. **Governance and retention:** required `extends` templates, Azure DevOps settings as code, and released runs (artifacts, symbols) retained for the support lifetime.
