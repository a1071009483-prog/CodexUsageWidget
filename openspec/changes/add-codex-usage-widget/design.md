## Context

The repository currently contains only an empty OpenSpec workspace. This change introduces a new Windows desktop application rather than modifying an existing runtime. The application must keep Codex's five-hour and weekly allowance visible, update the display without requiring the Codex UI to be open, and start a completely unused five-hour window with one minimal Codex turn.

Codex App Server is the supported integration boundary. Its JSON-RPC protocol exposes account state, model discovery, `account/rateLimits/read`, `account/rateLimits/updated`, and thread/turn lifecycle operations. Rate-limit reads do not run a model. Starting a quota window has no dedicated API, so the activation path necessarily starts one real turn and must be engineered as an at-most-once workflow.

The primary constraints are:

- Windows 10/11, single interactive user, and no administrator requirement for normal use.
- ChatGPT-backed Codex authentication; API-key-only use does not expose the relevant included-plan windows.
- Monitoring must not generate model turns.
- Automatic activation is allowed only while a fresh five-hour snapshot reports `usedPercent = 0` and neither server state nor durable local state proves that the same five-hour timer is already active.
- Once any activation turn begins generating, the application must issue no further turn in that guarded five-hour period, including after a crash or restart.
- The server percentage can be rounded, so activation success is based on a changed future `resetsAt`, not a required visual transition from 100% to 99% remaining.
- Weekly remaining capacity is displayed but does not gate activation.

## Goals / Non-Goals

**Goals:**

- Deliver a native `.NET 8` WPF floating widget and tray application that starts with Windows by default.
- Surface fresh five-hour and weekly remaining percentages, reset times, countdowns, connection health, and data freshness.
- Update from App Server notifications within one second and reconcile through a 60-second read-only poll.
- Activate a fully unused five-hour window through at most one accepted generation per account and guarded period.
- Survive process crashes, Windows restarts, sleep/resume, App Server restarts, and temporary network failures without duplicate generation.
- Prefer the newest recognized available lightweight model and fall back to the current default model when no lightweight candidate exists.
- Keep authentication material outside the application and persist only redacted local state and audit data.
- Make the no-further-consumption property observable and testable by auditing every attempted `turn/start` boundary.

**Non-Goals:**

- Bypassing, resetting, increasing, or otherwise circumventing an OpenAI usage limit.
- Guaranteeing that the service displays exactly 99% remaining after a minimal turn.
- Triggering when the five-hour bucket has any recorded use, or providing a manual control that bypasses the guarded eligibility flow.
- Scraping the Codex dashboard, parsing the interactive `/status` display, reading browser cookies, or directly reading raw Codex authentication files.
- Supporting API-key-only allowance monitoring, macOS, Linux, multi-user services, or remote fleet administration in the first release.
- Predicting the exact quota cost of a model turn or guaranteeing that a provider-side policy will continue to treat the first turn as a five-hour window start.

## Decisions

### 1. Use .NET 8 WPF for the resident Windows shell

The application will be a WPF process with a borderless draggable topmost window and a system-tray icon. A named Windows mutex enforces a single instance; a small named-pipe signal brings the existing widget forward when a second launch is attempted. The application remembers its monitor-relative position and validates it against the current multi-monitor work area and DPI whenever displays change.

The tray owns show/hide, read-only refresh, pause/resume activation, Windows-startup preference, audit viewing, reconnect, and exit. The floating widget also exposes one clearly labeled safe manual check. It enters the same guarded coordinator as automatic activation, but supplies one-shot user authorization so it can be evaluated while automatic triggering is paused; it never bypasses quota eligibility or changes the persisted pause state. Startup is registered per user and is opt-out. The widget shows official percentages and a separate activation state; it never fabricates 99% when the server still reports 100%.

Alternatives considered:

- Tauri would provide a flexible web UI but adds Rust and WebView2 integration complexity for a Windows-only utility.
- Electron would shorten UI prototyping but has materially higher resident memory and packaging overhead.

### 2. Run Codex App Server as a supervised child process

`CodexAppServerClient` starts `codex app-server` over its documented stdio JSONL transport, performs the initialization handshake, and serializes JSON-RPC requests through a typed boundary. It subscribes to account and rate-limit notifications and restarts the child with bounded exponential backoff after an unexpected exit. The application uses App Server-managed authentication and never opens or persists raw tokens.

Before enabling activation, capability checks must confirm that the installed CLI supports the required account, model, thread, turn, delete, and rate-limit methods. Missing or incompatible methods fail closed and remain visible in the UI. Dashboard scraping and parsing CLI presentation text were rejected because they are fragile and require broader access to private session state.

### 3. Normalize quota data behind a read-only monitor

`QuotaMonitor` maps App Server responses into an immutable snapshot:

- Account namespace and plan metadata.
- Five-hour bucket, identified by a `windowDurationMins` value of 300.
- Weekly bucket, selected from the multi-bucket/secondary data by its server duration and label rather than by fixed array position.
- `usedPercent`, clamped remaining percentage (`100 - usedPercent`), `resetsAt`, server-read time, and freshness state.

Notifications publish a new UI snapshot within one second. A 60-second poll repairs missed notifications and provides the maximum normal convergence interval. Countdown text is calculated locally from UTC `resetsAt` and updated once per second without contacting the service. A snapshot becomes stale after two minutes without a successful server read; stale data remains visible but is never eligible for activation.

Account or workspace changes dispose of the current monitor state and load a separate persistence namespace. The namespace uses a stable opaque account/workspace identifier when available, otherwise a hash of normalized account identity plus plan/workspace context. Raw email addresses are not stored.

### 4. Persist activation state transactionally before any turn request

A local SQLite database under `%LOCALAPPDATA%\CodexUsageWidget` stores settings, account namespaces, activation attempts, pending cleanup, and redacted audit entries. SQLite is preferred over ad hoc JSON because a unique constraint and transaction can atomically establish the at-most-once guard before the process crosses the model boundary.

An activation attempt records:

- Account namespace and generated attempt ID.
- Observation and attempt timestamps in UTC.
- Pre-activation `usedPercent` and `resetsAt`.
- A suppression deadline of at least five hours from the attempt, extended through a later verified server reset when necessary.
- Selected model, whether a turn was accepted/started, terminal outcome, post-activation snapshot, and cleanup state.

The window key uses the authoritative server limit/reset epoch when one exists. A completely unused bucket may not yet have a stable active reset identity; in that case the transaction creates a local eligibility epoch and reuses it across process restart until the suppression deadline. The coordinator loads and validates this state before starting App Server or evaluating quota data. A corrupt or unreadable database disables activation and requires explicit repair; it is not silently replaced, because losing a live guard could duplicate consumption.

### 5. Use a guarded activation state machine

The activation flow is:

1. Require either enabled automatic activation or one explicit manual-check invocation, and a fresh 300-minute bucket to report `usedPercent = 0`. A single future `resetsAt` is only a candidate signal because an unused bucket may expose a rolling placeholder equal to the read time plus five hours. Manual authorization applies to one evaluation only and does not mutate the automatic-trigger setting.
2. Confirm the same eligibility through two independent reads separated by a two-second debounce. If `resetsAt` advances with the elapsed observation time while both horizons remain approximately five hours, classify it as an unused-window placeholder; if the future instant remains stable or otherwise cannot be proven rolling, fail closed as an active timer. Rolling placeholders use the durable local eligibility epoch rather than an unstable authoritative window key.
3. In one immediate database transaction, verify no active suppression record and insert the durable pending attempt. For rolling placeholders, the transaction searches the whole account/workspace scope for any unexpired local guard before inserting, so crossing a computed epoch boundary cannot create a second lock.
4. Before the final read, allow at least one second after the prior rolling observation so positive movement can be proven at the protocol timestamp resolution. Then perform the final read immediately before model selection. Revalidate exact-zero state and continuity of the rolling placeholder; equality or otherwise unprovable movement fails closed. If another Codex task has already used the window or established a stable reset, mark the attempt satisfied externally and do not create a thread.
5. Resolve a model, create a fresh temporary thread in an empty read-only working directory, and start a constant fixed-response turn with the lowest supported reasoning effort, no client-supplied dynamic tools, and a non-interactive approval policy.
6. Once App Server accepts or announces the turn, mark `turn_started` and prohibit all further model fallbacks and retries for the suppression period.
7. Wait for the short turn to complete. On timeout or an unexpected tool-use event, interrupt it once and retain the guard.
8. Re-read rate limits, using read-only checks for up to 60 seconds. For a baseline without a rolling placeholder, a changed `resetsAt` in the expected future five-hour range marks success even if `usedPercent` remains rounded to zero. For a rolling baseline, two consecutive post-generation reads must report the same changed future reset; a value that continues moving with observation time remains ambiguous.
9. Delete the completed temporary thread and persist the post-state. A delete failure creates cleanup work that can run later without a model call.

Failure, timeout, ambiguous verification, child-process loss, or application crash leaves the attempt guarded until its suppression deadline. The application never tries to compensate with another generation. After the deadline, only a new fresh 100%-remaining observation can open a new attempt.

There is no provider-side compare-and-start primitive. External Codex activity can begin in the narrow interval after the final read but before the automatic request reaches the service. The final preflight minimizes this race but cannot eliminate it, and quota data cannot attribute the external change to a particular task; the local at-most-once guarantee still applies to the widget itself.

### 6. Discover a lightweight model dynamically, then fall back to the default

Before creating the temporary thread, `LightweightModelSelector` calls `model/list`. It evaluates an application-updatable ordered policy of recognized lightweight model families and chooses the newest available member of the highest-priority family. Version comparison is semantic where the model ID exposes a version and stable ordered fallback rules cover non-semantic names.

The App Server catalog does not expose a normative quota-cost or `lightweight` field, so a newly named family cannot be classified safely from catalog presence alone. If no recognized lightweight candidate is available, the selector chooses the catalog entry marked `isDefault = true`, as explicitly accepted for this change. This fallback can consume more allowance and is recorded in the UI/audit state.

If a selected candidate returns an explicit model-unavailable error before any turn is accepted or started, the selector refreshes the catalog and advances to the next eligible candidate. The first accepted/started generation ends fallback immediately. Ambiguous errors are treated as possibly consumed and therefore are not retried.

### 7. Separate display state from activation eligibility

The widget renders the last normalized snapshot even while offline. Freshness and trigger eligibility are separate values: a cached 100% display cannot activate anything. The UI states are `connecting`, `monitoring`, `eligible`, `triggering`, `timer-active`, `paused`, `stale`, `authentication-required`, and `error`.

Color thresholds are green above 30% remaining, yellow from 10% through 30%, and red below 10%. If activation succeeds while the server still rounds remaining usage to 100%, the label is `100% · timer started` and uses the verified future reset time.

Notifications are emitted once for successful activation, failed/ambiguous activation, and required sign-in. Normal polls and countdown ticks are silent.

### 8. Keep local data minimal and auditable

Audit rows contain timestamps, account-namespace hash, model ID, pre/post quota fields, reset times, whether the turn crossed the generation boundary, outcome, and redacted error category. They exclude prompts, task output, raw email, cookies, access tokens, and authentication files. The activation prompt is a compiled constant and is not copied into audit data.

All model-boundary calls pass through one interface so tests and production audit can prove that no `turn/start` was issued after a guarded turn began. Monitoring, cleanup, UI refresh, and countdown logic cannot access that interface directly.

## Risks / Trade-offs

- **[No atomic service-side conditional activation]** → Perform two confirmations plus a final preflight, disclose the narrow external-use race, and enforce a strict local at-most-once boundary.
- **[A minimal turn may leave the percentage rounded at 100%]** → Verify the future `resetsAt` transition and display the official percentage alongside a separate timer-active state.
- **[Model catalogs do not label quota weight]** → Maintain a curated lightweight-family policy, refresh `model/list` before activation, and visibly audit fallback to the default model.
- **[The default fallback can consume more than a lightweight model]** → Use it only when no recognized lightweight model is available and still permit only one accepted generation.
- **[App Server protocol or CLI availability can change]** → Detect required capabilities at startup, use typed tolerant parsing, and fail closed on incompatible versions.
- **[Service policy may not preserve first-use window semantics]** → Treat one explicitly authorized end-to-end test as a release gate and avoid retries when verification is ambiguous.
- **[Polling can create unnecessary account traffic]** → Prefer pushed updates and use one 60-second read-only reconciliation interval with backoff while offline.
- **[State corruption could erase a live guard]** → Use transactional storage, backups for diagnostics, and disable activation rather than recreate state automatically.
- **[Sleep, clock changes, and display changes can invalidate local presentation]** → Re-read server state on resume, use UTC server timestamps, and revalidate window placement/DPI.
- **[Temporary task cleanup can fail]** → Persist cleanup work and retry only the non-model delete operation on later starts.

## Migration Plan

1. Create the WPF solution, typed App Server protocol layer, SQLite schema, and fake-server test harness.
2. Ship read-only monitoring and the widget first; verify five-hour/weekly mapping and freshness behavior against an authenticated account without starting a turn.
3. Add the activation coordinator behind a local feature flag and complete crash/restart, model-selection, stale-data, and at-most-once tests using the fake server.
4. Run one explicitly authorized real-account activation test from a fully unused five-hour window and verify the `resetsAt` transition and absence of later `turn/start` calls.
5. Enable automatic activation and Windows startup by default for the installed user, retaining tray controls to pause either behavior.

Rollback disables the Windows startup entry, stops the child App Server, and uninstalls the application. Local state can remain for audit or be removed explicitly after any active five-hour suppression period has passed. An upgrade must migrate and validate the database before activation is re-enabled.

## Open Questions

- Which lightweight model-family rules match the model catalog shipped with the target Codex CLI at implementation time?
- Does every supported ChatGPT plan expose a distinguishable weekly bucket, or must some plans show weekly status as unavailable?
- What exact App Server error shapes reliably prove that a model-unavailable failure occurred before generation began?

These questions are implementation validation items. They do not relax the fail-closed or at-most-once requirements.
