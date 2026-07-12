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

To upgrade, run `scripts/package.ps1` for the new version and then
`scripts/install.ps1`. The installer overwrites the existing installation files
and preserves your settings. The application validates the local SQLite database
on startup and disables automatic triggering if the database cannot be migrated
safely.

To roll back, uninstall the new version and reinstall the previous build. Local
state can remain while any active five-hour suppression period has not yet
expired; removing local data before the suppression period ends could remove a
live guard and must be done with care.
