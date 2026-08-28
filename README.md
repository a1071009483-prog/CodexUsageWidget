# Codex Usage Widget

A resident Windows .NET 8 WPF floating widget that shows your Codex five-hour and
weekly quota in real time and can automatically start a fully unused five-hour
window with exactly one guarded minimal turn.

> **Platform:** Windows 10 version 19041 (20H2) or later, or Windows 11.  
> **Runtime:** .NET 8 with Windows Desktop Runtime, or the self-contained build.  
> **Authentication:** ChatGPT-backed Codex CLI (`codex login`). API-key-only
> accounts are not supported.

## Features

- Always-on-top floating widget with live `5h` and `Weekly` quota progress,
  countdowns, and connection/freshness status.
- Updates from Codex App Server push notifications within one second.
- Read-only reconciliation every 60 seconds, with no model turns.
- Automatic activation when the five-hour bucket is fully unused (`usedPercent = 0`),
  using two confirmations, a durable write-ahead lock, and a final preflight.
- Rolling `now + 5h` reset placeholders are verified across the two read-only
  confirmations instead of being mistaken for an already-active timer.
- A safe **检查并触发** button that runs the same guarded eligibility flow
  without changing the automatic-trigger preference or offering a force override.
- Dynamic lightweight model selection with fallback to the current default model.
- At-most-once generation per account and five-hour window, surviving crashes,
  restarts, and App Server reconnections.
- System-tray controls for Show/Hide, Refresh Now, Pause/Resume automatic
  triggering, Start with Windows, Audit, Reconnect, and Exit.
- Redacted local audit and crash reports; no tokens, cookies, prompts, or
  responses are stored.

## Quick start

1. Install the [Codex CLI](https://github.com/openai/codex) and sign in:
   ```powershell
   codex login
   ```
2. Build and package the widget:
   ```powershell
   .\scripts\package.ps1 -Configuration Release
   ```
3. Install per user:
   ```powershell
   .\scripts\install.ps1
   ```
4. Start the widget:
   ```powershell
   $env:LOCALAPPDATA\CodexUsageWidget\CodexUsageWidget.exe
   ```

See [docs/install.md](docs/install.md) for detailed installation and upgrade
instructions, and [docs/usage.md](docs/usage.md) for everyday usage.

## Repository layout

```
├── src/
│   ├── CodexUsageWidget.Core/            # Domain logic: quota, monitoring, activation
│   ├── CodexUsageWidget.Infrastructure/  # App Server, SQLite, Windows services
│   └── CodexUsageWidget.App/             # WPF application shell
├── tests/
│   ├── CodexUsageWidget.Core.Tests/
│   ├── CodexUsageWidget.Infrastructure.Tests/
│   ├── CodexUsageWidget.App.Tests/
│   ├── CodexUsageWidget.AcceptanceTests/ # Windows + real Codex CLI only
│   └── FakeCodexAppServer/               # Scriptable test double
├── scripts/                              # Build, package, install, upgrade, rollback
├── docs/                                 # User and acceptance documentation
├── openspec/changes/add-codex-usage-widget/  # OpenSpec proposal, design, and specs
└── THIRD-PARTY-NOTICES.md
```

## Build and test

Open a PowerShell prompt in the repository root:

```powershell
# Run all automated tests
.\scripts\build.ps1 -Configuration Release

# Build a self-contained single-file release
.\scripts\package.ps1 -Configuration Release
```

The acceptance tests require a Windows environment with the Codex CLI
authenticated. See [docs/acceptance-matrix.md](docs/acceptance-matrix.md) for the
opt-in environment variables.

## Safety and privacy

- The widget never reads Codex cookies, raw credentials, or API keys.
- Account emails are hashed with a per-installation DPAPI-protected salt before
  storage.
- The activation prompt is a compiled constant and is not persisted.
- Local data lives under `%LOCALAPPDATA%\CodexUsageWidget\`.
- If safety state cannot be validated, automatic activation is disabled and the
  widget continues read-only monitoring.

See [docs/security.md](docs/security.md) for the full security model.

## Troubleshooting

See [docs/troubleshooting.md](docs/troubleshooting.md) for common symptoms and
recovery steps.

## Acceptance matrix

The full OpenSpec acceptance matrix, including manual Windows verification
procedures, is in [docs/acceptance-matrix.md](docs/acceptance-matrix.md).

## License and third-party notices

See `LICENSE` (if present) and `THIRD-PARTY-NOTICES.md` for dependency licenses.
