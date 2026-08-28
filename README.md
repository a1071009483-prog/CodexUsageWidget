# Codex Usage Widget

A resident Windows floating widget that shows your Codex five-hour and weekly
quota in real time and can automatically start a fully unused five-hour window
with exactly one guarded minimal turn.

> **Supported:** Windows 10 version 19041 (20H2) or later, or Windows 11 (x64).  
> **Authentication:** ChatGPT-backed Codex CLI login (`codex login`).
> API-key-only accounts are not supported.  
> **No .NET SDK or administrator rights required** when installing from a
> release installer.

## Install (recommended)

1. Install the [Codex CLI](https://github.com/openai/codex).
2. Run `codex login` once and sign in with your ChatGPT account.
3. Open the
   [GitHub Releases](https://github.com/a1071009483-prog/CodexUsageWidget/releases)
   page and download `CodexUsageWidget-Setup-<version>.exe`.
4. Double-click the installer. The widget starts automatically and lives in the
   system tray.

The installer is per-user, needs no administrator rights, and installs under
`%LOCALAPPDATA%\Programs\CodexUsageWidget\`. Your settings, audit history, and
activation safety state live separately under `%LOCALAPPDATA%\CodexUsageWidget\`
and are kept when you uninstall or upgrade.

Pre-release builds (`beta`/`rc`) may be unsigned; they are labeled as
pre-releases on the Releases page. Stable releases are Authenticode-signed.

See [docs/install.md](docs/install.md) for the portable ZIP, source-build, and
uninstall details, and [docs/usage.md](docs/usage.md) for everyday usage.

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
- Clear startup diagnostics when the Codex CLI is missing, not logged in,
  using an unsupported authentication type, or protocol-incompatible.
- System-tray controls for Show/Hide, Refresh Now, Pause/Resume automatic
  triggering, Start with Windows, Audit, Reconnect, and Exit.
- Redacted local audit and crash reports; no tokens, cookies, prompts, or
  responses are stored.

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
├── scripts/                              # Build, package, verify, installer tooling
├── installer/                            # Inno Setup per-user installer definition
├── docs/                                 # User and acceptance documentation
├── openspec/changes/add-codex-usage-widget/  # OpenSpec proposal, design, and specs
└── THIRD-PARTY-NOTICES.md
```

## Build from source

Building from source requires the exact .NET SDK version pinned in
`global.json` and is only needed for contributors. Open a PowerShell prompt in
the repository root:

```powershell
# Run all automated tests
.\scripts\build.ps1 -Configuration Release

# Build a versioned, self-contained release payload + portable ZIP
.\scripts\package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.0.0-dev -Clean

# Verify the packaged release output
.\scripts\verify-release.ps1 -Version 0.0.0-dev -RuntimeIdentifier win-x64
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

The project license will be published in the root `LICENSE` file before the
stable `1.0.0` release. See `THIRD-PARTY-NOTICES.md` for dependency licenses.
