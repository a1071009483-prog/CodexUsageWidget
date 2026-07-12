## 1. Solution Foundation

- [x] 1.1 Create a `.NET 8` solution with WPF application, core domain, App Server/persistence infrastructure, and automated test projects.
- [x] 1.2 Add and pin the required Windows desktop, SQLite, JSON serialization, and test dependencies, then document their licenses and runtime prerequisites.
- [x] 1.3 Define shared clock, scheduler, process, file-system, notification, and model-boundary interfaces so time, crashes, and App Server behavior can be tested deterministically.
- [x] 1.4 Add structured redacting logs and a baseline build/test command that succeeds before feature implementation.

## 2. Codex App Server Integration

- [x] 2.1 Implement Codex executable discovery plus startup capability diagnostics for the required account, rate-limit, model, thread, turn, interrupt, and delete methods.
- [x] 2.2 Implement a supervised `codex app-server` stdio JSONL process with initialize/initialized handshake, graceful shutdown, cancellation, and bounded restart backoff.
- [x] 2.3 Implement JSON-RPC request correlation, tolerant response parsing, notification dispatch, protocol-error classification, and stale-response rejection.
- [x] 2.4 Add typed clients for `account/read`, `account/rateLimits/read`, `account/rateLimits/updated`, `model/list`, temporary thread creation, `turn/start`, `turn/interrupt`, and thread deletion.
- [x] 2.5 Implement supported-authentication detection and account/workspace identity extraction without reading cookies, raw credentials, or authentication files.
- [x] 2.6 Build a scriptable fake App Server process and contract tests for handshake, request ordering, notifications, disconnects, malformed messages, and method incompatibility.

## 3. Durable Safety State and Process Coordination

- [x] 3.1 Create the SQLite schema and migration runner for settings, hashed account namespaces, activation attempts, notifications, cleanup work, and redacted audit rows under `%LOCALAPPDATA%\CodexUsageWidget`.
- [x] 3.2 Implement stable account/workspace namespace hashing with a locally protected salt and verify that raw account identifiers are never persisted.
- [x] 3.3 Implement an atomic unique write-ahead activation lock with authoritative-or-local window epochs, suppression deadline, pre/post quota fields, turn-boundary state, model selection, and terminal outcome.
- [x] 3.4 Load and validate safety state before activation starts; fail closed on corruption, migration failure, inconsistent rows, or failed durable writes.
- [x] 3.5 Implement redacted audit recording and deferred temporary-thread cleanup without any path back into model generation.
- [x] 3.6 Enforce one application instance per Windows user with a named mutex and bring the existing widget forward through a local single-instance signal.

## 4. Real-Time Usage Monitoring

- [x] 4.1 Implement five-hour bucket discovery from `windowDurationMins = 300` and robust weekly-bucket discovery from server duration/label metadata without positional assumptions.
- [x] 4.2 Normalize raw usage into clamped remaining percentage, UTC `resetsAt`, bucket availability, account scope, synchronization time, and freshness while preserving raw eligibility values.
- [x] 4.3 Implement immutable monitoring state publication and update-notification handling that reaches UI consumers within one second.
- [x] 4.4 Implement the 60-second read-only reconciliation loop, Refresh Now, missed-notification recovery, offline backoff, and App Server reconnection without creating threads or turns.
- [x] 4.5 Implement per-second local reset countdowns, the two-minute stale boundary, resume/clock-change resynchronization, and account/workspace transition invalidation.
- [x] 4.6 Add deterministic tests proving startup synchronization within 60 seconds, notification publication within one second, fallback convergence within 60 seconds, one-second countdown ticks, stale marking at 120 seconds, and zero monitoring `turn/start` calls.

## 5. Guarded Five-Hour Window Activation

- [x] 5.1 Implement an updateable lightweight-model family policy that selects the newest recognized available lightweight model from `model/list` and otherwise selects the current `isDefault = true` model.
- [x] 5.2 Implement activation eligibility for enabled automation, a fresh exact `usedPercent = 0` five-hour bucket, no already-active verified timer, and no active scoped suppression lock, without applying a weekly threshold.
- [x] 5.3 Implement two consecutive authoritative confirmations, transactional lock acquisition, and the final read-only preflight that cancels activation when external Codex activity has already started the window, without claiming source attribution.
- [x] 5.4 Construct a fresh isolated temporary thread in an empty read-only working directory with non-interactive approvals, lowest supported reasoning, no client-supplied dynamic tools, and a constant minimal fixed-response request.
- [x] 5.5 Implement model fallback only for an explicit pre-generation model-unavailable rejection; refresh the catalog under the same lock and permanently close fallback once a turn may have started.
- [x] 5.6 Implement turn lifecycle handling, accepted-generation boundary auditing, timeout interruption, unexpected-tool interruption, and the no-retry rule for failed or ambiguous outcomes.
- [x] 5.7 Verify activation through a changed fresh future five-hour `resetsAt` within a 60-second read-only observation period, independent of rounded percentage display.
- [x] 5.8 Delete successful temporary tasks, enqueue delete-only cleanup when deletion itself fails, persist redacted pre/post audit data, and emit one deduplicated terminal notification.
- [x] 5.9 Add concurrency and crash-injection tests at every boundary before and after lock persistence, request transmission, turn acceptance, verification, and cleanup to prove at most one actual generation per scoped window.
- [x] 5.10 Add model-selection tests for new lightweight versions, missing lightweight families, default fallback, multiple/absent defaults, explicit pre-generation rejection, and ambiguous failure without retry.

## 6. Floating Widget and Tray Experience

- [x] 6.1 Implement the resident WPF application shell, borderless draggable topmost widget, hide-without-exit behavior, and connection/monitor/activation view models.
- [x] 6.2 Implement separate `5h` and `Weekly` cards with remaining percentage, progress, reset countdown, last synchronization time, status, unavailable/stale presentation, and the exact rounded-active label `100%·计时已启动`.
- [x] 6.3 Apply normal, warning, and critical presentation thresholds at greater than 30%, greater than 10% through 30%, and 10% or less remaining for both quota cards.
- [x] 6.4 Implement tray actions for Show/Hide, Refresh Now, Pause/Resume Automatic Triggering, Start with Windows, Audit, Reconnect, and Exit, with no force-consume action.
- [x] 6.5 Implement deduplicated Windows notifications for activation success, failure/fail-closed outcomes, and authentication requirements while keeping normal polling silent.
- [x] 6.6 Implement the local audit view using only redacted metadata, including model fallback visibility and pending cleanup status.
- [x] 6.7 Persist and restore widget position and size safely across restarts, removed monitors, work-area changes, and per-monitor DPI changes.
- [x] 6.8 Implement per-user Windows startup registration, default startup/automatic-trigger settings, preference persistence, and safety overrides that keep activation disabled when state is invalid.
- [x] 6.9 Add WPF/view-model tests for quota rendering, active-but-rounded 100% state, stale data, color boundaries, tray commands, notifications, single-instance signaling, startup preferences, and multi-monitor placement logic.

## 7. Packaging, Security, and End-to-End Verification

- [x] 7.1 Add automated sensitive-data scans proving logs, SQLite rows, settings, crash reports, and audit exports contain no tokens, cookies, raw credentials, prompt bodies, response bodies, or unredacted workspace content.
- [x] 7.2 Build fake-server end-to-end scenarios covering every spec requirement, including external Codex usage, account switching, sleep/resume, network loss, App Server restarts, stale cached 100%, cleanup failure, and the narrow external-use race.
- [x] 7.3 Publish and package a Windows self-contained build with documented Codex CLI prerequisites, per-user install/uninstall behavior, and no administrator requirement for normal use.
- [x] 7.4 Verify install, first run, Windows sign-in startup, pause/resume, upgrade with database migration, rollback, uninstall, and optional local-data removal after any active suppression period.  
  Evidence: `scripts/package.ps1`, `scripts/install.ps1`, `scripts/upgrade.ps1`, `scripts/rollback.ps1`, and `scripts/uninstall.ps1` executed on Windows; see `docs/acceptance-matrix.md`.
- [x] 7.5 Implement and execute a read-only authenticated smoke test harness that confirms real five-hour/weekly mapping, startup freshness, ~30-second reconciliation (kept under 60 seconds to satisfy the App Server idle timeout), countdown behavior, and zero model turns. (Execution requires Windows + authenticated Codex CLI.)
- [ ] 7.6 With explicit user approval and a fully unused five-hour window, run exactly one real activation test and verify a future `resetsAt`, temporary-task cleanup, redacted audit data, and zero later `turn/start` calls in that guarded period.  
  See `tests/CodexUsageWidget.AcceptanceTests/Activation/RealActivationAcceptanceTest.cs` and `docs/acceptance-matrix.md`. **Pending explicit user approval.**
- [x] 7.7 Execute the full automated suite and manually check the final acceptance matrix: real-time values, one-second notification display, 60-second maximum reconciliation, 120-second stale state, at-most-once activation, crash recovery, model fallback, and no post-success generation.  
  Evidence: `dotnet test CodexUsageWidget.sln` passes; `ReadOnlyAuthenticatedSmokeTest` passes against real Codex CLI. See `docs/acceptance-matrix.md`.
- [x] 7.8 Write user documentation for installation, authentication, quota semantics, model fallback, startup behavior, pause/reconnect/audit controls, known race limitations, troubleshooting, and safe removal.
