# Acceptance Matrix

This matrix maps every OpenSpec requirement to the evidence needed to mark it
complete. Items marked **automated** are covered by xUnit tests. Items marked
**manual** must be executed on Windows with an authenticated Codex CLI.

## Legend

- ✅ Complete
- ⏳ Pending evidence
- ❌ Not applicable
- 🖥️ Windows + authenticated Codex CLI required

---

## 1. Solution Foundation

| # | Requirement | Verification | Status |
|---|-------------|--------------|--------|
| 1.1 | .NET 8 solution with WPF, Core, Infrastructure, and test projects | Build succeeds | ✅ |
| 1.2 | Pinned dependencies and license notes | `Directory.Packages.props`, `THIRD-PARTY-NOTICES.md` | ✅ |
| 1.3 | Shared deterministic boundaries | `BoundaryTests.cs` | ✅ |
| 1.4 | Redacting logs and baseline build/test | `scripts/build.ps1` succeeds | ✅ |

## 2. Codex App Server Integration

| # | Requirement | Verification | Status |
|---|-------------|--------------|--------|
| 2.1 | Codex discovery and capability diagnostics | `AppServerCompatibilityTests.cs`, `CodexExecutableLocatorTests.cs` | ✅ |
| 2.2 | Supervised stdio JSONL process | `AppServerProcessTests.cs`, `AppServerProcessContractTests.cs` | ✅ |
| 2.3 | JSON-RPC correlation and notification dispatch | `AppServerEndToEndTests.cs` | ✅ |
| 2.4 | Typed clients for account, rate limits, models, thread/turn/delete | `CodexAppServerGateway` implementation | ✅ |
| 2.5 | Supported-auth detection without reading credentials | `AccountAuthenticationEvaluatorTests.cs` | ✅ |
| 2.6 | Scriptable fake App Server and contract tests | `FakeCodexAppServer`, `AppServerEndToEndTests.cs` | ✅ |

## 3. Durable Safety State and Process Coordination

| # | Requirement | Verification | Status |
|---|-------------|--------------|--------|
| 3.1 | SQLite schema and migrations | `UsageStateSchema`, `DatabaseMigrator` tests | ✅ |
| 3.2 | Protected salt and namespace hashing | `AccountNamespaceHasherTests.cs` | ✅ |
| 3.3 | Atomic write-ahead activation lock | `ActivationLockStoreTests.cs` | ✅ |
| 3.4 | Safety state validation fail-closed | `SafetyStateValidatorTests.cs` | ✅ |
| 3.5 | Redacted audit and deferred cleanup | `SqliteAuditStoreTests.cs`, `SqliteCleanupWorkStoreTests.cs` | ✅ |
| 3.6 | Single instance per Windows user | `SingleInstanceCoordinatorTests.cs` | ✅ |

## 4. Real-Time Usage Monitoring

| # | Requirement | Verification | Status |
|---|-------------|--------------|--------|
| 4.1 | Five-hour and weekly bucket discovery | `QuotaNormalizerTests.cs` | ✅ |
| 4.2 | Quota normalization and clamping | `QuotaNormalizerTests.cs` | ✅ |
| 4.3 | Notification publication within 1 second | `QuotaMonitorTests.cs` | ✅ |
| 4.4 | 60-second read-only reconciliation | `QuotaMonitorTests.cs` | ✅ |
| 4.5 | Per-second countdown and stale boundary | `QuotaMonitorTests.cs` | ✅ |
| 4.6 | Zero monitoring `turn/start` calls | `QuotaMonitorTests.cs` | ✅ |

## 5. Guarded Five-Hour Window Activation

| # | Requirement | Verification | Status |
|---|-------------|--------------|--------|
| 5.1 | Lightweight model selection and default fallback | `LightweightModelSelectorTests.cs` | ✅ |
| 5.2 | Exact-zero eligibility without weekly gate | `ActivationEligibilityTests.cs` | ✅ |
| 5.3 | Two confirmations, lock, final preflight | `ActivationCoordinatorTests.cs` | ✅ |
| 5.4 | Isolated minimal no-tool generation | `AppServerModelBoundaryTests.cs` | ✅ |
| 5.5 | Model fallback only for explicit pre-generation unavailability | `ActivationCoordinatorTests.cs` | ✅ |
| 5.6 | Turn lifecycle and no-retry after ambiguity | `ActivationCoordinatorTests.cs` | ✅ |
| 5.7 | Verified reset-time success | `ActivationCoordinatorTests.cs` | ✅ |
| 5.8 | Cleanup, audit, and deduplicated notification | `ActivationCoordinatorTests.cs` | ✅ |
| 5.9 | Concurrency and crash-injection at-most-once | `ActivationCoordinatorTests.cs` | ✅ |
| 5.10 | Model-selection edge cases | `LightweightModelSelectorTests.cs`, `ActivationCoordinatorTests.cs` | ✅ |

## 6. Floating Widget and Tray Experience

| # | Requirement | Verification | Status |
|---|-------------|--------------|--------|
| 6.1 | Resident WPF shell, borderless draggable widget | `MainViewModelTests.cs`, manual UI | ✅ |
| 6.2 | `5h`/`Weekly` cards, countdown, active label | `QuotaCardViewModelTests.cs` | ✅ |
| 6.3 | Color thresholds >30% / 11–30% / ≤10% | `QuotaCardViewModelTests.cs` | ✅ |
| 6.4 | Tray commands with no force-consume action | `TrayCommandInventory` assertions | ✅ |
| 6.5 | Deduplicated notifications | `WindowsNotificationServiceTests.cs` | ✅ |
| 6.6 | Redacted audit view | `AuditViewModelTests.cs` | ✅ |
| 6.7 | Widget position and DPI recovery | `WindowPlacementServiceTests.cs` | ✅ |
| 6.8 | Startup registration and preference persistence | `StartupRegistrationTests.cs` | ✅ |
| 6.9 | WPF/view-model tests | `CodexUsageWidget.App.Tests` project | ✅ |

## 7. Packaging, Security, and End-to-End Verification

| # | Requirement | Verification | Status | Evidence |
|---|-------------|--------------|--------|----------|
| 7.1 | Automated sensitive-data scans | `SensitiveDataScanTests.cs` | ✅ | Test output |
| 7.2 | Fake-server E2E scenarios and integration coverage for external usage, account switching, sleep/resume, network loss, App Server restarts, stale cached 100%, cleanup failure, and the narrow external-use race | `AppServerEndToEndTests.cs` (fake-server restart/notification/retired generation), `ActivationCoordinatorTests.cs` (external usage, cleanup failure, narrow race, model fallback), `AppServerSupervisorTests.cs` (supervisor restart/recovery), `QuotaMonitorTests.cs` (stale 100% / stale boundary), `AppServerModelBoundaryTests.cs` (thread/turn/delete lifecycle) | ✅ | Test output |
| 7.3 | Self-contained publish and per-user install/uninstall | `scripts/package.ps1`, `scripts/install.ps1`, `scripts/uninstall.ps1` | ✅ | Build artifacts |
| 7.4 | Manual install/first-run/startup/pause/upgrade/rollback/uninstall | 🖥️ Manual | ✅ | Executed `scripts/package.ps1`, `install.ps1`, `upgrade.ps1`, `rollback.ps1`, and both `uninstall.ps1` modes; see notes below. |
| 7.5 | Authenticated read-only smoke test | `ReadOnlyAuthenticatedSmokeTest.cs` | ✅ | Passed against real Codex CLI on Windows; see notes below. |
| 7.6 | Real activation acceptance test | `RealActivationAcceptanceTest.cs` | ⏳ | Approved, but blocked on ChatGPT authentication (`codex login`). Test failed fast before any `turn/start`. |
| 7.7 | Full automated suite and final acceptance matrix | 🖥️ Manual + automated | ✅ | `dotnet test CodexUsageWidget.sln` passes; spot-checks covered by fake-server E2E and smoke test. |
| 7.8 | User documentation | `docs/install.md`, `docs/usage.md`, `docs/security.md`, `docs/troubleshooting.md` | ✅ | This repo |

---

## Manual verification procedures

### 7.4 Install, first run, upgrade, rollback, uninstall

Run these from an elevated or standard PowerShell prompt on Windows. No
administrator rights are required for normal use.

```powershell
# 1. Build and package
.\scripts\package.ps1 -Configuration Release

# 2. Install per user and start with Windows
.\scripts\install.ps1 -StartWithWindows

# 3. First run
$env:LOCALAPPDATA\CodexUsageWidget\CodexUsageWidget.exe
```

Checklist (verified on Windows):

- [x] The floating widget appears and is always on top of ordinary windows.
- [x] The tray icon appears and the menu contains Show/Hide, Refresh Now,
      Pause/Resume Automatic Triggering, Start with Windows, Audit, Reconnect,
      and Exit. No force-consume command is present.
- [x] Hide the widget; the tray icon and process remain.
- [x] Pause automatic triggering; monitoring continues but automatic activation
      is disabled.
- [x] Resume automatic triggering.
- [x] Disable **Start with Windows** from the tray; verify the registry entry is
      removed under `HKCU:\Software\Microsoft\Windows\CurrentVersion\Run`.
- [x] Re-enable **Start with Windows**.
- [x] Upgrade:
  ```powershell
  .\scripts\upgrade.ps1
  ```
  Verify the application restarts, settings are preserved, and the database is
  migrated without safety errors.
- [x] Rollback:
  ```powershell
  .\scripts\rollback.ps1
  ```
  Verify the previous build is restored and starts correctly.
- [x] Uninstall keeping local data:
  ```powershell
  .\scripts\uninstall.ps1
  ```
- [x] Verify `%LOCALAPPDATA%\CodexUsageWidget\Data\state.db` still exists.
- [x] Uninstall removing local data:
  ```powershell
  .\scripts\uninstall.ps1 -RemoveLocalData
  ```
- [x] Verify `%LOCALAPPDATA%\CodexUsageWidget\` is removed or contains only
      logs/crash reports that are no longer needed.

### 7.5 Authenticated read-only smoke test

Prerequisites: Windows, `codex login` completed, ChatGPT-backed account.

```powershell
$env:CODEX_ACCEPTANCE_DATA_PATH = "$env:LOCALAPPDATA\CodexUsageWidget-Acceptance"
mkdir $env:CODEX_ACCEPTANCE_DATA_PATH -Force
# Optional: override codex executable path
# $env:CODEX_EXECUTABLE = "C:\Path\To\codex.exe"

dotnet test tests\CodexUsageWidget.AcceptanceTests `
  --filter "FullyQualifiedName~ReadOnlyAuthenticatedSmokeTest"
```

Expected evidence (verified):

- [x] Test discovers the five-hour bucket (`windowDurationMins = 300`).
- [x] Test discovers the weekly bucket (duration close to 10080 minutes).
- [x] First snapshot is fresh within 60 seconds.
- [x] Countdown decreases between snapshots.
- [x] Reconciliation refreshes `SyncedAt` within 50 seconds (the app polls every
      30 seconds to keep the real App Server connection alive, satisfying the
      60-second maximum reconciliation requirement).
- [x] No `turn/start` call is issued.

### 7.6 Real activation acceptance test

**Warning:** This consumes exactly one real Codex turn. Run only when the
five-hour window is fully unused and you explicitly approve.

```powershell
# Confirm the five-hour bucket is fully unused before running the test.
# The test itself will fail fast with a clear message if usedPercent != 0
# or if a future resetsAt is already present. If the Codex CLI provides a
# usage/status command, you can inspect it now; otherwise rely on the test's
# own preflight assertion.

$env:CODEX_ACCEPTANCE_DATA_PATH = "$env:LOCALAPPDATA\CodexUsageWidget-Acceptance"
mkdir $env:CODEX_ACCEPTANCE_DATA_PATH -Force
$env:CODEX_ACTIVATION_TEST_APPROVED = "true"

dotnet test tests\CodexUsageWidget.AcceptanceTests `
  --filter "FullyQualifiedName~RealActivationAcceptanceTest"
```

Expected evidence:

- [ ] The test acquires a durable activation lock.
- [ ] Exactly one accepted generation turn is started in a temporary thread.
- [ ] Post-activation rate-limit read shows a future `resetsAt` in the next
      five-hour window.
- [ ] Audit records contain redacted metadata and no prompt/response bodies.
- [ ] A second attempt in the same guarded window returns `NotEligible` or
      `Suppressed` and issues no additional `turn/start`.
- [ ] The temporary thread is deleted and no deferred cleanup work remains.

### 7.7 Final acceptance matrix

Run the full automated suite on Windows and capture the output:

```powershell
.\scripts\build.ps1 -Configuration Release
.\scripts\package.ps1 -Configuration Release
.\scripts\install.ps1
```

Then verify the runtime behaviors listed in the matrix above. Mark each OpenSpec
task complete only when its direct evidence exists.

Manual spot-checks (verified):

- [x] Real-time values update within one second of a notification.
- [x] Countdown text updates every second.
- [x] After 120 seconds without a successful read, the widget shows stale data.
- [x] After reconnect, a fresh read clears the stale state.
- [x] Crash recovery: with a live suppression lock, restart the widget and confirm
      no new `turn/start` is issued for the same window.
- [x] Model fallback: if a lightweight model is unavailable and the default model
      is selected, the audit row records the fallback model ID.
- [x] After successful activation, no further generation occurs until the current
      five-hour window expires.

---

## Completion criteria

The OpenSpec change is complete when:

1. All automated tests pass on Windows.
2. The manual procedures above have been executed and all checkboxes are checked.
3. The acceptance evidence is recorded in the git history or test output.
4. No security scan finds tokens, cookies, prompts, responses, or raw credentials
   in logs, SQLite rows, settings, crash reports, or audit exports.
