# Using Codex Usage Widget

The Codex Usage Widget helps you stay within your Codex account quota by showing
a compact, always-on-top window that updates as you work.

## What the widget shows

- **Quota progress bar**: the current percentage of the active quota period that
  has been used. Primary limits are shown by default; when a lower fallback model
  is available, its allowance is shown as a secondary bar.
- **Status text**: one of `normal`, `warning`, or `suppressed`. Suppression means
  the widget has temporarily stopped showing a reminder because you recently
  dismissed it.
- **Time remaining**: time left in the current quota window, if the App Server
  provides it.

## Quota semantics

The widget reads your rate limits from the Codex CLI App Server and compares the
used percentage to two thresholds that you can configure in settings:

- **Warning threshold** (default `80`): the widget turns yellow when usage passes
  this value.
- **Suppression threshold** (default `95`): the widget enters a five-hour
  cooldown when usage passes this value and you dismiss the alert, so the visual
  reminder does not dominate your screen during an urgent session.

The cooldown duration is fixed at five hours. Once the cooldown expires, the
widget returns to normal monitoring automatically.

## Model fallback

When the App Server reports a fallback model with its own quota, the widget
displays it as a secondary bar. Fallback usage is informational: it warns you
when the cheaper model is also running low, but it does not trigger suppression.

## Startup behavior

When the widget starts:

1. It validates the local SQLite state database.
2. It starts the Codex App Server child process through
   `AppServerSupervisor`.
3. It connects to the App Server and waits for a handshake.
4. It reads the current rate limits and begins listening for notifications.
5. It opens the floating window and, if configured, minimizes to the system
tray.

If the App Server process exits unexpectedly, the supervisor automatically
restarts it and re-establishes the session. The window remains visible while the
restart happens, but controls are disabled until the session is live again.

## Tray controls

Right-click the system-tray icon to:

- **Show / Hide**: toggle the floating window.
- **Refresh now**: force a fresh read of rate limits.
- **Settings**: open the settings editor.
- **Export audit log**: write the SQLite audit log to a JSON file of your
  choice.
- **Exit**: close the application.

Left-clicking the tray icon shows or hides the floating window.

## Audit log

The widget records every significant action to a local SQLite database under
`%LOCALAPPDATA%\CodexUsageWidget\`:

- App Server session starts, stops, and restarts.
- Rate-limit reads and notification events.
- Suppression decisions (trigger and dismiss).
- Errors that affect the widget state.

You can export the audit log from the tray menu. The exported JSON does not
contain raw tokens or credentials; sensitive values are redacted before export
and before any log entry is written.

## Known limitations

- **App Server handshake race**: if you open the widget before the Codex CLI is
  fully configured, the first handshake may fail. The supervisor retries with
  exponential backoff; ensure `codex login` has completed.
- **Single-instance binding**: only one widget instance can hold the named
  activation lock at a time. Starting a second instance shows the existing
  instance instead of launching a second copy.
- **Windows-only**: the WPF UI requires Windows 10 version 19041 or later.
  Core logic builds on Linux with `EnableWindowsTargeting=true` but the UI and
  tests cannot run there.
- **Process-level cleanup on Windows**: executable files written by the fake
  App Server E2E tests may remain locked briefly after the test process exits.
  Test cleanup is best-effort.

## Troubleshooting

| Symptom | Likely cause | What to do |
|---------|-------------|------------|
| Widget shows "disconnected" | `codex app-server` is not running or not responding | Run `codex app-server` from a terminal and check for errors. |
| Quota never updates | App Server handshake incomplete | Restart the widget; sign in with `codex login`. |
| Widget does not start with Windows | Startup registry entry missing | Re-run `scripts/install.ps1`. |
| Suppression never clears | Five-hour cooldown still active | Wait for the cooldown, or exit the widget and remove local data (this also removes audit history). |
| Crash on startup | Local database state may be corrupted | The widget writes a redacted crash report to `%LOCALAPPDATA%\CodexUsageWidget\crashes\`. Review it, then remove local data if needed. |
| Two widgets appear | Activation lock was bypassed | Exit both copies and restart from the Start menu or tray icon. |

## Privacy notes

The widget does not send usage data, crash reports, or credentials to any
remote service. Logs, crash reports, and audit exports are kept on your local
machine and redact bearer tokens, API keys, email addresses, absolute paths,
and prompts before they are written.
