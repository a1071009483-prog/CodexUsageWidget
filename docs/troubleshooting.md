# Troubleshooting

This guide covers common symptoms and how to recover from them. All commands are
PowerShell and run without administrator rights unless noted.

## Before you start

1. Confirm you are on Windows 10 version 19041 (20H2) or later, or Windows 11.
2. Confirm you installed the [Codex CLI](https://github.com/openai/codex) and ran
   `codex login` with a ChatGPT-backed account.
3. Check the widget's local data directory:
   ```powershell
   $env:LOCALAPPDATA\CodexUsageWidget
   ```

## Symptom: widget shows "disconnected"

**Likely cause:** The Codex App Server child process could not start or the
handshake failed.

**What to do:**

1. Open a terminal and run:
   ```powershell
   codex app-server
   ```
2. If the command is not found, add the Codex CLI directory to your `PATH` and
   restart the widget.
3. If `codex app-server` starts but the widget stays disconnected, right-click
   the tray icon and choose **Reconnect**.
4. If reconnecting fails, exit the widget, run `scripts/uninstall.ps1`, and
   reinstall with `scripts/install.ps1`.

## Symptom: quota never updates

**Likely cause:** The App Server handshake is incomplete or the account is not
authenticated.

**What to do:**

1. Run:
   ```powershell
   codex login
   ```
2. Restart the widget.
3. Right-click the tray icon and choose **Refresh Now**.

## Symptom: five-hour window is not activated automatically

**Likely cause:** One of the safety preconditions is not met.

**What to do:**

1. Right-click the tray icon and confirm **Pause automatic triggering** is not
   checked.
2. Hover over the widget status area and look for a safety error. If safety
   state is invalid, automatic activation is disabled.
3. Check that the five-hour bucket shows 100% remaining and no active timer. The
   widget activates only when the App Server reports `usedPercent = 0` for a
   fresh bucket.
4. If you recently activated the window in another Codex surface, the widget
   treats it as externally satisfied and sends no generation.

## Symptom: widget does not start with Windows

**Likely cause:** The startup registry entry is missing.

**What to do:**

1. Run:
   ```powershell
   .\scripts\install.ps1
   ```
2. Open Task Manager → Startup and verify `CodexUsageWidget` is enabled.
3. If you previously disabled startup from the tray, right-click the tray icon
   and choose **Start with Windows**.

## Symptom: stale-data indicator stays on

**Likely cause:** No successful rate-limit read has occurred for more than two
minutes.

**What to do:**

1. Check your network connection.
2. Right-click the tray icon and choose **Reconnect**.
3. If the App Server process crashed, the supervisor restarts it automatically;
   wait up to 60 seconds.

## Symptom: two widgets appear

**Likely cause:** The single-instance signal was bypassed (for example, by
launching from a different user session).

**What to do:**

1. Exit both copies.
2. Start the widget normally from the Start menu or tray icon.

## Symptom: crash on startup

**Likely cause:** Local state may be corrupted.

**What to do:**

1. Check the redacted crash report in:
   ```powershell
   $env:LOCALAPPDATA\CodexUsageWidget\crashes\
   ```
2. If the crash mentions the SQLite database or the protected salt, you can
   remove local data:
   ```powershell
   .\scripts\uninstall.ps1 -RemoveLocalData
   ```
   This also removes audit history. Removing data while a five-hour suppression
   period is active can erase a live guard, so do this only when you are sure no
   recent activation is in progress.

## Symptom: upgrade failed

**Likely cause:** The application was running while files were being overwritten.

**What to do:**

1. Exit the widget from the tray icon.
2. Run the rollback script to restore the previous build:
   ```powershell
   .\scripts\rollback.ps1
   ```
3. If rollback also fails, run `scripts/uninstall.ps1` and then
   `scripts/install.ps1` with the desired build.

## Collecting information for a bug report

Include:

- Windows version.
- Codex CLI version (`codex --version`).
- Widget version (from `CodexUsageWidget.exe` file properties).
- The relevant redacted crash report or audit export.
- The exact steps that produced the issue.

Do not include raw email addresses, tokens, prompts, or response content.
