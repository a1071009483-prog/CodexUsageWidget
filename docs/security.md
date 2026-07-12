# Security and Privacy

Codex Usage Widget is designed to keep your authentication material and workspace
content out of the application. This document explains what the widget stores,
what it never stores, and how it fails closed when safety state cannot be
validated.

## What the widget does NOT access

- **No tokens or cookies.** The widget never reads Codex session cookies,
  browser storage, `~/.codex/config`, or any OpenAI API key.
- **No raw credentials.** ChatGPT account email addresses are hashed before they
  are persisted. The raw email is used only as input to the namespace hasher and
  is never written to logs, audit rows, crash reports, or the SQLite database.
- **No prompt or response bodies.** The activation prompt is a compiled constant.
  The model boundary passes it to the App Server but does not record it. Task,
  turn, and response content never enter local storage.
- **No unredacted workspace content.** The activation turn runs in an empty
  read-only working directory with no client-supplied dynamic tools.
- **No dashboard scraping.** All quota data comes from the documented Codex App
  Server JSON-RPC protocol.

## What the widget stores locally

Everything is kept under the current user's local application-data directory,
typically:

```
%LOCALAPPDATA%\CodexUsageWidget\
```

The directory contains:

| File / folder | Purpose | Sensitivity |
|---|---|---|
| `state.db` | SQLite database with settings, account namespace hashes, activation locks, pending cleanup, notification dedupe keys, and redacted audit rows. | Low: no raw email, tokens, prompts, or responses. |
| `salt.bin` | A per-installation random salt protected with Windows DPAPI. Used to hash account identities. | Medium: protected to the current user; loss would break existing namespace rows. |
| `settings.json` | User preferences: Start with Windows and automatic triggering. | Low. |
| `crashes\` | Redacted crash reports. | Low: stack traces and error categories only. |

## Redaction rules

Before any value is written to logs, audit, crash reports, or settings, the
application runs it through `SensitiveDataRedactor`. The redactor removes or
masks:

- Bearer tokens, API keys, and authorization headers.
- Cookies and session identifiers.
- Absolute file paths.
- Prompt and response bodies.
- Raw email addresses.
- JWT-like strings and long random tokens.

## Fail-closed behavior

If any of the following conditions occur, automatic activation is disabled while
read-only quota monitoring continues:

- The SQLite database cannot be opened or migrated.
- The protected salt cannot be unprotected.
- Activation-lock rows are inconsistent or cannot be written durably.
- The safety-state validator detects corruption or a migration mismatch.

The widget never recovers from damaged state by assuming that no activation has
occurred. A safety error is shown in the widget status and tray menu until the
state is repaired or removed.

## Auditing

Every activation attempt writes a redacted audit row containing:

- Timestamps in UTC.
- The account namespace hash.
- The selected model identifier.
- Pre- and post-activation quota percentages and reset instants.
- Whether the generation boundary was crossed.
- The terminal outcome category and a redacted error category when applicable.

Audit exports contain the same fields and are safe to share for troubleshooting.

## Single-instance protection

A named mutex and a local named-pipe signal enforce one running instance per
Windows user. A second launch brings the existing widget forward instead of
starting a second App Server connection or a second tray icon.

## Updates and removal

- `scripts/install.ps1` and `scripts/uninstall.ps1` touch only the current
  user's `%LOCALAPPDATA%` and `HKCU` registry hive.
- `scripts/upgrade.ps1` creates a timestamped backup of the previous installation
  before overwriting it.
- `scripts/rollback.ps1` restores the most recent backup.
- Removing local data before an active five-hour suppression period has ended can
  erase a live guard and is therefore optional and explicitly gated by a switch.

## Reporting security issues

If you discover behavior that contradicts this document, report it with the
relevant audit export and a description of the steps that produced it.
