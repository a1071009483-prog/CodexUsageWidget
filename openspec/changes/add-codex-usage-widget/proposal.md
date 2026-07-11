## Why

Codex users currently lack an always-visible Windows view of their five-hour and weekly allowance, and a fully unused five-hour window does not begin counting down until the first real use. A resident desktop widget should expose fresh quota state and, only when the five-hour allowance is completely unused, start that window with one guarded minimal turn without continuing to consume quota.

## What Changes

- Add a Windows `.NET 8` WPF application with an always-on-top floating widget, system tray controls, and optional Windows sign-in startup.
- Read Codex account rate-limit buckets through the official Codex App Server protocol and show the five-hour and weekly remaining percentages, reset times, freshness, and connection state.
- React to rate-limit update notifications and use a 60-second read-only reconciliation poll so server-side changes appear within a bounded interval while local countdowns update every second.
- When the five-hour bucket is at 100% remaining, acquire a durable per-account, per-window lock before creating exactly one minimal temporary Codex turn.
- Select the newest recognized available lightweight model, falling back to the current default model when no lightweight candidate is available.
- Treat an updated five-hour `resetsAt` value as activation success even when the displayed percentage remains rounded to 100%; never retry after a turn has begun during the same guarded window.
- Delete temporary trigger tasks after successful activation and retain only non-sensitive local audit metadata.
- Fail closed on stale quota data, missing authentication, damaged safety state, or ambiguous trigger outcomes.

## Capabilities

### New Capabilities

- `codex-usage-monitoring`: Discover, refresh, normalize, and expose five-hour and weekly Codex rate-limit state with bounded freshness semantics.
- `codex-window-activation`: Start a completely unused five-hour window through a single guarded minimal turn, including persistent deduplication, dynamic model selection, verification, and cleanup.
- `windows-usage-widget`: Provide the Windows floating widget, tray menu, startup behavior, notifications, settings, and local audit experience.

### Modified Capabilities

None.

## Impact

- Introduces a new Windows-only `.NET 8` WPF solution and automated test projects.
- Depends on an installed Codex CLI that supports `codex app-server`, authenticated with a Codex-backed ChatGPT account rather than API-key-only usage.
- Uses the official App Server JSON-RPC methods for account, model, thread, turn, and rate-limit operations; it does not scrape the usage dashboard or read browser cookies or raw authentication files.
- Stores settings, crash-safe trigger locks, cleanup work, and redacted audit records under the current user's local application-data directory.
- Registers an opt-out Windows startup entry and enforces a single running application instance.
