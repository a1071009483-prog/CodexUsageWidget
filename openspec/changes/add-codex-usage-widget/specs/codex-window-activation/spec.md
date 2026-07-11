## ADDED Requirements

### Requirement: Strict automatic activation eligibility
The system SHALL consider automatic activation eligible only when automatic activation is enabled, the current account and workspace have a fresh five-hour bucket whose raw `usedPercent` is exactly `0`, corresponding to 100% remaining, and neither server state nor durable local state proves that the same five-hour timer is already active. The system MUST NOT apply a weekly-usage threshold or require the weekly bucket to be fresh or available.

#### Scenario: Fresh completely unused five-hour bucket is eligible
- **WHEN** automatic activation is enabled and a fresh five-hour bucket reports `usedPercent = 0`
- **THEN** the system admits the candidate to the confirmation sequence regardless of the weekly bucket value

#### Scenario: Inexact or stale five-hour state is ineligible
- **WHEN** the five-hour bucket is stale, unavailable, or reports any `usedPercent` other than exactly `0`, or automatic activation is disabled
- **THEN** the system does not create or send an automatic activation generation

#### Scenario: Weekly state does not gate activation
- **WHEN** the five-hour eligibility conditions are satisfied and the weekly bucket is low, exhausted, stale, or unavailable
- **THEN** the system does not reject the candidate because of weekly state

#### Scenario: Rounded 100 percent is already timing
- **WHEN** the bucket reports `usedPercent = 0` but a verified future `resetsAt` or durable success record proves that its five-hour timer is already active
- **THEN** the system treats the activation goal as satisfied and sends no generation

### Requirement: Consecutive confirmation and final read-only gate
The system MUST obtain two consecutive, independently fetched, fresh confirmations of `usedPercent = 0` for the same account, workspace, and five-hour window, with no intervening ineligible observation. After acquiring the durable activation lock, it SHALL perform one final read-only quota check immediately before sending the generation request and revalidate the same eligibility conditions.

#### Scenario: Candidate passes all three observations
- **WHEN** two consecutive fresh observations confirm the same eligible five-hour window and the final read-only pre-send check confirms it again
- **THEN** the system records the durable lock before proceeding to model selection or any generation send

#### Scenario: Final gate no longer confirms eligibility
- **WHEN** the final read-only pre-send check is stale, unknown, targets a different window, or no longer reports exact `usedPercent = 0`
- **THEN** the system cancels the automatic send and does not issue a generation request

### Requirement: Durable scoped write-ahead deduplication
Before creating a temporary activation task or sending a generation, the system SHALL atomically persist and durably flush a write-ahead lock keyed by account identity, workspace identity, and five-hour window identity. It MUST treat an existing lock for that complete key as authoritative and block another activation attempt. The system SHALL use an authoritative server window/reset identity when one is available; otherwise it MUST create a durable local eligibility epoch that survives restart and remains active for at least the suppression period.

#### Scenario: Existing lock blocks a duplicate
- **WHEN** an activation candidate resolves to an account, workspace, and window key that already has a persistent lock
- **THEN** the system performs no temporary-task creation and sends no generation for that key

#### Scenario: Different scopes remain independent
- **WHEN** two candidates differ by account, workspace, or five-hour window identity
- **THEN** the system evaluates and locks them under distinct deduplication keys

#### Scenario: Server provides no stable unused-window identity
- **WHEN** an eligible completely unused bucket has no authoritative server window or reset identity
- **THEN** the system creates one durable local eligibility epoch and reuses it across restart until its suppression period ends

### Requirement: Crash-safe at-most-once generation
The system MUST recover persistent locks before evaluating activation after any process crash or restart and SHALL ensure that no account, workspace, and five-hour window key can cause more than one actual generation. Safety MUST take precedence over retrying an activation that might not have occurred.

#### Scenario: Crash after request transmission
- **WHEN** the process crashes after a generation request might have reached the server and restarts during the same five-hour window
- **THEN** the recovered lock prevents the system from sending another generation for that account and workspace window

#### Scenario: Crash before known transmission
- **WHEN** the process crashes with a durable lock but without durable proof that no request crossed the send boundary
- **THEN** the recovered lock still prevents another generation in the same scoped window

### Requirement: Dynamic model selection
Immediately before the first permitted generation attempt, the system SHALL obtain the current `model/list` result and select the newest available model among the returned entries that it recognizes as lightweight. If no recognized lightweight model is available, it MUST select the returned model whose `isDefault` value is `true` rather than relying on a hard-coded model identifier.

#### Scenario: Newest recognized lightweight model is selected
- **WHEN** `model/list` returns multiple available recognized lightweight models
- **THEN** the system selects the newest recognized lightweight entry from that current result

#### Scenario: Default model is the fallback
- **WHEN** `model/list` returns no available recognized lightweight model and includes an entry with `isDefault = true`
- **THEN** the system selects that default entry for the guarded generation

#### Scenario: No eligible model fails closed
- **WHEN** `model/list` returns neither an available recognized lightweight model nor an `isDefault = true` entry
- **THEN** the system sends no generation and retains the scoped lock

### Requirement: Model switching and retry boundary
The system MUST switch models only after an explicit server result proves that the selected model is unavailable before generation began. Such a switch SHALL refresh `model/list`, repeat the dynamic selection policy under the same lock, and MUST NOT occur for a timeout, transport failure, ambiguous result, or any result after the generation request could have started producing output.

#### Scenario: Explicit pre-generation unavailability permits a switch
- **WHEN** the server explicitly rejects the selected model as unavailable and confirms that generation did not begin
- **THEN** the system refreshes `model/list`, selects the next eligible candidate under the existing lock when one exists, and otherwise sends no generation

#### Scenario: Generation start permanently closes retry
- **WHEN** the generation request is accepted, any generation event is observed, or the result is ambiguous after crossing the send boundary
- **THEN** the system never switches models or retries generation for that scoped five-hour window

### Requirement: Isolated minimal no-tool generation
The system SHALL run activation in a dedicated temporary task isolated from user tasks, with a read-only workspace, a non-interactive approval policy, the lowest supported reasoning effort, no client-supplied dynamic tools, and a minimal fixed-response contract. The activation prompt MUST request only that fixed response and MUST prohibit tool calls and workspace mutation.

#### Scenario: Guarded activation request is constructed
- **WHEN** an eligible locked candidate reaches the generation step
- **THEN** the submitted request uses an isolated temporary task, read-only execution, non-interactive approvals, minimum reasoning, a minimal fixed-response contract, and no client-supplied dynamic tools

#### Scenario: Required isolation cannot be enforced
- **WHEN** the runtime cannot enforce read-only isolation, non-interactive approvals, minimum reasoning, or the fixed-response contract
- **THEN** the system sends no activation generation and retains the scoped lock

#### Scenario: An unexpected tool event occurs
- **WHEN** the server emits a tool-use event despite the no-tool activation request
- **THEN** the system interrupts the turn, records a guarded failure, and sends no replacement generation for that scoped window

### Requirement: Reset-time activation verification
The system SHALL declare activation successful when a fresh post-generation five-hour bucket updates `resetsAt` from the pre-send baseline to a future time consistent with a newly started window of approximately five hours. It MUST use this reset-time transition as the success signal even when percentage rounding still displays 100% remaining or reports `usedPercent = 0`.

#### Scenario: Reset time advances while displayed remaining stays at 100 percent
- **WHEN** a fresh post-generation bucket has an updated future `resetsAt` approximately five hours away while the rounded display remains 100% remaining
- **THEN** the system records activation success without requiring a visible percentage change

#### Scenario: Reset transition is not proven
- **WHEN** verification ends without a fresh bucket that proves the required future `resetsAt` transition
- **THEN** the system treats the outcome as unknown and does not retry generation in that scoped window

### Requirement: External activation takes precedence
If authoritative quota state proves that the target five-hour window started before the automatic generation is sent, the system SHALL cancel the pending automatic task, send no automatic generation, and mark that scoped window as satisfied externally while preserving deduplication state. The system MUST NOT require or claim attribution to a particular external task.

#### Scenario: External activity starts the pending window
- **WHEN** authoritative fresh state shows that the target window started while automatic activation is pending but before its generation is sent
- **THEN** the system cancels the automatic task and records the window as externally satisfied without automatic generation or source attribution

### Requirement: Fail-closed terminal attempt handling
For every activation failure, timeout, transport error, malformed response, verification timeout, or unknown or ambiguous outcome other than an explicitly proven pre-generation model-unavailability rejection, the system MUST retain the persistent scoped lock through the target window and SHALL send no further generation for that key.

#### Scenario: Activation result is unknown
- **WHEN** an activation operation times out or loses its connection without proving that generation did not begin
- **THEN** the system retains the lock and does not retry during the same account, workspace, and five-hour window

#### Scenario: Activation fails definitively
- **WHEN** activation fails for any reason that is not the permitted explicit pre-generation model-unavailability case
- **THEN** the system retains the lock and sends no further generation for the scoped window

### Requirement: Successful cleanup and non-sensitive audit
After verified activation success, the system SHALL request deletion of the temporary activation task and MUST retain only non-sensitive audit metadata. Audit data MUST exclude prompt and response content, credentials, tokens, raw authentication material, and unredacted user workspace content.

#### Scenario: Successful activation is cleaned up
- **WHEN** reset-time verification establishes activation success
- **THEN** the system deletes the temporary activation task and records only redacted non-sensitive metadata needed to audit the scoped attempt and outcome

### Requirement: Deferred cleanup without reactivation
If deletion of a temporary activation task fails, the system SHALL only enqueue that task for later cleanup. It MUST preserve the verified success result and lock, and MUST NOT retry activation, switch models, or send another generation because cleanup failed.

#### Scenario: Temporary-task deletion fails
- **WHEN** deletion fails after activation has already been verified as successful
- **THEN** the system queues deferred cleanup, leaves activation successful, and performs no additional generation for the scoped window

### Requirement: No eligibility bypass
The system MUST NOT expose a force-trigger control, command, API, diagnostic path, or setting that bypasses the fresh exact-zero five-hour eligibility condition. Every user-invoked activation check SHALL pass through the same eligibility, confirmation, locking, and final read-only gates as automatic evaluation.

#### Scenario: User requests activation while the five-hour bucket is not completely unused
- **WHEN** any user-accessible trigger path is invoked and the fresh five-hour bucket does not report exact `usedPercent = 0`
- **THEN** the system sends no activation generation and offers no override that can force it
