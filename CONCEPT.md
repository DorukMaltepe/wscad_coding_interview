# DesktopApp CI/CD – Concept

## 1. Overall flow

```
PR ──► pr.yml (branch policy): restore + audit, build -warnaserror, unit + integration tests, coverage
merge to main / release/* ──► ci.yml, version 1.0.<rev>
   Build + CodeQL + sign ──► UITests ──► Package: MSI + sign ──► InstallerTests
   Windows-Build              Windows-UI-Interactive              Windows-UI-Interactive
green CI run on release/* ──► release.yml: approval on DesktopApp-Production ──► signed MSI to feed
```

Validation, CI and release are separate pipelines; PR and CI share `templates/build-and-test.yml`.

## 2. Key design decisions

- **Build once, promote the same bits.** The MSI is built from the `app` artifact; release only re-publishes the tested MSI. *Trade-off:* release artifacts must be retained.
- **UI tests after merge, not on PRs.** The interactive agent is scarce; PRs stay fast. *Trade-off:* a UI regression can reach main, but is caught before an MSI exists.
- **Sign before the artifact is published,** so all later tests run on signed files. *Trade-off:* every CI build uses the signing service.
- **Real install, repair and uninstall tests** catch what MSI table checks miss. *Trade-off:* the agent needs admin rights and a resettable machine.
- **Gates fail closed:** `-warnaserror` on build and restore (NuGet audit); a skipped install test fails in CI.
- **Self-contained `win-x64`:** no runtime prerequisite. *Trade-off:* bigger installer; runtime patches need a release.

## 3. Branching, pull request and release strategy

- **Trunk-based:** short-lived feature branches into `main`; `release/x.y` cut for stabilisation, fixes cherry-picked from `main`.
- **Branch policies on `main` and `release/*`:** reviewer, linked work item, squash merge, `pr.yml` as Build Validation (Azure Repos ignores YAML `pr:` triggers), required diff coverage of 70 %.
- **CI** uses `batch: true` to spare the UI agent. Every green CI run on `release/*` is a release candidate: `release.yml` is triggered through a pipeline resource and waits for approval. Main builds are never released.
- **Versioning:** `1.0.<rev>` is the assembly, MSI and package version, tracing an install back to a commit; `majorMinor` is bumped per release branch.

## 4. Build, testing, packaging and artifacts

- **Build:** central package versions, SDK pinned in `global.json`, NuGet audit, CodeQL on CI only.
- **Tests:** NUnit unit/integration tests with coverage on the build agent; UI, then installer tests on the interactive agent.
- **Packaging:** WiX harvests the whole `app` artifact; fixed `UpgradeCode` (test-enforced), major upgrades, downgrades blocked.
- **Artifacts:** `app`, `uitests`, `installertests`, `installer`, so interactive stages need no checkout or rebuild. Released MSIs go to the universal feed `desktop-releases`.

## 5. Secrets, permissions and service connections

- **No secrets in the repo or on agents.** Azure Trusted Signing keeps the key in Azure; the `DesktopApp-CodeSigning` connection uses workload identity federation and is limited to `DesktopApp-CI` on `main`/`release/*`. PR builds never sign. Secret scanning with push protection blocks committed keys.
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

- Azure Repos and Pipelines; pools `Windows-Build` and `Windows-UI-Interactive` exist. The UI agent runs with `--runAsAutoLogon` on a disposable VM, as an elevated local admin (UAC off).
- One x64 product released as an MSI; the internal feed is the end of the pipeline. Azure Trusted Signing and GitHub Advanced Security are available; the WiX v7 OSMF fee is cleared. All names are placeholders.

## 9. Improvements for production

1. **Release guard and versioning:** check `resources.pipeline.ci.sourceBranch`, tag the released commit, SemVer from tags (GitVersion).
2. **UI capacity:** ephemeral VM scale set agents, test sharding, a UI smoke subset on PRs.
3. **Supply chain:** internal feed with nuget.org upstream, lock files, SBOM, malware scan of the MSI.
4. **Governance:** required `extends` templates, PDBs to a symbol server instead of the MSI, released runs retained for the support lifetime.
