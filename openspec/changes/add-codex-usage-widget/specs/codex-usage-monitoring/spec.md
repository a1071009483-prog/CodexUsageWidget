## ADDED Requirements

### Requirement: Official App Server monitoring boundary
The system SHALL obtain Codex account and rate-limit state through the documented Codex App Server JSON-RPC protocol. It MUST NOT scrape a web dashboard, parse the interactive `/status` presentation, read browser cookies, or read raw Codex authentication files.

#### Scenario: Supported App Server is available
- **WHEN** the application connects to an installed compatible Codex App Server
- **THEN** it completes the protocol initialization handshake and requests account and rate-limit state through typed JSON-RPC calls

#### Scenario: Required protocol capability is unavailable
- **WHEN** the installed Codex App Server does not support a required account or rate-limit method
- **THEN** monitoring reports an incompatible-runtime state and automatic activation remains disabled

### Requirement: Supported account authentication
The system SHALL monitor included-plan five-hour and weekly windows only for an App Server account backed by Codex services. API-key-only or otherwise unsupported authentication MUST be reported as unsupported and MUST NOT be interpreted as an account with zero usage.

#### Scenario: ChatGPT-backed account is authenticated
- **WHEN** App Server reports an authenticated Codex-backed ChatGPT account
- **THEN** the system requests that account's current rate-limit buckets

#### Scenario: Authentication is missing or unsupported
- **WHEN** App Server reports no authenticated account, expired authentication, or API-key-only authentication
- **THEN** the system publishes an authentication-required or unsupported state and provides no trigger-eligible snapshot

### Requirement: Five-hour and weekly bucket discovery
The system SHALL identify the five-hour bucket by a valid `windowDurationMins` value of `300`. It SHALL identify the weekly bucket from the multi-bucket or secondary response by server-provided duration and label metadata and MUST NOT rely only on array position or on primary/secondary ordering. An absent or ambiguous bucket MUST be represented as unavailable rather than inferred.

#### Scenario: Both target buckets are discoverable
- **WHEN** the rate-limit response contains an unambiguous 300-minute bucket and an unambiguous weekly bucket
- **THEN** the system maps them to separate `5h` and `Weekly` normalized states

#### Scenario: Weekly bucket is unavailable
- **WHEN** a fresh response contains a valid five-hour bucket but no unambiguous weekly bucket
- **THEN** the system publishes fresh five-hour state and marks weekly state unavailable without fabricating a weekly percentage or reset time

#### Scenario: Five-hour bucket is unavailable
- **WHEN** a response contains no valid unambiguous 300-minute bucket
- **THEN** the system marks five-hour state unavailable and produces no trigger-eligible snapshot

### Requirement: Quota value normalization
For every valid bucket, the system SHALL preserve the server's raw `usedPercent`, compute remaining percentage as `clamp(100 - usedPercent, 0, 100)`, and interpret `resetsAt` as a UTC Unix timestamp in seconds. Invalid required numeric fields MUST invalidate that bucket rather than be coerced into an eligible value.

#### Scenario: Valid quota values are normalized
- **WHEN** a bucket reports `usedPercent = 37` and a valid future `resetsAt`
- **THEN** the normalized state reports 63% remaining and the corresponding UTC reset instant

#### Scenario: Server values exceed display bounds
- **WHEN** a valid response reports a percentage outside the display range because of rounding or service behavior
- **THEN** the display remaining value is clamped to 0 through 100 while the raw value remains available for eligibility and audit decisions

#### Scenario: Required values are malformed
- **WHEN** `usedPercent`, `windowDurationMins`, or another required bucket field cannot be parsed safely
- **THEN** that bucket is marked invalid and cannot be used for automatic activation

### Requirement: Initial synchronization deadline
When a compatible App Server, supported authentication, and network connectivity are available, the system MUST publish the first current rate-limit snapshot within 60 seconds of application startup or account connection.

#### Scenario: Normal startup synchronization
- **WHEN** the application starts with a compatible reachable App Server and supported authenticated account
- **THEN** it publishes current five-hour and weekly state or explicit per-bucket unavailability within 60 seconds

### Requirement: Near-real-time notification handling
The system SHALL subscribe to `account/rateLimits/updated` notifications and MUST publish the corresponding normalized state to UI consumers within one second after receiving a valid notification.

#### Scenario: Rate-limit update notification arrives
- **WHEN** App Server emits a valid rate-limit update for the current account and workspace
- **THEN** monitoring publishes the updated percentages, reset times, freshness, and synchronization time within one second

#### Scenario: Notification targets obsolete account state
- **WHEN** a delayed notification belongs to an account or workspace that is no longer current
- **THEN** monitoring discards it and does not overwrite the current account's state

### Requirement: Sixty-second read-only reconciliation
While connected, the system SHALL perform a read-only rate-limit reconciliation at least once every 60 seconds, with bounded backoff while the service is unavailable. A user-requested Refresh Now operation SHALL run the same read-only reconciliation. Reconciliation MUST NOT create a Codex thread or turn.

#### Scenario: A notification is missed
- **WHEN** server-side quota changes but no usable update notification reaches the application
- **THEN** the next successful reconciliation publishes the change no later than 60 seconds after the prior successful periodic read

#### Scenario: User requests an immediate refresh
- **WHEN** the user invokes Refresh Now
- **THEN** the system performs one immediate rate-limit read and publishes the result without starting a Codex task

### Requirement: Local reset countdown
For every valid future `resetsAt`, the system SHALL publish a locally calculated remaining duration at least once per second. Countdown ticks MUST use the last authoritative UTC reset instant and MUST NOT contact App Server or invoke a model.

#### Scenario: A reset countdown is active
- **WHEN** a fresh bucket contains a reset instant in the future
- **THEN** its published countdown decreases locally at least once per second between server synchronizations

#### Scenario: The reset instant passes
- **WHEN** local time reaches a displayed bucket's `resetsAt`
- **THEN** the system marks that countdown due and requests a read-only reconciliation rather than assuming the new quota value

### Requirement: External usage convergence
Quota changes caused by Codex activity outside the widget SHALL appear without restarting the widget or requiring manual refresh. Under normal connectivity, the change MUST appear within one second of a valid notification or, when no notification is received, within the 60-second reconciliation bound.

#### Scenario: User consumes quota in another Codex surface
- **WHEN** another Codex client changes the current account's five-hour or weekly usage
- **THEN** the widget's monitoring state converges to the new server values within the notification or reconciliation bound

### Requirement: Explicit freshness and stale-state boundary
The system SHALL record the time of the last successful authoritative rate-limit read. A snapshot MUST become stale after two minutes without another successful authoritative read, and stale or last-known values MUST NOT be exposed as eligible input to automatic activation.

#### Scenario: Synchronization remains healthy
- **WHEN** authoritative reads continue succeeding less than two minutes apart
- **THEN** monitoring marks the latest normalized snapshot fresh and updates the last-successful-sync time

#### Scenario: Snapshot exceeds the freshness limit
- **WHEN** two minutes pass after the last successful authoritative read
- **THEN** monitoring marks all affected values stale, retains them only as last-known display data, and exposes no trigger-eligible five-hour state

### Requirement: Connection failure and recovery
On network loss, App Server exit, authentication expiry, or transient protocol failure, the system SHALL retain last-known display data with an explicit connection or authentication error and SHALL suspend trigger eligibility. After recovery it MUST obtain a fresh authoritative read before clearing the stale/error state.

#### Scenario: Network or App Server connection is lost
- **WHEN** monitoring cannot complete an authoritative rate-limit read
- **THEN** it preserves last-known values, exposes the failure and freshness state, and provides no trigger-eligible snapshot

#### Scenario: Connectivity is restored
- **WHEN** the connection or authentication becomes usable again
- **THEN** monitoring performs a fresh read and only then republishes current values as fresh

### Requirement: Account and workspace isolation
The system MUST scope normalized quota state by the current account and workspace. On an account or workspace change, it SHALL stop publishing the prior scope as current, discard in-flight updates for that scope, and require a fresh read before the new scope can become trigger eligible.

#### Scenario: Active account or workspace changes
- **WHEN** App Server reports a different current account or workspace
- **THEN** monitoring clears current-scope freshness, ignores obsolete responses, and publishes the new scope only after a successful fresh read

### Requirement: Monitoring performs no model work
The monitoring subsystem MUST NOT call `thread/start`, `turn/start`, or any equivalent model-generation method. Its allowed service operations SHALL be limited to initialization, account/model metadata needed for status, rate-limit reads, subscriptions, health checks, and non-model recovery.

#### Scenario: Monitoring runs through multiple quota windows
- **WHEN** only monitoring, notification handling, countdown ticks, and reconciliation are active
- **THEN** the number of model turns created by the monitoring subsystem remains exactly zero
