# DesktopApp CI/CD – Concept

## 1. Overall flow

```
PR ──► pr.yml (branch policy, Windows-PR pool): restore + audit, build -warnaserror,
       unit + integration tests, coverage, unsigned MSI + MSI table/payload tests, pipeline script tests
merge to main ──► ci.yml, version 1.0.<rev>
   Build + CodeQL + sign ──► UITests ──► Package: MSI + sign ──► InstallerTests
   Windows-Build              Windows-UI-Interactive              Windows-UI-Interactive
green CI run on main ──► release.yml: Verify ──► [Test ──► PrP ──►] Production ──► signed MSI to feed
```

Validation, CI and release are separate pipelines; shared templates keep PR, CI and every agent consistent (`build-and-test`, `installer`, `use-dotnet`), and every environment uses `deploy.yml`.

## 2. Key design decisions

- **Build once, promote the same bits.** The MSI is built from the `app` artifact; release only re-publishes the tested MSI. *Trade-off:* release artifacts must be retained.
- **Only verified runs are deployable.** The release's Verify stage checks the CI run through the REST API: built from `main`, result `succeeded`, every build and test stage ran and passed, app and MSI signed and timestamped by our publisher. It runs before anyone is asked to approve. Server-side environment checks (branch control, approval, exclusive lock) back it up, because they hold even if the YAML is edited. *Trade-off:* ci.yml's stage names are now a contract.
- **Untrusted PR pool.** PR code runs on `Windows-PR`, never on the agents that build signed releases, so a PR cannot poison a persistent agent that later signs.
- **UI tests after merge, not on PRs.** The interactive agent is scarce; PRs stay fast. *Trade-off:* a UI regression can reach main, but is caught before an MSI exists.
- **Sign before the artifact is published,** so all later tests run on signed files. *Trade-off:* every CI build uses the signing service.
- **Real install, repair and uninstall tests** catch what MSI table checks miss. *Trade-off:* the agent needs admin rights and a resettable machine.
- **Gates fail closed:** `-warnaserror` on build and restore (NuGet audit); a skipped install test fails in CI.
- **Self-contained `win-x64`:** no runtime prerequisite. *Trade-off:* bigger installer; runtime patches need a release.

## 3. Branching, pull request and release strategy

- **Trunk-based, `main` is the only deployable branch:** short-lived feature branches into `main`. Hotfixes land on `main` and roll forward as a higher version. Downgrades are blocked, so a rollback is also a new version.
- **Branch policies on `main`:** reviewer, linked work item, squash merge, `pr.yml` as Build Validation (Azure Repos ignores YAML `pr:` triggers), required diff coverage of 70 %.
- **CI** uses `batch: true` and skips docs-only changes to spare the UI agent. Every green `main` run is a release candidate: `release.yml` verifies it, then promotes it through the environments in order. Today that is only Production; Test and PrP slot in before it as `deploy.yml` stages, each an environment with its own approvals and the same `main`-only branch control. Release branches (several versions in the field) would be added to `deployableBranches` and branch control together.
- **Versioning:** `1.0.<rev>` is the assembly, MSI and package version; the Production deployment tags the commit `v<version>`, so every shipped version maps to its source. `majorMinor` in ci.yml is bumped for a new minor release.

## 4. Build, testing, packaging and artifacts

- **Build:** central package versions, SDK pinned in `global.json` and used on every agent, NuGet audit, CodeQL on CI only.
- **Tests:** NUnit unit/integration tests with coverage on the build agent; UI, then installer tests on the interactive agent. Each interactive job starts by resetting the agent (no leftover app or install) and times out after 30 minutes. Failures carry evidence: screenshots and crash logs for UI tests, full msiexec logs for install tests, dumps for hangs. The release gate and agent scripts have Pester tests that run on every PR, because a gate nobody tests is no gate.
- **Packaging:** WiX harvests the whole `app` artifact; fixed `UpgradeCode` (test-enforced), major upgrades, downgrades blocked. PRs build the MSI too (unsigned), so installer breakage never reaches `main`.
- **Artifacts:** `app` (no PDBs), `uitests`, `installertests`, `installer`, so interactive stages need no rebuild (only a sparse checkout of `global.json` and scripts). PDBs go to the symbol server. Release publishes only the `.msi` to the universal feed `desktop-releases`.

## 5. Secrets, permissions and service connections

- **No secrets in the repo or on agents.** Azure Trusted Signing keeps the key in Azure; the `DesktopApp-CodeSigning` connection uses workload identity federation and is limited to `DesktopApp-CI` on `main`. PR builds never sign, and they run on a separate pool. Secret scanning with push protection blocks committed keys.
- **Feeds:** `NuGetAuthenticate@1` uses the job token; only the release pipeline may publish to `desktop-releases`.
- **Telemetry connection string** is an ingestion key shipped in the binary, so configuration in a variable group; real secrets go in a Key Vault-linked group.

## 6. Software protection, licensing, installer and updates

- **Obfuscation** runs after `dotnet publish` and before signing, so UI tests run on the protected build that ships.
- **Licensing:** the app only verifies licenses; the signing key stays in the licensing backend or an HSM.
- **Installer:** the signed MSI is the release unit; a WiX Burn bundle only if prerequisites appear (e.g. a dongle driver).
- **Updates:** a release stage uploads the MSI to a CDN with a manifest (version, URL, SHA-256) the app checks on start, rolled out in rings (internal → beta → GA) as environments with approvals.

## 7. Logging, telemetry and monitoring

The app sends crashes, errors, sessions, startup time, resources and feature usage to Application Insights via OpenTelemetry (no machine or customer names, opt-out, test runs opt out); `monitoring/main.bicep` deploys a health workbook. **Crash-free sessions per version** shows within hours if a release is worse and can gate ring promotion. On the pipeline side: flaky-test detection and an alert when the interactive agent goes offline.

## 8. Assumptions

- Azure Repos and Pipelines; pools `Windows-PR`, `Windows-Build` and `Windows-UI-Interactive` exist. The UI agent runs with `--runAsAutoLogon` on a disposable VM, as an elevated local admin (UAC off).
- One x64 product released as an MSI; the internal feed is the end of the pipeline. Azure Trusted Signing and GitHub Advanced Security are available; the WiX v7 OSMF fee is cleared. All names are placeholders.

## 9. Improvements for production

1. **Release traceability:** SemVer from tags (GitVersion), a retention lease on every deployed CI run.
2. **Agents and UI capacity:** ephemeral VM scale set agents (reimaged per job, PR pool first), test sharding, a UI smoke subset on PRs.
3. **Supply chain:** internal feed with nuget.org upstream, lock files, SBOM, malware scan of the MSI.
4. **Governance:** required `extends` templates, released runs retained for the support lifetime.
