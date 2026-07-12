# Installing Codex Usage Widget

The Codex Usage Widget is a Windows-only .NET 8 WPF application. Normal use does
not require administrator rights.

## Prerequisites

- Windows 10 version 19041 (20H2) or later, or Windows 11.
- [.NET 8 Windows Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
  if you are not using the self-contained build.
- The [Codex CLI](https://github.com/openai/codex) installed and available on
  `PATH`. The widget launches `codex app-server` and relies on the CLI for
  authentication and App Server protocol support.

## Build from source

Open a PowerShell prompt in the repository root and run:

```powershell
.\scripts\package.ps1 -Configuration Release
```

This produces a self-contained single-file build in:

```
artifacts\publish\win-x64\Release\
```

## Install per user

From the repository root, run:

```powershell
.\scripts\install.ps1
```

The installer copies the published files to:

```
%LOCALAPPDATA%\CodexUsageWidget\
```

and registers the application to start with your Windows session. No
administrator rights are required.

To also create a Start-menu shortcut, run:

```powershell
.\scripts\install.ps1 -CreateStartMenuShortcut
```

## First run

After installation, sign in to the Codex CLI with a ChatGPT-backed account:

```powershell
codex login
```

Then start the widget from the Start menu or by running:

```powershell
%LOCALAPPDATA%\CodexUsageWidget\CodexUsageWidget.exe
```

The widget appears as a floating window and remains available in the system tray
when the window is hidden.

## Uninstall

Run:

```powershell
.\scripts\uninstall.ps1
```

By default this removes the startup entry and the installation directory but
keeps local state under `%LOCALAPPDATA%\CodexUsageWidget\` in case you want to
retain audit history. To remove local data as well, run:

```powershell
.\scripts\uninstall.ps1 -RemoveLocalData
```

## Upgrade and rollback

### Upgrade

Build the new version and run the upgrade script:

```powershell
.\scripts\package.ps1 -Configuration Release
.\scripts\upgrade.ps1
```

`upgrade.ps1` will:

1. Stop any running widget instance.
2. Create a timestamped backup of the current installation under
   `%LOCALAPPDATA%\CodexUsageWidget-backups\`.
3. Copy the new build into `%LOCALAPPDATA%\CodexUsageWidget\`.
4. Preserve the **Start with Windows** setting.
5. Remove older backups, keeping the most recent three.

On the next launch the widget validates and migrates the local SQLite database.
If migration cannot be performed safely, automatic triggering is disabled and a
safety error is shown.

### Rollback

If the new build fails, restore the most recent backup:

```powershell
.\scripts\rollback.ps1
```

To restore a specific backup, pass its directory:

```powershell
.\scripts\rollback.ps1 -BackupDirectory "$env:LOCALAPPDATA\CodexUsageWidget-backups\20260712-120000"
```

`rollback.ps1` stops the running instance, replaces the current installation
with the chosen backup, and re-registers startup if requested.

Local state under `%LOCALAPPDATA%\CodexUsageWidget\` is not removed by either
script. Removing local data while an active five-hour suppression guard is still
live could erase the at-most-once lock, so use `-RemoveLocalData` with
`uninstall.ps1` only when you are sure no recent activation is in progress.
