# Using Codex Usage Widget

The Codex Usage Widget is a resident Windows floating window that shows your
Codex five-hour and weekly quota in real time. It can also automatically start a
fully unused five-hour window with exactly one guarded minimal turn.

## What the widget shows

The floating widget contains two cards:

- **5h** — the rolling five-hour usage window.
- **本周** — the rolling weekly usage window.

Each card shows:

- **Remaining percentage** — `100 - usedPercent`, clamped to `0–100`.
- **Progress bar** — the same remaining percentage visually.
- **Countdown** — time left until the bucket's `resetsAt`, calculated locally and
  updated once per second.
- **Status text** — one of `已同步`, `已过期`, `不可用`, or `100%·计时已启动`.
- **Last sync time** — local time of the last successful App Server read.

The widget also shows the current connection state (`connecting`, `monitoring`,
`authentication-required`, `disconnected`, `error`) and whether automatic
triggering is enabled or paused.

## Color thresholds

Both cards use the same thresholds for the percentage and progress bar color:

| Remaining | State | Color |
|-----------|-------|-------|
| > 30% | Normal | Green |
| 11% – 30% | Warning | Yellow |
| ≤ 10% | Critical | Red |

## The active-but-rounded 100% state

Codex App Server may round a freshly started five-hour window back to
`usedPercent = 0` (100% remaining). When the widget has verified that the
five-hour `resetsAt` has moved to a future time, the five-hour card shows the
exact text:

```
100%·计时已启动
```

This means the timer is running even though the rounded percentage still reads
100%.

## Countdown and freshness

- The countdown ticks locally every second using the last authoritative UTC
  `resetsAt`.
- When a bucket's reset instant passes, the widget marks the countdown as due
  and requests a fresh read-only reconciliation instead of guessing the new
  value.
- If more than two minutes pass without a successful App Server read, the data
  is marked **stale**. Stale values remain visible but are never used for
  automatic activation.

## Tray controls

Right-click the system-tray icon for:

- **Show / Hide** — toggle the floating widget without exiting the application.
- **Refresh Now** — force one immediate read-only quota reconciliation.
- **Pause / Resume Automatic Triggering** — pause or resume automatic five-hour
  activation without stopping quota monitoring.
- **Start with Windows** — enable or disable the per-user startup registry entry.
- **Audit** — open the local redacted audit view.
- **Reconnect** — restart the Codex App Server connection.
- **Exit** — close the resident application.

There is **no** force-consume or manual trigger command. Automatic activation is
the only widget-initiated path that can start a turn, and it runs only when all
safety preconditions pass.

## Automatic five-hour activation

When all of the following are true, the widget will automatically start an
unused five-hour window once per window:

1. Automatic triggering is enabled and not paused.
2. The current account uses ChatGPT-backed Codex authentication.
3. The five-hour bucket is fresh and reports exactly `usedPercent = 0`.
4. No durable lock or verified future reset proves the window is already active.

The activation workflow:

- Two consecutive fresh confirmations of the unused bucket.
- A durable write-ahead lock is flushed before any turn is sent.
- A final read-only preflight just before generation.
- One minimal fixed-response turn with no tools in a temporary thread.
- Verification through a changed future `resetsAt` within 60 seconds.
- Deletion of the temporary thread and redacted audit recording.

If the outcome is ambiguous, the widget retains the lock and never retries in the
same five-hour window. Safety always takes precedence over retrying.

## Startup behavior

On a fresh install:

1. The widget validates the local SQLite state database.
2. It starts the Codex App Server child process through
   `AppServerSupervisor`.
3. It completes the App Server initialization handshake.
4. It reads the current rate limits and subscribes to update notifications.
5. It opens the floating widget and minimizes to the system tray.

If the App Server exits unexpectedly, the supervisor restarts it with bounded
backoff and re-establishes the session. The widget remains visible during
restart.

## Privacy notes

The widget does not read Codex cookies, raw credentials, API keys, or browser
storage. Account emails are hashed before persistence. The activation prompt is a
compiled constant and is not stored in audit rows. Logs, crash reports, and audit
exports are kept locally and redact tokens, paths, and prompt/response content.

See [security.md](security.md) for the full security model.

## Troubleshooting

See [troubleshooting.md](troubleshooting.md) for common symptoms and recovery
steps.
