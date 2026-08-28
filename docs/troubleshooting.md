# Troubleshooting

This guide covers common symptoms and how to recover from them. All commands are
PowerShell and run without administrator rights unless noted.

## Before you start

1. Confirm you are on Windows 10 version 19041 (20H2) or later, or Windows 11 (x64).
2. Confirm you installed the [Codex CLI](https://github.com/openai/codex) and ran
   `codex login` with a ChatGPT-backed account.
3. Check the widget's local data directory:
   ```powershell
   $env:LOCALAPPDATA\CodexUsageWidget
   ```

The widget shows a diagnostic banner on startup when something in the
environment blocks normal operation, plus a small footer line with the widget,
Codex CLI, and Windows versions.

## Codex CLI not found

**Banner:** `未找到 Codex CLI。请先安装 Codex CLI，然后运行 codex login。`

The widget could not find the `codex` executable.

1. Install the [Codex CLI](https://github.com/openai/codex).
2. Open a **new** terminal and confirm `codex --version` works.
3. If you just installed the CLI, restart the widget so it picks up the updated
   `PATH`.
4. If the CLI is installed in a non-standard location, set the
   `CODEX_EXECUTABLE` environment variable to the full path of `codex.exe`.

Automatic activation stays disabled in this state.

## Codex login required

**Banner:** `Codex 尚未登录。请在终端运行 codex login，然后重新连接。`

The CLI is installed but not authenticated.

1. Run `codex login` in a terminal and complete the sign-in.
2. Right-click the widget tray icon and choose **Reconnect** (or restart the
   widget).

## API-key-only authentication unsupported

**Banner:** `需要使用 ChatGPT 账号登录 Codex；仅 API Key 的认证方式暂不支持。`

The widget requires a ChatGPT-backed Codex login. Authenticating with only an
API key is not supported. Run `codex login` and choose the ChatGPT sign-in
option.

## Codex App Server incompatible

**Banner:** `当前 Codex CLI 与 Codex Usage Widget 的 App Server 协议不兼容。`

The installed Codex CLI speaks an App Server protocol that the widget cannot
safely use.

1. Update the Codex CLI to the latest version.
2. Restart the widget.
3. If the message persists, file an issue with the diagnostic footer text
   (see below). Automatic activation stays disabled until the protocol check
   passes; the widget never guesses protocol behavior.

## Windows signature / SmartScreen checks

Stable releases are Authenticode-signed with a timestamped signature; Windows
shows the publisher when you run the installer. Pre-release (`beta`/`rc`)
builds may be unsigned, and SmartScreen may show an "unknown publisher"
warning for them — verify the SHA-256 against `SHA256SUMS.txt` on the release
page before running an unsigned build:

```powershell
Get-FileHash .\CodexUsageWidget-Setup-<version>.exe -Algorithm SHA256
```

If a *stable* release shows an invalid or missing signature, do not run it and
report the release as compromised.

## Symptom: widget shows "disconnected"

**Likely cause:** The Codex App Server child process could not start or the
handshake failed.

**What to do:**

1. Open a terminal and run:
   ```powershell
   codex app-server
   ```
2. If the command is not found, see "Codex CLI not found" above.
3. If `codex app-server` starts but the widget stays disconnected, right-click
   the tray icon and choose **Reconnect**.
4. If reconnecting fails, exit the widget and start it again from the Start
   Menu.

## Symptom: quota never updates

**Likely cause:** The App Server handshake is incomplete or the account is not
authenticated.

**What to do:**

1. Run `codex login`.
2. Restart the widget.
3. Right-click the tray icon and choose **Refresh Now**.

## Symptom: five-hour window is not activated automatically

**Likely cause:** One of the safety preconditions is not met.

**What to do:**

1. Right-click the tray icon and confirm **Pause automatic triggering** is not
   checked.
2. Look for a startup diagnostic banner. While any environment problem is
   displayed, automatic activation is disabled.
3. Check that the five-hour bucket shows 100% remaining and no active timer. The
   widget activates only when the App Server reports `usedPercent = 0` for a
   fresh bucket.
4. If you recently activated the window in another Codex surface, the widget
   treats it as externally satisfied and sends no generation.

## Symptom: widget does not start with Windows

**Likely cause:** The in-app **Start with Windows** preference is off. (The
installer never forces startup registration.)

**What to do:**

1. Right-click the tray icon and enable **Start with Windows**.
2. Open Task Manager → Startup and verify `CodexUsageWidget` is enabled.

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
   remove local data after uninstalling:
   ```powershell
   Remove-Item -Recurse "$env:LOCALAPPDATA\CodexUsageWidget"
   ```
   This also removes audit history and activation safety locks. Removing data
   while a five-hour suppression period is active can erase a live guard, so do
   this only when you are sure no recent activation is in progress.

## Collecting information for a bug report

The widget footer shows non-sensitive runtime diagnostics: widget version,
Codex CLI version, and Windows version. You can also collect them manually:

```powershell
CodexUsageWidget.exe --version   # widget version
codex --version                  # Codex CLI version
winver                           # Windows version
```

Include the diagnostic footer text (or the commands above), the relevant
redacted crash report or audit export, and the exact steps that produced the
issue.

Do not include raw email addresses, tokens, prompts, or response content.
