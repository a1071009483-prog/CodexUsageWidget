# Installing Codex Usage Widget

The Codex Usage Widget is a Windows-only application. Normal use does not
require administrator rights, a .NET SDK, or any build tools.

## Normal installation

This is the path for everyone who just wants to use the widget:

1. Install the [Codex CLI](https://github.com/openai/codex) so that `codex` is
   available on your `PATH`.
2. Run `codex login` once and sign in with your ChatGPT account.
   (API-key-only accounts are not supported.)
3. Open the
   [GitHub Releases](https://github.com/a1071009483-prog/CodexUsageWidget/releases)
   page and download the latest `CodexUsageWidget-Setup-<version>.exe`.
4. Double-click the installer.

The installer:

- installs the application into
  `%LOCALAPPDATA%\Programs\CodexUsageWidget\`,
- creates a Start Menu shortcut and an uninstall entry,
- can launch the widget immediately after installation (enabled by default),
- never asks for administrator rights.

Supported systems: Windows 10 version 19041 (20H2) or later, or Windows 11,
x64. The release payload is self-contained; Windows never asks you to install a
.NET runtime.

## Portable ZIP

Advanced users can use `CodexUsageWidget-<version>-win-x64.zip` from the same
release instead:

1. Extract the ZIP to any folder you own (for example
   `%LOCALAPPDATA%\Programs\CodexUsageWidget-portable\`).
2. Double-click `CodexUsageWidget.exe`.

The portable build is the same application payload as the installer. It shares
the same per-user data directory, so quota safety state and settings are
consistent whichever variant you start.

`SHA256SUMS.txt` on the release page lets you verify downloads:

```powershell
Get-FileHash .\CodexUsageWidget-Setup-<version>.exe -Algorithm SHA256
```

## Build from source

Only contributors need this path. Normal users should use the installer above.

Prerequisites:

- Windows 10 19041+ / Windows 11, x64.
- The exact .NET SDK version pinned in `global.json`.
- (For the installer) Inno Setup 6.

```powershell
# Run all automated tests
.\scripts\build.ps1 -Configuration Release

# Produce the versioned self-contained payload and portable ZIP
.\scripts\package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.0.0-dev -Clean

# Build the per-user installer from the packaged payload
.\scripts\build-installer.ps1 -Version 0.0.0-dev -SourceDirectory .\artifacts\publish\win-x64\Release

# Verify the release output (version, archive contents, checksums, signatures)
.\scripts\verify-release.ps1 -Version 0.0.0-dev -RuntimeIdentifier win-x64 -RequireInstaller
```

The legacy developer scripts `install.ps1`, `upgrade.ps1`, `rollback.ps1`, and
`uninstall.ps1` remain available for source-build workflows. Note that
`install.ps1` copies the payload into `%LOCALAPPDATA%\CodexUsageWidget\`,
while the Inno installer uses `%LOCALAPPDATA%\Programs\CodexUsageWidget\`;
pick one installation variant at a time so the single-instance behavior stays
predictable.

## Uninstall and retained local state

Uninstall from **Settings → Apps** or the Start Menu entry, or run
`scripts\uninstall.ps1` for source-build installations.

Uninstalling removes the program files, shortcuts, and the uninstall entry. It
deliberately **keeps** `%LOCALAPPDATA%\CodexUsageWidget\`, which contains your
settings, redacted audit history, and the activation safety locks that
guarantee at-most-once activation per five-hour window. Reinstalling later
reuses this state safely.

Removing that directory while a five-hour suppression guard is live can erase
the at-most-once lock. If you fully understand that consequence, source-build
users can run `scripts\uninstall.ps1 -RemoveLocalData`; installer users can
delete `%LOCALAPPDATA%\CodexUsageWidget\` manually after uninstalling.
