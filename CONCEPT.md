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

### Alternatives weighed

| Chosen | Instead of | Why |
|---|---|---|
| **MSI (WiX)** | MSIX | MSIX offers clean uninstall and differential updates, but its container blocks what CAD software often needs: kernel drivers for license dongles, writable install folders for plug-ins, and integration that other tools see from outside the container. Enterprise customers also roll out MSIs with transforms through Intune or SCCM. The same signed `app` artifact can feed an MSIX later if customers ask for one. |
| **Azure Trusted Signing** | EV certificate in an HSM | No hardware and no yearly renewal. Certificates live three days, so a leaked one expires quickly, and the agent signs through a federated identity without ever holding a key. EV no longer buys instant SmartScreen reputation, so its main advantage is gone. *Risk:* Trusted Signing is limited to eligible organizations and regions. The fallback is an HSM-backed certificate in Key Vault, which the same `sign` CLI supports, so only `sign.yml` would change. |
| **Azure Artifacts feed** | CDN or blob storage | The feed gives immutable versions, access control and retention for internal users (QA, support). Customers can't reach it, so customer delivery is a later stage that copies the verified MSI to a CDN (section 6), not a second build. |
| **YAML stages + environments** | Classic release pipelines | The release is code: reviewed in PRs, versioned with the app, and gated by server-side checks. Classic releases live only in the UI. |

## 3. Branching, pull request and release strategy

- **Trunk-based, `main` is the only deployable branch:** short-lived feature branches into `main`. Hotfixes land on `main` and roll forward as a higher version. Downgrades are blocked, so a rollback is also a new version.
- **Branch policies on `main`:** reviewer, linked work item, squash merge, `pr.yml` as Build Validation (Azure Repos ignores YAML `pr:` triggers), required diff coverage of 70 %.
- **CI** uses `batch: true` and skips docs-only changes to spare the UI agent. Every green `main` run is a release candidate: `release.yml` verifies it, then promotes it through the environments in order. Today that is only Production; Test and PrP slot in before it as `deploy.yml` stages, each an environment with its own approvals and the same `main`-only branch control. Release branches (several versions in the field) would be added to `deployableBranches` and branch control together.
- **Versioning:** `1.0.<rev>` is the assembly, MSI and package version; the Production deployment tags the commit `v<version>`, so every shipped version maps to its source. `majorMinor` in ci.yml is bumped for a new minor release.

## 4. Build, testing, packaging and artifacts

- **Build:** central package versions, SDK pinned in `global.json` and used on every agent, NuGet audit, CodeQL on CI only.
- **Tests:** NUnit unit/integration tests with coverage on the build agent; UI, then installer tests on the interactive agent. Each interactive job starts by resetting the agent (no leftover app or install) and times out after 30 minutes. Failures carry evidence: screenshots and crash logs for UI tests, full msiexec logs for install tests, dumps for hangs. The release gate and agent scripts have Pester tests that run on every PR.
- **Packaging:** WiX harvests the whole `app` artifact; fixed `UpgradeCode` (test-enforced), major upgrades, downgrades blocked. PRs build the MSI too (unsigned), so installer breakage never reaches `main`.
- **Artifacts:** `app` (no PDBs), `uitests`, `installertests`, `installer`, so interactive stages need no rebuild (only a sparse checkout of `global.json` and scripts). PDBs go to the symbol server. Release publishes only the `.msi` to the universal feed `desktop-releases`.

## 5. Secrets, permissions and service connections

- **No secrets in the repo or on agents.** Azure Trusted Signing keeps the key in Azure; the `DesktopApp-CodeSigning` connection uses workload identity federation and is limited to `DesktopApp-CI` on `main`. PR builds never sign, and they run on a separate pool. Secret scanning with push protection blocks committed keys.
- **Feeds:** `NuGetAuthenticate@1` uses the job token; only the release pipeline may publish to `desktop-releases`.
- **Telemetry connection string:** it is only an ingestion key and ships inside the binary anyway, so a plain variable group is enough. Real secrets go in a Key Vault-linked group.

## 6. Software protection, licensing, installer and updates

- **Protection:** an obfuscator (e.g. .NET Reactor or Dotfuscator) runs after `dotnet publish` and before signing, because signing must be the last change to a binary. The UI tests then run on the protected build that ships, which also catches rules that rename XAML-bound or automation names. Its rename map is kept per version next to the PDBs; without it, crash stack traces from telemetry are unreadable.
- **Licensing:** the app only *verifies* licenses with an embedded public key. The private key stays in the licensing backend or an HSM and never enters a pipeline. Test agents use a time-limited CI license from the real backend, so the tested binary is the shipped one, with no test-only key path that could leak into a release.
- **Installer:** the signed MSI is the release unit. A WiX Burn bundle comes in only when prerequisites appear (e.g. a dongle driver), and it is signed the same way.
- **Updates:** a release stage uploads the MSI to a CDN with a manifest (version, URL, SHA-256) that the app checks on start. Rings (internal → beta → GA) are environments with approvals, and each promotion is decided on crash-free-session data (section 7).

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
