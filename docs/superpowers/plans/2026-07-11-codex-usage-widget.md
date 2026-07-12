# Codex Usage Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a resident Windows .NET 8 WPF widget that displays fresh Codex 5h/weekly quota state and starts a fully unused 5h window with one crash-safe, guarded minimal turn.

**Architecture:** A pure `Core` library owns quota normalization, freshness, model selection, and the activation state machine. `Infrastructure` owns the supervised Codex App Server JSONL client, SQLite durable state, Windows integration, and redacted logging. The WPF `App` consumes only immutable view state and commands; fake-server and unit projects verify protocol, timing, crash, safety, and UI behavior.

**Tech Stack:** .NET SDK 8.0.422, WPF/Windows Forms tray APIs, System.Text.Json 8.0.6, Microsoft.Data.Sqlite 8.0.28, System.Security.Cryptography.ProtectedData 8.0.0, xUnit 2.9.3, xunit.runner.visualstudio 2.8.2, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4.

## Global Constraints

- Target Windows 10/11 with `net8.0-windows10.0.19041.0`; normal use MUST NOT require administrator rights.
- Monitoring may initialize, read account/model/rate-limit metadata, subscribe, health-check, and reconnect, but MUST issue exactly zero `thread/start` or `turn/start` calls.
- Notifications reach UI state within 1 second; reconciliation runs no less often than every 60 seconds; data becomes stale at 120 seconds; countdown ticks locally every second.
- Automatic activation requires enabled automation and fresh raw five-hour `usedPercent = 0`; weekly state never gates it.
- A durable scoped lock is flushed before temporary-thread creation or `turn/start`; after the generation boundary may have been crossed, no retry is permitted in that guarded period.
- Activation success is a changed, fresh, future five-hour `resetsAt`, even when the rounded display remains `100%·计时已启动`.
- Model selection uses the newest recognized available lightweight model, then the current `isDefault = true` model; fallback is allowed only after explicit pre-generation model-unavailable rejection.
- Local data MUST exclude tokens, cookies, raw credentials, prompt/response bodies, raw account identifiers, and unredacted workspace data.
- There is no force-consume UI, CLI option, API, keyboard shortcut, or diagnostic bypass.

---

### Task 1: Solution foundation and deterministic boundaries

**Files:**
- Create: `CodexUsageWidget.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `THIRD-PARTY-NOTICES.md`
- Create: `src/CodexUsageWidget.Core/CodexUsageWidget.Core.csproj`
- Create: `src/CodexUsageWidget.Infrastructure/CodexUsageWidget.Infrastructure.csproj`
- Create: `src/CodexUsageWidget.App/CodexUsageWidget.App.csproj`
- Create: `tests/CodexUsageWidget.Core.Tests/CodexUsageWidget.Core.Tests.csproj`
- Create: `tests/CodexUsageWidget.Infrastructure.Tests/CodexUsageWidget.Infrastructure.Tests.csproj`
- Create: `tests/CodexUsageWidget.App.Tests/CodexUsageWidget.App.Tests.csproj`
- Create: `tests/FakeCodexAppServer/FakeCodexAppServer.csproj`
- Test: `tests/CodexUsageWidget.Core.Tests/Architecture/BoundaryTests.cs`

**Interfaces:**
- Produces: `IClock.UtcNow`, `IDelay.DelayAsync`, `IProcessHost`, `IAppFileSystem`, `IUserNotifier`, `IModelBoundary`, `IRedactingLog`.

- [x] Write `BoundaryTests` first to require the abstractions and prohibit `Core` references to WPF, SQLite, process, registry, or file-system implementation types.
- [x] Run `\.dotnet\dotnet.exe test tests\CodexUsageWidget.Core.Tests` and verify RED because the solution and abstractions do not exist.
- [x] Scaffold the projects, central package versions, nullable/analysis settings, usable asynchronous contracts for the seven boundaries, a tested JSON-lines redacting logger, license notes, and `scripts/build.ps1` with local-SDK/PATH discovery.
- [x] Run `\.dotnet\dotnet.exe test CodexUsageWidget.sln` and verify GREEN with a clean build.

### Task 2: Current App Server JSON-RPC transport and typed gateway

**Files:**
- Create: `src/CodexUsageWidget.Infrastructure/AppServer/AppServerProcess.cs`
- Create: `src/CodexUsageWidget.Infrastructure/AppServer/JsonRpcConnection.cs`
- Create: `src/CodexUsageWidget.Infrastructure/AppServer/CodexAppServerGateway.cs`
- Create: `src/CodexUsageWidget.Infrastructure/AppServer/CodexExecutableLocator.cs`
- Create: `src/CodexUsageWidget.Infrastructure/AppServer/Protocol/*.cs`
- Create: `tests/FakeCodexAppServer/Program.cs`
- Test: `tests/CodexUsageWidget.Infrastructure.Tests/AppServer/*Tests.cs`

**Interfaces:**
- Consumes: `IProcessHost`, `IClock`, `IDelay`, `IRedactingLog`.
- Produces: `ICodexGateway` methods `InitializeAsync`, `ReadAccountAsync`, `ReadRateLimitsAsync`, `ListModelsAsync`, `StartThreadAsync`, `StartTurnAsync`, `InterruptTurnAsync`, `DeleteThreadAsync`; typed notification stream.

- [ ] Write failing fake-server contract tests for initialize/initialized ordering, numeric request correlation, sparse `account/rateLimits/updated`, tolerant unknown fields, malformed JSON, cancellation, disconnect, bounded restart, and required-method diagnostics.
- [ ] Run the App Server tests and verify failures are due to missing transport/gateway behavior.
- [ ] Implement line-delimited JSON-RPC, supervised stdio, response correlation, stale-generation rejection, notification routing, typed DTOs matching the locally generated Codex App Server schema, and redacted protocol errors.
- [ ] Add discovery tests covering PATH, configured executable, packaged Codex locations, inaccessible executables, and incompatible method sets.
- [ ] Run infrastructure tests and the full solution; verify all are GREEN and monitoring calls contain no generation method.

### Task 3: SQLite durable safety state and Windows process coordination

**Files:**
- Create: `src/CodexUsageWidget.Infrastructure/Persistence/SqliteStateStore.cs`
- Create: `src/CodexUsageWidget.Infrastructure/Persistence/Migrations.cs`
- Create: `src/CodexUsageWidget.Infrastructure/Security/AccountNamespaceHasher.cs`
- Create: `src/CodexUsageWidget.Infrastructure/Windows/SingleInstanceCoordinator.cs`
- Test: `tests/CodexUsageWidget.Infrastructure.Tests/Persistence/*Tests.cs`
- Test: `tests/CodexUsageWidget.Infrastructure.Tests/Windows/SingleInstanceCoordinatorTests.cs`

**Interfaces:**
- Produces: `ISafetyStateStore`, `ISettingsStore`, `IAuditStore`, `ICleanupQueue`, `INotificationDeduplicator`, `ISingleInstanceCoordinator`.
- Schema tables: `settings`, `account_namespaces`, `activation_attempts`, `notifications`, `cleanup_work`, `audit_rows`, `schema_info`.

- [ ] Write failing tests for migrations, WAL/full synchronization, protected local salt, raw-identity exclusion, atomic unique scoped locks, local eligibility epochs, suppression deadlines, corruption fail-closed behavior, pending delete-only cleanup, named mutex, and bring-forward pipe signaling.
- [ ] Run persistence/Windows tests and verify RED at the missing repository boundary.
- [ ] Implement transactional schema/migrations under `%LOCALAPPDATA%\CodexUsageWidget`, durable write-ahead lock acquisition, state validation, redacted audit storage, cleanup queue, deduplicated notification keys, and per-user single-instance signaling.
- [ ] Add crash injection before/after commit and verify recovery never loses an established guard.
- [ ] Run all persistence, security-scan, and solution tests GREEN.

### Task 4: Real-time quota monitoring

**Files:**
- Create: `src/CodexUsageWidget.Core/Quota/QuotaModels.cs`
- Create: `src/CodexUsageWidget.Core/Quota/QuotaNormalizer.cs`
- Create: `src/CodexUsageWidget.Core/Monitoring/QuotaMonitor.cs`
- Test: `tests/CodexUsageWidget.Core.Tests/Quota/QuotaNormalizerTests.cs`
- Test: `tests/CodexUsageWidget.Core.Tests/Monitoring/QuotaMonitorTests.cs`

**Interfaces:**
- Consumes: read-only subset `IQuotaSource.ReadAsync`, `RateLimitsUpdated`, `IClock`, `IDelay`.
- Produces: immutable `QuotaSnapshot` with scoped raw/clamped values, UTC reset instants, availability, freshness, sync time, countdown, connection state, and trigger-eligible five-hour input.

- [ ] Write failing table tests for 300-minute discovery, weekly duration/label discovery without position assumptions, invalid/ambiguous buckets, clamping while preserving raw values, scope changes, and sparse update merge/refetch.
- [ ] Write failing deterministic scheduler tests for startup <=60 seconds, notification publication <=1 second, poll convergence <=60 seconds, countdown ticks every second, due-reset refresh, stale transition exactly at 120 seconds, reconnect/backoff, and zero model-boundary calls.
- [ ] Implement the normalizer and immutable monitor state machine with separate display freshness and activation eligibility.
- [ ] Run core monitoring tests and the full suite GREEN.

### Task 5: Guarded five-hour activation state machine

**Files:**
- Create: `src/CodexUsageWidget.Core/Activation/LightweightModelSelector.cs`
- Create: `src/CodexUsageWidget.Core/Activation/ActivationEligibility.cs`
- Create: `src/CodexUsageWidget.Core/Activation/ActivationCoordinator.cs`
- Create: `src/CodexUsageWidget.Infrastructure/AppServer/AppServerModelBoundary.cs`
- Test: `tests/CodexUsageWidget.Core.Tests/Activation/*Tests.cs`
- Test: `tests/CodexUsageWidget.Infrastructure.Tests/Activation/ActivationContractTests.cs`

**Interfaces:**
- Consumes: fresh `QuotaSnapshot`, `ISafetyStateStore`, `IModelCatalog`, `IModelBoundary`, `IClock`, `IDelay`, `IUserNotifier`.
- Produces: immutable `ActivationStatus`; one guarded temporary-thread lifecycle; redacted terminal audit and cleanup work.

- [x] Write failing model policy tests for recognized lightweight semantic versions, unavailable entries, default fallback, multiple/absent defaults, refreshed catalog, and explicit pre-generation unavailability.
- [x] Write failing eligibility/coordination tests for exact-zero freshness, weekly non-gating, two consecutive confirmations, transactional lock before any thread, final preflight, external satisfaction, fixed no-tool/read-only/noninteractive request, accepted boundary, timeout/tool interrupt, changed-future-reset verification, delete/deferred cleanup, and notification dedupe.
- [x] Write failing concurrency/crash tests at lock, thread creation, request send, accepted/started, verification, audit, and cleanup boundaries; assert at most one actual `turn/start` and no retry after ambiguity.
- [x] Implement the curated updateable lightweight policy, coordinator, and App Server model boundary with a compiled constant response contract and an empty read-only activation directory.
- [x] Run activation tests repeatedly and in parallel; verify all safety invariants remain GREEN.

### Task 6: WPF floating widget, tray, startup, and audit UX

**Files:**
- Create: `src/CodexUsageWidget.App/App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`
- Create: `src/CodexUsageWidget.App/ViewModels/MainViewModel.cs`, `QuotaCardViewModel.cs`, `AuditViewModel.cs`
- Create: `src/CodexUsageWidget.App/Views/AuditWindow.xaml`
- Create: `src/CodexUsageWidget.Infrastructure/Windows/TrayIconService.cs`, `StartupRegistration.cs`, `WindowPlacementService.cs`, `WindowsNotificationService.cs`
- Test: `tests/CodexUsageWidget.App.Tests/ViewModels/*Tests.cs`
- Test: `tests/CodexUsageWidget.Infrastructure.Tests/Windows/*Tests.cs`

**Interfaces:**
- Consumes: immutable monitor/activation state and command services only.
- Produces: borderless draggable topmost widget, resident tray lifecycle, per-user startup registration, local redacted audit view.

- [ ] Write failing view-model tests for `5h`/`Weekly`, percentages/progress/countdowns, unavailable/stale states, sync time, exact `100%·计时已启动`, >30/11-30/<=10 thresholds, and command enablement.
- [ ] Write failing Windows service tests for show/hide-without-exit, tray command inventory with no force action, pause preserving monitoring, startup defaults/persistence/safety override, one-time notifications, DPI/work-area placement, and second-instance bring-forward.
- [ ] Implement WPF resources, cards, view models, tray, audit window, startup registration, notifications, and placement persistence with accessible Chinese labels.
- [ ] Run App/Windows tests and launch a non-generating design-data smoke mode to visually verify layout and hide/restore behavior.

### Task 7: Security, fake-server end-to-end, packaging, and documentation

**Files:**
- Create: `tests/CodexUsageWidget.EndToEnd.Tests/*`
- Create: `scripts/publish.ps1`, `scripts/package.ps1`, `installer/install.ps1`, `installer/uninstall.ps1`
- Create: `README.md`, `docs/installation.md`, `docs/security.md`, `docs/troubleshooting.md`, `docs/acceptance-matrix.md`

**Interfaces:**
- Produces: self-contained `win-x64` package and non-admin per-user install/uninstall flow.

- [ ] Write failing sensitive-data scanners for logs, settings, SQLite, crash reports, and audit export fixtures.
- [ ] Add fake-server E2E cases for external usage, account switch, stale cached 100%, sleep/resume, clock change, network loss, App Server restart, malformed/sparse data, cleanup failure, model fallback, and the narrow external-use race.
- [ ] Implement publish/package/install/uninstall/upgrade/rollback scripts and documentation, with optional local-data removal only after active suppression periods.
- [ ] Run full tests, publish self-contained `win-x64`, install to a temporary per-user location, launch smoke mode, verify startup registration and uninstall, then scan artifacts for secrets.

### Task 8: Authenticated read-only and explicitly approved real acceptance

**Files:**
- Update: `docs/acceptance-matrix.md`
- Update: `openspec/changes/add-codex-usage-widget/tasks.md`

**Interfaces:**
- Uses production build and current authenticated Codex App Server; no test bypasses safety gates.

- [ ] Run an authenticated read-only smoke test proving real 5h/weekly mapping, first freshness, update notification, 60-second reconciliation, local countdown, stale recovery, and zero `turn/start` calls.
- [ ] Only when the authenticated five-hour bucket is fully unused and the prior explicit approval remains applicable, run exactly one production guarded activation and verify changed future `resetsAt`, temporary-thread deletion/deferred-cleanup state, redacted audit data, and zero later `turn/start` calls in the guarded period.
- [ ] Execute the complete automated suite and the acceptance matrix; mark each OpenSpec task complete only when its direct evidence exists.
- [ ] Run strict OpenSpec validation and final code review; leave any environment-dependent acceptance item unchecked if authoritative evidence cannot be obtained.
