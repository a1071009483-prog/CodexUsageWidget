# Codex Usage Widget v1 Release Productization Design

## Goal

Make Codex Usage Widget usable by a normal Windows user through this path:

1. Install Codex CLI.
2. Run `codex login` once.
3. Open the GitHub Releases page.
4. Download `CodexUsageWidget-Setup-<version>.exe`.
5. Double-click the installer.
6. The widget starts and works without the user installing a .NET SDK, cloning the repository, running PowerShell scripts, or using administrator privileges.

A portable ZIP remains available for advanced users and diagnostics, but it is not the primary installation path.

## Non-Goals

The v1 productization work does not add new quota features, new activation behavior, a plugin system, telemetry, cloud sync, Store/MSIX distribution, ARM64 support, or a custom auto-update subsystem.

Existing domain behavior remains unchanged except where startup diagnostics must surface environment or compatibility failures more clearly.

## Supported Environment

- Windows 10 version 19041 (20H2) or later, or Windows 11.
- x64 only for v1.
- ChatGPT-backed Codex CLI authentication.
- API-key-only Codex accounts remain unsupported.
- End users do not need the .NET SDK or .NET Desktop Runtime because the distributed application is self-contained.
- Development and CI continue to use the exact SDK version pinned in `global.json`.

## Distribution Model

Each stable GitHub Release must contain exactly these primary artifacts:

- `CodexUsageWidget-Setup-<version>.exe`
- `CodexUsageWidget-<version>-win-x64.zip`
- `SHA256SUMS.txt`

The setup executable is the default download for normal users.

The portable ZIP contains the same signed application payload used by the installer and is intended for portable use, diagnostics, or manual recovery.

Release artifacts must be produced by GitHub Actions from the tagged commit. Stable release binaries must not be manually built and uploaded from a developer workstation.

## Versioning

Use semantic versions.

Pre-release sequence:

- `0.9.0-beta.1`: automated build plus portable ZIP.
- `0.9.0-beta.2`: installer plus first-run diagnostics.
- `0.9.0-rc.1`: signed candidate plus clean-machine acceptance.
- `1.0.0`: all v1 acceptance gates complete, including one real activation acceptance run.

Tags use a `v` prefix, for example `v1.0.0`.

The application assembly, file version, informational version, installer version, ZIP filename, and Release version must derive from the same release version input.

The executable must support `CodexUsageWidget.exe --version` and print the application version without starting the WPF shell.

## Build and Packaging

`scripts/package.ps1` becomes the canonical local and CI packaging entry point.

It must accept at least:

- `-Configuration`
- `-RuntimeIdentifier`
- `-OutputDirectory`
- `-Version`
- `-Clean`
- existing `-NoRestore`

For `win-x64`, packaging must publish a self-contained application. The implementation may use single-file extraction for native libraries if required by SQLite or other native dependencies; the release requirement is reliable double-click startup, not an artificial one-physical-file constraint.

Default release output layout:

```text
artifacts/
  publish/
    win-x64/
      Release/
        <application payload>
  release/
    CodexUsageWidget-<version>-win-x64.zip
    SHA256SUMS.txt
```

The packaging process must clean stale output when `-Clean` is specified.

Release archives must not contain source files, test assemblies, `obj`, intermediate build files, or development-only artifacts.

## Release Verification

Create `scripts/verify-release.ps1` as a separate release-quality gate.

It must fail when any required invariant is violated.

At minimum it verifies:

- `CodexUsageWidget.exe` exists.
- `CodexUsageWidget.exe --version` equals the requested release version.
- the portable ZIP exists and can be extracted.
- the extracted application starts its version command successfully.
- `THIRD-PARTY-NOTICES.md` is included.
- release output contains no source, test, `obj`, or unexpected debug artifacts.
- checksums are present and match the produced artifacts.
- when signing is enabled, Authenticode verification succeeds for the application and installer.

Release verification must be runnable locally and in CI.

## Installer

The primary installer is a per-user Windows installer and must not require elevation.

Recommended v1 implementation: Inno Setup.

Default application installation directory:

```text
%LOCALAPPDATA%\Programs\CodexUsageWidget\
```

Persistent application data remains under:

```text
%LOCALAPPDATA%\CodexUsageWidget\
```

This separation is required so uninstalling or replacing binaries does not implicitly destroy local quota safety state, audit history, settings, or activation locks.

The installer must:

- install the release payload.
- create a Start Menu shortcut.
- register an uninstall entry.
- optionally launch the application after installation, enabled by default.
- never require administrator privileges for normal installation.

The installer must not silently force Windows startup registration. The application's existing `Start with Windows` preference remains the source of truth for startup registration.

Uninstall must remove installed program files and shortcuts. Persistent application state is retained by default and may only be removed through an explicit user action documented separately.

## First-Run and Environment Diagnostics

Double-clicking the application must never fail silently for expected environment problems.

The startup path must distinguish at least these states:

### Codex CLI missing

The application remains stable and shows a user-facing diagnostic stating that Codex CLI is not installed or cannot be found on `PATH`.

Automatic activation must remain disabled.

### Codex CLI present but unauthenticated

The application shows a user-facing diagnostic instructing the user to run `codex login`.

Automatic activation must remain disabled.

### Unsupported authentication type

The application explains that a ChatGPT-backed Codex login is required and that API-key-only authentication is unsupported.

Automatic activation must remain disabled.

### App Server incompatible

The application shows a compatibility diagnostic including the Codex CLI version when available.

Read-only or automatic behavior must follow the existing safety model. If the required safety state or protocol behavior cannot be validated, automatic activation stays disabled.

### Normal environment

The widget starts without setup prompts and proceeds with its current quota monitoring behavior.

Technical exceptions and protocol details may appear in an expandable diagnostics or audit view, but the primary message must be phrased for normal users.

## Runtime Diagnostics

Diagnostics must expose, without sensitive account data:

- Codex Usage Widget version.
- Codex CLI version when detectable.
- Windows version.
- current connectivity/compatibility state.

No raw credentials, tokens, cookies, prompts, responses, or account identifiers may be added to diagnostics.

## Codex CLI Compatibility

The application must detect the installed Codex CLI version when possible.

Compatibility handling is capability-based first, version-based second:

1. Existing App Server handshake and capability preflight remain the authoritative protocol check.
2. The CLI version is captured for diagnostics and acceptance evidence.
3. No hard-coded maximum version is introduced for v1 unless a known incompatible version is identified.
4. A protocol capability failure disables unsafe behavior and gives the user a compatibility message instead of crashing.

The project documentation must record the Codex CLI version used for each stable release acceptance run.

## CI

Create `.github/workflows/ci.yml`.

It runs on Windows for:

- pushes to `main`.
- pull requests targeting `main`.

The workflow must:

1. check out the repository.
2. install the exact SDK from `global.json`.
3. restore dependencies.
4. run the full automated test suite.
5. create a Release-mode package with a CI test version.
6. run `scripts/verify-release.ps1` for the portable artifact path.

No merge to `main` should be considered releasable while CI is failing.

## Release Automation

Create `.github/workflows/release.yml`.

It runs for version tags matching `v*`.

The release workflow must:

1. check out the tagged commit.
2. derive and validate the semantic version from the tag.
3. install the exact SDK from `global.json`.
4. restore and run the automated test suite.
5. build the self-contained application.
6. sign the application when signing credentials are configured.
7. build the per-user installer from the exact signed application payload.
8. sign the installer when signing credentials are configured.
9. generate SHA-256 checksums.
10. run release verification.
11. create the GitHub Release.
12. upload the setup executable, portable ZIP, and checksum file.

A stable `v1.0.0` release must not be published without the signing and acceptance gates described below.

## Code Signing

Public stable v1 artifacts require Authenticode signing.

The signing certificate and password must be stored only in GitHub Actions secrets or an equivalent secure signing mechanism. They must never be committed to the repository.

Both of these must be signed:

- `CodexUsageWidget.exe`
- `CodexUsageWidget-Setup-<version>.exe`

Signatures must use a trusted timestamp service so signatures survive certificate expiry.

Pre-release beta artifacts may be unsigned while the release pipeline is being established, but the README and release notes must label them as pre-release builds.

## License

A root `LICENSE` file is required before `1.0.0`.

The repository owner must explicitly choose the license. The implementation must not silently select a license on the owner's behalf.

After selection:

- README license text must reference the actual license without `if present` wording.
- `THIRD-PARTY-NOTICES.md` remains included in release artifacts.

## README and User Documentation

The README must be reorganized around binary installation rather than source compilation.

The first user path must be:

1. install Codex CLI.
2. run `codex login`.
3. download the latest setup executable from GitHub Releases.
4. run the installer.

`Build from source` moves below the normal installation section.

Normal-user documentation must not instruct users to install a .NET SDK, run `package.ps1`, or run `install.ps1`.

Developer documentation may continue to document those commands.

`docs/install.md` must distinguish:

- normal binary installation.
- portable ZIP usage.
- source build for contributors.
- uninstall and retained local data behavior.

## Acceptance Gates

### Automated gate

Required before every release candidate:

- all Core tests pass.
- all Infrastructure tests pass.
- all App tests pass.
- acceptance tests that do not require real credentials pass or explicitly skip through their existing environment gates.
- package creation succeeds.
- release verification succeeds.

### Clean-machine gate

Run on a Windows x64 machine or VM with no repository checkout, Visual Studio, or .NET SDK.

Verify:

1. with no Codex CLI, application shows a clear missing-CLI diagnostic and does not crash.
2. with Codex CLI installed but not logged in, application instructs the user to run `codex login`.
3. after `codex login`, installing the setup executable and launching the widget works.
4. no .NET runtime or SDK installation is requested.
5. launching twice preserves single-instance behavior.
6. loss and recovery of network connectivity behaves safely.
7. Windows restart plus the application's `Start with Windows` option behaves correctly.
8. uninstall removes binaries without silently deleting persistent safety state.
9. reinstall works with retained state.
10. portable ZIP extraction and double-click startup works independently of the installer.

### Real Codex acceptance gate

Before `1.0.0`:

- authenticated read-only smoke test passes on the release candidate.
- one real fully-unused five-hour-window activation acceptance test completes successfully.
- the Codex CLI version, Windows version, application version, and pass/fail result are recorded in `docs/acceptance-matrix.md`.

If the required fully-unused quota condition cannot be obtained, the project may publish another release candidate but must not call the build `1.0.0` stable.

### External-user gate

Before `1.0.0`, at least two testers who do not know the repository internals must be able to complete the documented installation flow without developer assistance.

Any blocker that requires knowledge of source layout, build scripts, App Server internals, or database locations is treated as a productization defect.

## Required Repository Changes

Expected additions or modifications:

```text
.github/workflows/ci.yml
.github/workflows/release.yml
installer/CodexUsageWidget.iss
scripts/package.ps1
scripts/verify-release.ps1
src/CodexUsageWidget.App/CodexUsageWidget.App.csproj
src/CodexUsageWidget.App/<startup/version handling files as appropriate>
src/CodexUsageWidget.Infrastructure/AppServer/<CLI version diagnostics as appropriate>
tests/CodexUsageWidget.App.Tests/<version/startup diagnostics tests>
tests/CodexUsageWidget.Infrastructure.Tests/<CLI diagnostics tests>
README.md
docs/install.md
docs/troubleshooting.md
docs/acceptance-matrix.md
LICENSE
```

Existing project boundaries must be preserved. Release productization must not trigger unrelated domain refactoring.

## Security and Safety Constraints

- Existing fail-closed activation safety behavior remains unchanged.
- Packaging, diagnostics, logging, and installer work must not expose credentials.
- Persistent safety state must not be silently removed during update or uninstall.
- Release automation must not print signing secrets.
- Release artifacts must be reproducibly associated with a Git tag and GitHub Actions run.

## Definition of Done

The v1 productization effort is complete when a normal Windows user can follow only this instruction:

> Install Codex CLI, run `codex login`, download the latest Codex Usage Widget installer from GitHub Releases, and double-click it.

That path must work on a clean supported Windows x64 machine without source code, Git, Visual Studio, PowerShell build commands, a .NET SDK, administrator privileges, or knowledge of the repository internals.
