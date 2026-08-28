## ADDED Requirements

### Requirement: Windows resident application
The system SHALL provide a Windows-only resident desktop application implemented with .NET 8 and WPF. The application SHALL continue running in the notification area while the floating widget is hidden and SHALL terminate only when the user chooses Exit or Windows ends the user session.

#### Scenario: Widget is hidden without terminating the application
- **WHEN** the user hides or closes the floating widget
- **THEN** the widget disappears while the resident process and tray icon remain available

### Requirement: Single application instance
The application MUST enforce a single running instance for the current Windows user and MUST prevent a later launch from creating a second widget, tray icon, or Codex App Server connection.

#### Scenario: A second launch is attempted
- **WHEN** the application is launched while its instance for the current user is already running
- **THEN** the existing instance is brought forward and the later process exits without starting duplicate background work

### Requirement: Floating widget behavior and placement
The widget SHALL be borderless, draggable, and always on top of ordinary windows. The application SHALL remember its last valid position and SHALL restore it correctly across restarts, display topology changes, and per-monitor DPI changes, keeping the entire widget reachable within an available monitor work area.

#### Scenario: The user repositions the widget
- **WHEN** the user drags the widget and later restarts the application with the same displays available
- **THEN** the widget reopens at the remembered visual position and remains always on top

#### Scenario: The remembered display is unavailable or its DPI changed
- **WHEN** the application restores a saved position after a monitor is removed or a monitor DPI setting changes
- **THEN** it maps or clamps the widget into an available monitor work area so the complete widget remains reachable

### Requirement: Quota and status presentation
The widget SHALL display separate `5h` and `Weekly` sections containing remaining percentage, a corresponding progress indicator, and a countdown derived from each bucket's `resetsAt`. It SHALL also display the current connection, freshness, and automatic-trigger status plus the time of the last successful synchronization. Countdown text SHALL update locally at least once per second while a future `resetsAt` is known. If a changed five-hour `resetsAt` proves that timing has started while the remaining percentage rounds to 100%, the five-hour state MUST display the exact text `100%·计时已启动` rather than representing the bucket as unused.

#### Scenario: A fresh quota snapshot is available
- **WHEN** fresh five-hour and weekly data includes remaining values and reset timestamps
- **THEN** both sections show their remaining percentages, matching progress, live reset countdowns, current status, and last-successful-sync time

#### Scenario: Timing has started while the percentage rounds to 100%
- **WHEN** the five-hour reset timestamp has advanced to an active window and its normalized remaining value rounds to 100%
- **THEN** the widget displays `100%·计时已启动` and continues showing the active reset countdown

### Requirement: Remaining-quota color thresholds
The widget SHALL use a normal color when remaining quota is greater than 30%, a warning color when it is greater than 10% and no greater than 30%, and a critical color when it is 10% or less. The same thresholds MUST apply to both the numeric value and its progress indicator for both buckets.

#### Scenario: Remaining quota crosses the warning threshold
- **WHEN** a bucket changes from 31% remaining to 30% remaining
- **THEN** its value and progress indicator change from the normal color to the warning color

#### Scenario: Remaining quota crosses the critical threshold
- **WHEN** a bucket changes from 11% remaining to 10% remaining
- **THEN** its value and progress indicator change from the warning color to the critical color

### Requirement: Stale-data indication
The widget MUST present a prominent stale-data indicator whenever the monitoring state marks the latest quota snapshot stale. It MUST keep the last-successful-sync time visible and MUST visually distinguish last-known values from current values rather than implying that stale values are fresh.

#### Scenario: The latest snapshot becomes stale
- **WHEN** the monitoring freshness deadline expires without a successful synchronization
- **THEN** the widget marks the quota data stale, distinguishes the last-known values, and retains the last-successful-sync time

### Requirement: Tray controls and lifecycle
The resident application SHALL provide tray commands for Show/Hide, Refresh Now, Pause/Resume Automatic Triggering, Start with Windows, Audit, Reconnect, and Exit. These commands MUST remain available while the widget is hidden. Refresh Now MUST request a read-only quota reconciliation; Pause/Resume MUST affect automatic triggering without stopping read-only monitoring; Start with Windows MUST update the sign-in startup setting; Audit MUST open the local audit view; Reconnect MUST re-establish the Codex App Server connection; and Exit MUST terminate the resident application cleanly.

#### Scenario: The user refreshes from the tray
- **WHEN** the user chooses Refresh Now
- **THEN** the application requests an immediate read-only quota reconciliation without creating a Codex turn

#### Scenario: The user pauses and resumes automatic triggering
- **WHEN** the user pauses automatic triggering and later resumes it from the tray
- **THEN** automatic triggering is disabled and then re-enabled while quota monitoring continues in both states

#### Scenario: The user invokes the remaining tray commands
- **WHEN** the user chooses Show/Hide, Start with Windows, Audit, Reconnect, or Exit
- **THEN** the application respectively changes widget visibility, persists startup registration, opens the audit view, re-establishes the connection, or shuts down cleanly

### Requirement: Safe initial defaults and persisted preferences
On a fresh installation, the application SHALL default Start with Windows to enabled and automatic triggering to enabled. It SHALL persist later user changes to both settings and restore those choices on subsequent launches, except that a fail-closed safety condition MUST override the enabled automatic-trigger preference.

#### Scenario: The application runs for the first time
- **WHEN** no prior local settings exist
- **THEN** Start with Windows and automatic triggering are both enabled and shown as enabled in the tray

#### Scenario: The user changes a default
- **WHEN** the user disables startup or pauses automatic triggering and restarts the application
- **THEN** the application restores the user's persisted choice unless safety validation requires automatic triggering to remain disabled

### Requirement: One-time notifications
The application SHALL issue a notification when an automatic activation first reaches a terminal success, failure, or fail-closed safety outcome. It MUST persist a non-sensitive notification key and MUST suppress duplicate notifications for the same account, five-hour window, and outcome, including after application restart.

#### Scenario: A completed outcome is observed repeatedly
- **WHEN** the same activation outcome is received more than once or is recovered again after restart
- **THEN** the user receives exactly one notification for that account, five-hour window, and outcome

### Requirement: Local and non-sensitive application data
The application SHALL store settings, durable anti-repeat state, notification deduplication state, and redacted audit metadata under the current user's `%LOCALAPPDATA%` directory. Local files MUST NOT contain authentication tokens, browser cookies, raw Codex credentials, or any Codex task, turn, or prompt body. Audit records SHALL contain only the non-sensitive metadata needed to identify time, redacted account/window identity, action, and outcome.

#### Scenario: Local application files are inspected
- **WHEN** the application has synchronized quota data and completed an automatic-trigger workflow
- **THEN** its files under `%LOCALAPPDATA%` contain the required settings and redacted safety/audit metadata but contain no token, cookie, credential, or task/turn/prompt body

### Requirement: Fail closed on damaged state
The application MUST validate safety-critical settings, anti-repeat state, and audit state before automatic triggering. If required state is unreadable, malformed, internally inconsistent, or cannot be durably updated, the application MUST disable automatic triggering, MUST NOT create a Codex turn, and SHALL expose a safety-error status while allowing read-only monitoring to continue. It MUST NOT recover by treating damaged state as proof that no trigger has occurred.

#### Scenario: Durable anti-repeat state is corrupted
- **WHEN** the application cannot validate the stored anti-repeat state for an account and five-hour window
- **THEN** it creates no turn, disables automatic triggering, and displays a fail-closed safety error while read-only quota monitoring remains available

### Requirement: Safe manual activation check without forced consumption
The floating widget SHALL expose a `检查并触发` button that requests one guarded activation evaluation. The control MUST NOT force quota consumption: a Codex turn may be created only when the fresh five-hour bucket reports exact `usedPercent = 0` and the existing confirmation, durable deduplication, and final read-only preflight requirements all pass. The manual check MAY run while automatic triggering is paused and MUST NOT change that persisted preference. While a manual check is running, the button SHALL be disabled to prevent duplicate invocation, and the widget SHALL expose a concise in-progress or terminal status.

#### Scenario: Manual check is ineligible
- **WHEN** the user selects `检查并触发` while the five-hour window is already active, stale, unavailable, or otherwise ineligible
- **THEN** the widget reports that the current window does not need triggering and creates no Codex turn

#### Scenario: Manual check is eligible
- **WHEN** the user selects `检查并触发` while automatic triggering is paused and all guarded activation conditions pass
- **THEN** the application may create at most one isolated activation turn through the existing guarded workflow while leaving automatic triggering paused

#### Scenario: Refresh remains read-only
- **WHEN** the user invokes Refresh Now rather than `检查并触发`
- **THEN** the application performs only a read-only reconciliation and creates no Codex turn
