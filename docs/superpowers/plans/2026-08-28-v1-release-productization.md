# Codex Usage Widget v1 Release Productization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn Codex Usage Widget into a normal Windows binary product where a user installs Codex CLI, runs `codex login`, downloads a GitHub Release installer, and double-clicks it without needing source code, Git, a .NET SDK, build scripts, administrator privileges, or repository knowledge.

**Architecture:** Keep the existing Core/Infrastructure/App boundaries and domain behavior intact. Add a thin productization layer around the current application: deterministic version entry point, CLI/environment diagnostics, reproducible packaging and verification scripts, per-user Inno Setup installer, Windows CI/release workflows, signing gates, and clean-machine acceptance evidence. Failures in Codex discovery/auth/protocol compatibility remain fail-closed for activation and are surfaced as user-readable startup diagnostics instead of being collapsed into a generic authentication state.

**Tech Stack:** .NET 8 WPF, PowerShell 7/Windows PowerShell-compatible release scripts, GitHub Actions Windows runners, Inno Setup 6, Authenticode/SignTool, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-28-v1-release-productization-design.md`

## Global Constraints

- Windows 10 version 19041 (20H2) or later, or Windows 11.
- x64 only for v1.
- ChatGPT-backed Codex CLI authentication only; API-key-only accounts remain unsupported.
- End users must not need the .NET SDK or .NET Desktop Runtime; release payloads are self-contained.
- Development and CI continue to use the exact SDK version pinned in `global.json`.
- Stable release artifacts are produced by GitHub Actions from the tagged commit, never manually uploaded from a developer workstation.
- Stable release primary artifacts are exactly `CodexUsageWidget-Setup-<version>.exe`, `CodexUsageWidget-<version>-win-x64.zip`, and `SHA256SUMS.txt`.
- The installer is per-user, does not require elevation, installs under `%LOCALAPPDATA%\Programs\CodexUsageWidget\`, and does not delete `%LOCALAPPDATA%\CodexUsageWidget\` state on uninstall.
- Existing fail-closed activation safety behavior must not be weakened.
- No credentials, tokens, cookies, prompts, responses, or raw account identifiers may be added to diagnostics or logs.
- Public stable v1 artifacts require Authenticode signing for both the application executable and installer.
- A root `LICENSE` is required before `1.0.0`, but the repository owner must explicitly choose the license.
- `1.0.0` is blocked until the clean-machine gate, real authenticated read-only smoke test, one real fully-unused five-hour activation acceptance run, and two external-user installation tests pass.

---

## File Map

### New files

- `src/CodexUsageWidget.App/Program.cs` — explicit process entry point; handles `--version` before constructing WPF.
- `src/CodexUsageWidget.App/Services/ApplicationVersion.cs` — one source for the running application semantic version.
- `src/CodexUsageWidget.App/Services/StartupEnvironmentStatus.cs` — user-facing startup environment state and diagnostics payload.
- `src/CodexUsageWidget.Infrastructure/AppServer/CodexCliVersionProbe.cs` — probes `codex --version` through the existing process abstraction.
- `tests/CodexUsageWidget.App.Tests/Services/ApplicationVersionTests.cs` — version normalization/entry-point-support tests.
- `tests/CodexUsageWidget.App.Tests/ViewModels/StartupDiagnosticsTests.cs` — startup environment message and safety-state tests.
- `tests/CodexUsageWidget.Infrastructure.Tests/AppServer/CodexCliVersionProbeTests.cs` — CLI version parsing/failure tests.
- `scripts/verify-release.ps1` — release artifact invariant gate.
- `scripts/build-installer.ps1` — deterministic Inno Setup invocation.
- `installer/CodexUsageWidget.iss` — per-user installer definition.
- `.github/workflows/ci.yml` — Windows PR/main test and portable packaging verification.
- `.github/workflows/release.yml` — tagged release, signing, installer, verification, and GitHub Release publication.

### Modified files

- `src/CodexUsageWidget.App/CodexUsageWidget.App.csproj` — explicit startup object and version metadata behavior.
- `src/CodexUsageWidget.App/App.xaml.cs` — consume structured startup environment result; remove hard-coded client version.
- `src/CodexUsageWidget.App/ViewModels/MainViewModel.cs` — expose user-readable environment diagnostic and disable automation when blocked.
- `src/CodexUsageWidget.App/MainWindow.xaml` — render startup diagnostic text without expanding the widget into a setup wizard.
- `scripts/package.ps1` — versioned, clean, reproducible portable release packaging.
- `README.md` — binary-first installation path.
- `docs/install.md` — installer/portable/source-build split and retained-state uninstall behavior.
- `docs/troubleshooting.md` — missing CLI, login, incompatible App Server, and signature troubleshooting.
- `docs/acceptance-matrix.md` — v1 release acceptance evidence.
- `THIRD-PARTY-NOTICES.md` — only if installer/tool notices require an addition.
- `LICENSE` — created only after the owner explicitly selects a license.

---

### Task 1: Establish one application version contract and `--version`

**Files:**
- Create: `src/CodexUsageWidget.App/Program.cs`
- Create: `src/CodexUsageWidget.App/Services/ApplicationVersion.cs`
- Modify: `src/CodexUsageWidget.App/CodexUsageWidget.App.csproj`
- Modify: `src/CodexUsageWidget.App/App.xaml.cs`
- Create: `tests/CodexUsageWidget.App.Tests/Services/ApplicationVersionTests.cs`

**Interfaces:**
- Produces: `ApplicationVersion.Current : string` returning the semantic version embedded by MSBuild.
- Produces: `Program.Main(string[] args) : int`; `--version` writes only `ApplicationVersion.Current` and exits `0`; normal invocation starts WPF.
- Consumed later by: packaging verification, installer versioning, diagnostics, acceptance evidence.

- [ ] **Step 1: Write failing version tests**

Create `ApplicationVersionTests.cs` with tests for normalization and non-empty runtime version:

```csharp
using CodexUsageWidget.App.Services;

namespace CodexUsageWidget.App.Tests.Services;

public sealed class ApplicationVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.2")]
    [InlineData("1.0.0+abcdef", "1.0.0")]
    public void Normalize_RemovesBuildMetadata(string input, string expected)
    {
        Assert.Equal(expected, ApplicationVersion.Normalize(input));
    }

    [Fact]
    public void Current_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(ApplicationVersion.Current));
    }
}
```

- [ ] **Step 2: Run the focused test and confirm failure**

Run:

```powershell
dotnet test tests/CodexUsageWidget.App.Tests/CodexUsageWidget.App.Tests.csproj -c Release --filter ApplicationVersionTests
```

Expected: compile failure because `ApplicationVersion` does not exist.

- [ ] **Step 3: Implement the version service**

Create `ApplicationVersion.cs` using the entry assembly informational version as the source of truth:

```csharp
using System.Reflection;

namespace CodexUsageWidget.App.Services;

public static class ApplicationVersion
{
    public static string Current => Normalize(
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0");

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        int metadata = value.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? value : value[..metadata];
    }
}
```

- [ ] **Step 4: Add the explicit process entry point**

Create `Program.cs` and make it the startup object. Keep normal launches console-free; `--version` must work under redirected stdout, which is what release verification uses.

```csharp
using CodexUsageWidget.App.Services;

namespace CodexUsageWidget.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.Ordinal))
        {
            using Stream stdout = Console.OpenStandardOutput();
            using StreamWriter writer = new(stdout) { AutoFlush = true };
            writer.WriteLine(ApplicationVersion.Current);
            return 0;
        }

        App app = new();
        app.InitializeComponent();
        return app.Run();
    }
}
```

Modify `CodexUsageWidget.App.csproj`:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <StartupObject>CodexUsageWidget.App.Program</StartupObject>
  <AssemblyName>CodexUsageWidget</AssemblyName>
  <UseWPF>true</UseWPF>
  <UseWindowsForms>true</UseWindowsForms>
</PropertyGroup>
```

- [ ] **Step 5: Remove the hard-coded App Server client version**

In `App.xaml.cs`, replace:

```csharp
ClientInformation clientInformation = new(
    "codex-usage-widget",
    "1.0.0",
    "Codex Usage Widget");
```

with:

```csharp
ClientInformation clientInformation = new(
    "codex-usage-widget",
    ApplicationVersion.Current,
    "Codex Usage Widget");
```

and add `using CodexUsageWidget.App.Services;` if not already present.

- [ ] **Step 6: Run focused and full app tests**

Run:

```powershell
dotnet test tests/CodexUsageWidget.App.Tests/CodexUsageWidget.App.Tests.csproj -c Release --filter ApplicationVersionTests
dotnet test tests/CodexUsageWidget.App.Tests/CodexUsageWidget.App.Tests.csproj -c Release
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/CodexUsageWidget.App tests/CodexUsageWidget.App.Tests/Services/ApplicationVersionTests.cs
git commit -m "feat: add release version command"
```

---

### Task 2: Add Codex CLI version probing through the existing process boundary

**Files:**
- Create: `src/CodexUsageWidget.Infrastructure/AppServer/CodexCliVersionProbe.cs`
- Create: `tests/CodexUsageWidget.Infrastructure.Tests/AppServer/CodexCliVersionProbeTests.cs`

**Interfaces:**
- Consumes: existing `IProcessHost`, `IHostedProcess`, and `ProcessStartRequest` abstractions.
- Produces: `CodexCliVersionResult(bool Succeeded, string? Version, string Diagnostic)`.
- Produces: `CodexCliVersionProbe.GetVersionAsync(string command, CancellationToken cancellationToken) : Task<CodexCliVersionResult>`.
- Consumed later by: startup diagnostics and acceptance evidence.

- [ ] **Step 1: Write failing CLI version tests**

Cover successful standard output, stderr fallback, non-zero exit, malformed output, and cancellation. The successful parse contract is that input such as `codex-cli 0.148.0-alpha.9` produces `0.148.0-alpha.9`.

Representative test:

```csharp
[Fact]
public async Task GetVersionAsync_ParsesCodexCliVersion()
{
    var host = new TestProcessHost(
        stdout: "codex-cli 0.148.0-alpha.9\n",
        stderr: "",
        exitCode: 0);
    var probe = new CodexCliVersionProbe(host);

    CodexCliVersionResult result = await probe.GetVersionAsync("codex.exe", CancellationToken.None);

    Assert.True(result.Succeeded);
    Assert.Equal("0.148.0-alpha.9", result.Version);
    Assert.Equal(["--version"], host.LastRequest!.Arguments);
}
```

Implement the test helper in the same test file using the repository's `IProcessHost`/`IHostedProcess` interfaces so no real process is started.

- [ ] **Step 2: Run the focused test and confirm failure**

```powershell
dotnet test tests/CodexUsageWidget.Infrastructure.Tests/CodexUsageWidget.Infrastructure.Tests.csproj -c Release --filter CodexCliVersionProbeTests
```

Expected: compile failure because the probe/result do not exist.

- [ ] **Step 3: Implement the probe**

Use the existing `IProcessHost` rather than `System.Diagnostics.Process` directly:

```csharp
public sealed record CodexCliVersionResult(
    bool Succeeded,
    string? Version,
    string Diagnostic);

public sealed class CodexCliVersionProbe
{
    private readonly IProcessHost _processHost;

    public CodexCliVersionProbe(IProcessHost processHost)
    {
        _processHost = processHost ?? throw new ArgumentNullException(nameof(processHost));
    }

    public async Task<CodexCliVersionResult> GetVersionAsync(
        string command,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        await using IHostedProcess process = await _processHost.StartAsync(
            new ProcessStartRequest(command, ["--version"], null),
            cancellationToken).ConfigureAwait(false);

        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        ProcessExitResult exit = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (exit.ExitCode != 0)
        {
            return new(false, null, "Codex CLI version command failed.");
        }

        string text = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        string? version = ParseVersion(text);
        return version is null
            ? new(false, null, "Codex CLI version output was not recognized.")
            : new(true, version, "Codex CLI version detected.");
    }

    internal static string? ParseVersion(string value)
    {
        string[] parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.FirstOrDefault(part => char.IsDigit(part.FirstOrDefault()));
    }
}
```

If analyzers reject the simple parser, replace it with a compiled regex matching `\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?`; keep the public result contract unchanged.

- [ ] **Step 4: Run focused and full infrastructure tests**

```powershell
dotnet test tests/CodexUsageWidget.Infrastructure.Tests/CodexUsageWidget.Infrastructure.Tests.csproj -c Release --filter CodexCliVersionProbeTests
dotnet test tests/CodexUsageWidget.Infrastructure.Tests/CodexUsageWidget.Infrastructure.Tests.csproj -c Release
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CodexUsageWidget.Infrastructure/AppServer/CodexCliVersionProbe.cs tests/CodexUsageWidget.Infrastructure.Tests/AppServer/CodexCliVersionProbeTests.cs
git commit -m "feat: detect Codex CLI version"
```

---

### Task 3: Replace generic startup failure with structured environment diagnostics

**Files:**
- Create: `src/CodexUsageWidget.App/Services/StartupEnvironmentStatus.cs`
- Modify: `src/CodexUsageWidget.App/App.xaml.cs`
- Modify: `src/CodexUsageWidget.App/ViewModels/MainViewModel.cs`
- Modify: `src/CodexUsageWidget.App/MainWindow.xaml`
- Create: `tests/CodexUsageWidget.App.Tests/ViewModels/StartupDiagnosticsTests.cs`
- Modify: `tests/CodexUsageWidget.App.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Produces: `StartupEnvironmentKind` with `Ready`, `CodexCliMissing`, `AuthenticationRequired`, `UnsupportedAuthentication`, `AppServerIncompatible`, `StartupError`.
- Produces: `StartupEnvironmentStatus` containing kind, user message, widget version, optional CLI version, Windows version, and `CanActivate`.
- Changes `CreateLiveServicesAsync` to return live services plus `StartupEnvironmentStatus` instead of a single `bool IsAuthenticated`.
- Produces: `MainViewModel.ApplyStartupEnvironment(StartupEnvironmentStatus status)`.

- [ ] **Step 1: Write failing view-model startup diagnostic tests**

Add tests asserting each blocked state disables automation and exposes the exact normal-user message. Use these strings as the v1 UI contract:

```csharp
[Theory]
[InlineData(StartupEnvironmentKind.CodexCliMissing, "未找到 Codex CLI。请先安装 Codex CLI，然后运行 codex login。")]
[InlineData(StartupEnvironmentKind.AuthenticationRequired, "Codex 尚未登录。请在终端运行 codex login，然后重新连接。")]
[InlineData(StartupEnvironmentKind.UnsupportedAuthentication, "需要使用 ChatGPT 账号登录 Codex；仅 API Key 的认证方式暂不支持。")]
[InlineData(StartupEnvironmentKind.AppServerIncompatible, "当前 Codex CLI 与 Codex Usage Widget 的 App Server 协议不兼容。")]
public void ApplyStartupEnvironment_BlockedStateDisablesAutomation(
    StartupEnvironmentKind kind,
    string expectedMessage)
{
    MainViewModel vm = CreateViewModel();
    vm.IsAutomationEnabled = true;

    vm.ApplyStartupEnvironment(new StartupEnvironmentStatus(
        kind,
        expectedMessage,
        "1.0.0",
        "0.148.0-alpha.9",
        "Windows 11",
        CanActivate: false));

    Assert.False(vm.IsAutomationEnabled);
    Assert.Equal(expectedMessage, vm.EnvironmentDiagnosticText);
    Assert.True(vm.HasEnvironmentDiagnostic);
}
```

Also test `Ready` clears the diagnostic and does not force automation off.

- [ ] **Step 2: Run focused tests and confirm failure**

```powershell
dotnet test tests/CodexUsageWidget.App.Tests/CodexUsageWidget.App.Tests.csproj -c Release --filter "StartupDiagnosticsTests"
```

Expected: compile failure for missing status types/properties.

- [ ] **Step 3: Implement the startup status model**

Create `StartupEnvironmentStatus.cs`:

```csharp
namespace CodexUsageWidget.App.Services;

public enum StartupEnvironmentKind
{
    Ready,
    CodexCliMissing,
    AuthenticationRequired,
    UnsupportedAuthentication,
    AppServerIncompatible,
    StartupError,
}

public sealed record StartupEnvironmentStatus(
    StartupEnvironmentKind Kind,
    string UserMessage,
    string WidgetVersion,
    string? CodexCliVersion,
    string WindowsVersion,
    bool CanActivate)
{
    public bool IsReady => Kind == StartupEnvironmentKind.Ready;
}
```

- [ ] **Step 4: Refactor `CreateLiveServicesAsync` to preserve failure reason**

Replace the return tuple:

```csharp
(IQuotaSource QuotaSource, IActivationCoordinator ActivationCoordinator, AccountIdentity Identity, bool IsAuthenticated)
```

with:

```csharp
(IQuotaSource QuotaSource,
 IActivationCoordinator ActivationCoordinator,
 AccountIdentity Identity,
 StartupEnvironmentStatus Environment)
```

Before App Server startup, probe CLI version with:

```csharp
CodexCliVersionResult cliVersion = await new CodexCliVersionProbe(new SystemProcessHost())
    .GetVersionAsync(resolution.Command!, cancellationToken)
    .ConfigureAwait(true);
```

Use `Environment.OSVersion.VersionString` for the Windows version text.

Map failure reasons explicitly:

```text
Codex executable not found            -> CodexCliMissing
identity/auth says login required     -> AuthenticationRequired
identity/auth says API-key-only       -> UnsupportedAuthentication
IncompatibleDetected event            -> AppServerIncompatible
unexpected startup exception          -> StartupError
successful identity + capability      -> Ready
```

Do not catch all failures and report them as authentication required. Preserve the existing fallback `DesignQuotaSource` + `NoOpActivationCoordinator` in every blocked state so no model turn can occur.

- [ ] **Step 5: Add view-model diagnostic state**

Add:

```csharp
public string EnvironmentDiagnosticText { get; private set; } = string.Empty;
public bool HasEnvironmentDiagnostic => !string.IsNullOrWhiteSpace(EnvironmentDiagnosticText);
public string RuntimeDiagnosticText { get; private set; } = string.Empty;
```

and:

```csharp
public void ApplyStartupEnvironment(StartupEnvironmentStatus status)
{
    ArgumentNullException.ThrowIfNull(status);

    if (!status.CanActivate)
    {
        IsAutomationEnabled = false;
    }

    EnvironmentDiagnosticText = status.IsReady ? string.Empty : status.UserMessage;
    RuntimeDiagnosticText = $"Widget {status.WidgetVersion} · Codex {status.CodexCliVersion ?? "未知"} · {status.WindowsVersion}";
    OnPropertyChanged(nameof(EnvironmentDiagnosticText));
    OnPropertyChanged(nameof(HasEnvironmentDiagnostic));
    OnPropertyChanged(nameof(RuntimeDiagnosticText));
}
```

Retain `SetAuthenticationRequired()` only if still used by tests/other paths; otherwise migrate tests and remove it.

- [ ] **Step 6: Render diagnostics in the existing main window**

In `MainWindow.xaml`, add a compact text block/banner bound to `EnvironmentDiagnosticText` with visibility driven by `HasEnvironmentDiagnostic`. Do not add a setup wizard or modal dialog. Add a secondary small text line bound to `RuntimeDiagnosticText` in the existing status/footer area.

The diagnostic text must fit the current widget; allow wrapping rather than increasing the window into a multi-page UI.

- [ ] **Step 7: Run app tests and full solution tests**

```powershell
dotnet test tests/CodexUsageWidget.App.Tests/CodexUsageWidget.App.Tests.csproj -c Release
dotnet test CodexUsageWidget.sln -c Release
```

Expected: all automated suites pass; real-credential acceptance tests keep their existing explicit skip gates.

- [ ] **Step 8: Commit**

```bash
git add src/CodexUsageWidget.App tests/CodexUsageWidget.App.Tests
git commit -m "feat: surface startup environment diagnostics"
```

---

### Task 4: Make `package.ps1` produce a versioned portable release and checksum gate

**Files:**
- Modify: `scripts/package.ps1`
- Create: `scripts/verify-release.ps1`

**Interfaces:**
- `package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version <semver> -Clean` produces `artifacts/publish/win-x64/Release/`, `artifacts/release/CodexUsageWidget-<version>-win-x64.zip`, and `artifacts/release/SHA256SUMS.txt`.
- `verify-release.ps1 -Version <semver> -RuntimeIdentifier win-x64 [-RequireInstaller] [-RequireSignatures]` exits non-zero on any invariant failure.

- [ ] **Step 1: Add a failing local packaging acceptance invocation**

Before changing the script, run:

```powershell
.\scripts\package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.9.0-beta.1 -Clean
```

Expected: parameter binding failure because `-Version` and `-Clean` do not yet exist.

- [ ] **Step 2: Extend package parameters and enforce semantic version input**

Add:

```powershell
[Parameter(Mandatory = $true)]
[ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
[string] $Version,

[switch] $Clean
```

Keep existing `Configuration`, `RuntimeIdentifier`, `OutputDirectory`, and `NoRestore` behavior.

If `-Clean` is present, remove only repository-owned output roots:

```powershell
$publishRoot = Join-Path $repoRoot 'artifacts\publish'
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
Remove-Item -LiteralPath $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $releaseRoot -Recurse -Force -ErrorAction SilentlyContinue
```

- [ ] **Step 3: Pass one version into all .NET version properties**

Add publish properties:

```powershell
'-p:Version=' + $Version
'-p:InformationalVersion=' + $Version
'-p:FileVersion=' + (($Version -split '-')[0] + '.0')
```

If `FileVersion` requires four numeric components, normalize `1.2.3` to `1.2.3.0`; never put prerelease text into `FileVersion`.

Also add:

```powershell
'-p:IncludeNativeLibrariesForSelfExtract=true'
```

only if the current SQLite/native publish output requires it. The acceptance condition is runnable release payload, not one physical file.

- [ ] **Step 4: Build the portable ZIP from the publish directory**

After publish and notice-copy, create:

```powershell
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$zipPath = Join-Path $releaseRoot "CodexUsageWidget-$Version-$RuntimeIdentifier.zip"
Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
```

Do not include repository source directories in the archive.

- [ ] **Step 5: Generate `SHA256SUMS.txt` deterministically**

For each current primary artifact that exists, write a line:

```text
<lowercase-sha256>  <filename>
```

At portable-only beta stage this file contains the ZIP checksum; after installer creation the release pipeline regenerates it with both ZIP and Setup checksums.

- [ ] **Step 6: Create `verify-release.ps1`**

The script must:

1. confirm release ZIP exists.
2. extract it to a fresh temporary directory.
3. confirm `CodexUsageWidget.exe` and `THIRD-PARTY-NOTICES.md` exist.
4. execute the extracted EXE with `--version`, redirect stdout, wait for exit, and require exact `$Version` plus exit code `0`.
5. reject files matching `*.cs`, `*.csproj`, `*.sln`, `*.pdb`, paths containing `\obj\` or `\tests\`.
6. parse `SHA256SUMS.txt` and recompute every listed file hash.
7. with `-RequireInstaller`, require `CodexUsageWidget-Setup-$Version.exe`.
8. with `-RequireSignatures`, require `(Get-AuthenticodeSignature ...).Status -eq 'Valid'` for extracted `CodexUsageWidget.exe` and the installer.
9. remove the temporary extraction directory in `finally`.

Use `Start-Process -Wait -PassThru -RedirectStandardOutput` for the GUI-subsystem executable so verification does not depend on terminal attachment behavior.

- [ ] **Step 7: Run the complete local portable gate**

```powershell
.\scripts\package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.9.0-beta.1 -Clean
.\scripts\verify-release.ps1 -Version 0.9.0-beta.1 -RuntimeIdentifier win-x64
```

Expected: both commands exit `0`; ZIP extraction version is exactly `0.9.0-beta.1`.

- [ ] **Step 8: Run the full automated suite**

```powershell
.\scripts\build.ps1 -Configuration Release
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add scripts/package.ps1 scripts/verify-release.ps1
git commit -m "build: produce verified portable releases"
```

---

### Task 5: Add the per-user Inno Setup installer

**Files:**
- Create: `installer/CodexUsageWidget.iss`
- Create: `scripts/build-installer.ps1`
- Modify: `scripts/verify-release.ps1`

**Interfaces:**
- `build-installer.ps1 -Version <semver> -SourceDirectory <publish-dir> [-IsccPath <path>]` produces `artifacts/release/CodexUsageWidget-Setup-<version>.exe`.
- Installer targets `%LOCALAPPDATA%\Programs\CodexUsageWidget\`, creates a Start Menu shortcut and uninstall entry, launches the app by default after install, and requires no elevation.

- [ ] **Step 1: Create the installer definition with fixed per-user semantics**

Create `installer/CodexUsageWidget.iss` with these required directives:

```ini
[Setup]
AppId={{C5A6B234-4E3E-4C2F-9E2B-7B43DA9B7D7A}
AppName=Codex Usage Widget
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\CodexUsageWidget
DefaultGroupName=Codex Usage Widget
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
UninstallDisplayName=Codex Usage Widget
OutputBaseFilename=CodexUsageWidget-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Codex Usage Widget"; Filename: "{app}\CodexUsageWidget.exe"

[Run]
Filename: "{app}\CodexUsageWidget.exe"; Description: "Launch Codex Usage Widget"; Flags: nowait postinstall skipifsilent
```

Do not add a `[Registry]` startup entry. The existing in-app `Start with Windows` preference owns that behavior.

Do not add an uninstall action that removes `%LOCALAPPDATA%\CodexUsageWidget\`.

- [ ] **Step 2: Create deterministic installer build wrapper**

`scripts/build-installer.ps1` must validate the source EXE exists, create `artifacts/release`, invoke ISCC with compile-time defines, and verify the expected output exists:

```powershell
& $IsccPath `
  "/DAppVersion=$Version" `
  "/DSourceDir=$SourceDirectory" `
  "/O$releaseRoot" `
  $issPath
```

Default `IsccPath` resolution order:

1. explicit `-IsccPath`.
2. `$env:ISCC_PATH`.
3. `%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe`.
4. fail with a clear message instructing contributors to install Inno Setup 6.

- [ ] **Step 3: Build a local unsigned beta installer**

Run:

```powershell
.\scripts\package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.9.0-beta.2 -Clean
.\scripts\build-installer.ps1 -Version 0.9.0-beta.2 -SourceDirectory .\artifacts\publish\win-x64\Release
```

Expected: `artifacts\release\CodexUsageWidget-Setup-0.9.0-beta.2.exe` exists.

- [ ] **Step 4: Regenerate checksums after installer creation**

Move checksum generation into a shared block/function or make `verify-release.ps1` regenerate/verify against both primary artifacts when `-RequireInstaller` is set. Final file must contain exactly the ZIP and Setup entries for this stage.

- [ ] **Step 5: Verify installer-aware release output**

```powershell
.\scripts\verify-release.ps1 -Version 0.9.0-beta.2 -RuntimeIdentifier win-x64 -RequireInstaller
```

Expected: PASS.

- [ ] **Step 6: Manually test installer semantics on the development machine**

Verify:

```text
install location: %LOCALAPPDATA%\Programs\CodexUsageWidget\
Start Menu shortcut exists
Add/Remove Programs uninstall entry exists
no UAC elevation required
post-install launch works
uninstall removes program files
%LOCALAPPDATA%\CodexUsageWidget\ remains
```

Record the result in the commit message body or local implementation notes; formal clean-machine evidence is Task 9.

- [ ] **Step 7: Commit**

```bash
git add installer/CodexUsageWidget.iss scripts/build-installer.ps1 scripts/verify-release.ps1
git commit -m "build: add per-user Windows installer"
```

---

### Task 6: Add Windows CI for tests and portable release verification

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Trigger: pushes to `main`, pull requests targeting `main`.
- Produces no GitHub Release; it only proves source/test/package integrity.

- [ ] **Step 1: Create `ci.yml` with least-privilege permissions**

Use:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

permissions:
  contents: read

jobs:
  test-and-package:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Restore
        shell: pwsh
        run: dotnet restore CodexUsageWidget.sln

      - name: Test
        shell: pwsh
        run: .\scripts\build.ps1 -Configuration Release -NoRestore

      - name: Package portable artifact
        shell: pwsh
        run: .\scripts\package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.0.0-ci -Clean

      - name: Verify portable artifact
        shell: pwsh
        run: .\scripts\verify-release.ps1 -Version 0.0.0-ci -RuntimeIdentifier win-x64
```

If the semantic-version validator intentionally rejects `0.0.0-ci`, change both calls to `0.0.0-ci.1`; keep the same value in both steps.

- [ ] **Step 2: Validate workflow syntax locally**

At minimum parse it as YAML with an available local parser, or inspect through GitHub after push. Do not add third-party actions solely to lint this one workflow.

- [ ] **Step 3: Push the implementation branch and inspect the first CI run**

Expected:

```text
restore -> pass
test -> pass
package -> pass
verify-release -> pass
```

If GitHub Actions exposes a previously hidden Windows-only failure, fix the productization code/script rather than weakening the gate.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: verify Windows builds and portable packages"
```

---

### Task 7: Add tagged release automation, signing, and stable-release blocking

**Files:**
- Create: `.github/workflows/release.yml`
- Modify: `scripts/verify-release.ps1`
- Modify: `docs/troubleshooting.md` only if certificate/signature support needs user-facing notes at this stage.

**Interfaces:**
- Trigger: pushed tags matching `v*`.
- Secrets: `WINDOWS_SIGNING_PFX_BASE64`, `WINDOWS_SIGNING_PFX_PASSWORD`.
- Environment variable: `WINDOWS_SIGNING_TIMESTAMP_URL` set in workflow to the chosen trusted RFC3161 timestamp endpoint.
- Stable tags (`v1.0.0` and later non-prerelease tags) fail before publication if signing secrets are absent or signatures are invalid.
- Beta/RC tags may be published unsigned only when the job explicitly identifies them as prerelease; `v1.0.0` may not.

- [ ] **Step 1: Implement tag-to-version validation**

In PowerShell inside `release.yml`:

```powershell
$tag = "${{ github.ref_name }}"
if ($tag -notmatch '^v(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)$') {
  throw "Tag '$tag' is not a supported semantic version tag."
}
$version = $Matches.version
"version=$version" >> $env:GITHUB_OUTPUT
$isPrerelease = $version.Contains('-')
"prerelease=$($isPrerelease.ToString().ToLowerInvariant())" >> $env:GITHUB_OUTPUT
```

Expose these as outputs from an `id: version` step.

- [ ] **Step 2: Build/test/package before touching signing material**

Workflow order must be:

```text
checkout
setup exact SDK from global.json
restore
full automated tests
package portable payload
```

Do not decode certificate secrets until tests and publish have succeeded.

- [ ] **Step 3: Add application signing step**

For stable releases, require both signing secrets. For prereleases, sign when secrets are present; otherwise continue as unsigned prerelease.

Decode PFX only into `$env:RUNNER_TEMP`, invoke SignTool with SHA-256 file and timestamp digests, then delete the PFX in `finally`.

Command shape:

```powershell
signtool sign /fd SHA256 /td SHA256 /tr $env:WINDOWS_SIGNING_TIMESTAMP_URL /f $pfxPath /p $env:WINDOWS_SIGNING_PFX_PASSWORD $exePath
```

Never echo the password or PFX content.

- [ ] **Step 4: Build installer from the exact signed publish directory**

Install Inno Setup 6 on the Windows runner only if `ISCC.exe` is absent. Use Chocolatey available on GitHub-hosted Windows runners:

```powershell
choco install innosetup --no-progress -y
```

Resolve `ISCC.exe`, then run:

```powershell
.\scripts\build-installer.ps1 -Version $version -SourceDirectory .\artifacts\publish\win-x64\Release -IsccPath $iscc
```

This ordering guarantees installer and portable ZIP use the same application payload. If the ZIP was created before application signing, re-create the ZIP after signing before checksums/verification.

- [ ] **Step 5: Sign installer with the same certificate**

Use the same SignTool parameters on:

```text
artifacts\release\CodexUsageWidget-Setup-<version>.exe
```

- [ ] **Step 6: Generate final checksums and verify release**

For signed stable builds:

```powershell
.\scripts\verify-release.ps1 -Version $version -RuntimeIdentifier win-x64 -RequireInstaller -RequireSignatures
```

For unsigned prereleases:

```powershell
.\scripts\verify-release.ps1 -Version $version -RuntimeIdentifier win-x64 -RequireInstaller
```

The checksum file must be generated after final signing because signing changes file bytes.

- [ ] **Step 7: Publish using GitHub CLI with only three primary assets**

Give the job:

```yaml
permissions:
  contents: write
```

Publish with `gh release create` rather than a third-party release action:

```powershell
gh release create "${{ github.ref_name }}" `
  ".\artifacts\release\CodexUsageWidget-Setup-$version.exe" `
  ".\artifacts\release\CodexUsageWidget-$version-win-x64.zip" `
  ".\artifacts\release\SHA256SUMS.txt" `
  --generate-notes `
  $(if ($isPrerelease) { '--prerelease' })
```

Set `GH_TOKEN: ${{ github.token }}` for this step.

- [ ] **Step 8: Add explicit `v1.0.0` manual acceptance protection**

Create a GitHub Actions environment named `stable-release` in repository settings and require manual approval for it. Bind the stable publication job/stage to that environment; prereleases do not use it.

The approver must confirm Task 9 acceptance evidence is committed before approving `v1.0.0` publication.

- [ ] **Step 9: Test with a beta tag before stable**

Use the planned sequence:

```bash
git tag v0.9.0-beta.1
git push origin v0.9.0-beta.1
```

Expected: prerelease created with exactly the Setup EXE, portable ZIP, and checksum file. If signing secrets are not configured yet, the release is visibly marked prerelease and verification does not require signatures.

- [ ] **Step 10: Commit**

```bash
git add .github/workflows/release.yml scripts/verify-release.ps1 docs/troubleshooting.md
git commit -m "ci: automate signed tagged releases"
```

---

### Task 8: Reorganize binary-first documentation and resolve the license gate

**Files:**
- Modify: `README.md`
- Modify: `docs/install.md`
- Modify: `docs/troubleshooting.md`
- Create: `LICENSE` after explicit owner choice.
- Modify: `THIRD-PARTY-NOTICES.md` only if required by the selected installer/tool licensing terms.

**Interfaces:**
- Normal-user path is installer-first.
- Contributor/source-build path remains documented but is secondary.

- [ ] **Step 1: Rewrite README top-level flow**

The first installation section must say, in this order:

```text
1. Install Codex CLI.
2. Run `codex login`.
3. Open GitHub Releases and download `CodexUsageWidget-Setup-<version>.exe`.
4. Double-click the installer.
```

State immediately:

```text
Supported: Windows 10 20H2+ / Windows 11 x64
Authentication: ChatGPT-backed Codex login
No .NET SDK or administrator rights required for the release installer
```

Move `Build from source` below installer and portable ZIP instructions.

- [ ] **Step 2: Rewrite `docs/install.md` into four explicit paths**

Use these headings:

```markdown
## Normal installation
## Portable ZIP
## Build from source
## Uninstall and retained local state
```

Normal installation must never instruct users to run `package.ps1` or `install.ps1`.

Retained-state section must explicitly say `%LOCALAPPDATA%\CodexUsageWidget\` is preserved on uninstall because it contains settings, audit history, and activation safety state.

- [ ] **Step 3: Add exact startup troubleshooting cases**

`docs/troubleshooting.md` must include:

```text
Codex CLI not found
Codex login required
API-key-only authentication unsupported
Codex App Server incompatible
Windows signature/SmartScreen checks
How to obtain Widget/Codex/Windows version diagnostics without exposing credentials
```

- [ ] **Step 4: Stop and obtain the owner's explicit license selection**

This is a required human decision, not an implementation default. Record the selected SPDX license identifier in the implementation session before creating `LICENSE`.

Do not proceed to stable `1.0.0` if the owner has not made this choice.

- [ ] **Step 5: Create the selected root `LICENSE` and update README wording**

Use the canonical full text for the license the owner selected. Replace all README wording such as `LICENSE (if present)` with the actual license name and link/reference.

- [ ] **Step 6: Run documentation consistency search**

Run:

```powershell
git grep -n -E "if present|package\.ps1|install\.ps1|\.NET 8 Windows Desktop Runtime" -- README.md docs
```

Expected:

- `package.ps1` / `install.ps1` only appear in contributor/source-build sections.
- no `LICENSE (if present)` wording remains.
- normal binary-install sections do not require a runtime/SDK.

- [ ] **Step 7: Commit**

```bash
git add README.md docs/install.md docs/troubleshooting.md LICENSE THIRD-PARTY-NOTICES.md
git commit -m "docs: make release installer the primary user path"
```

---

### Task 9: Execute v0.9 beta/RC acceptance and record v1 evidence

**Files:**
- Modify: `docs/acceptance-matrix.md`

**Interfaces:**
- Produces the evidence required for `stable-release` approval.
- Does not change domain behavior.

- [ ] **Step 1: Run automated release gate on the exact RC commit**

Run locally or use CI evidence for:

```powershell
.\scripts\build.ps1 -Configuration Release
.\scripts\package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 0.9.0-rc.1 -Clean
.\scripts\build-installer.ps1 -Version 0.9.0-rc.1 -SourceDirectory .\artifacts\publish\win-x64\Release
.\scripts\verify-release.ps1 -Version 0.9.0-rc.1 -RuntimeIdentifier win-x64 -RequireInstaller
```

For the actual signed RC produced by GitHub Actions, require signatures in the release run.

- [ ] **Step 2: Run clean-machine matrix on a Windows x64 VM with no repo, Visual Studio, Git, or .NET SDK**

Record pass/fail for each exact case:

```text
A. no Codex CLI -> clear missing-CLI diagnostic; no crash; activation disabled
B. Codex CLI installed, not logged in -> explicit `codex login` instruction
C. after `codex login` -> Setup install and widget launch succeed
D. no .NET runtime/SDK installation prompt appears
E. launch twice -> single-instance behavior preserved
F. disconnect network -> safe stale/disconnected state
G. restore network -> recovery succeeds
H. enable Start with Windows and reboot -> widget starts once
I. uninstall -> program binaries removed; local state retained
J. reinstall -> retained state loads safely
K. portable ZIP -> extract and double-click works independently of installer
```

- [ ] **Step 3: Run authenticated read-only acceptance on the exact RC**

Run the existing `ReadOnlyAuthenticatedSmokeTest.cs` with its documented opt-in environment gate. Record:

```text
application version
Codex CLI version
Windows version
five-hour window mapped: pass/fail
weekly window mapped: pass/fail
countdown/reconciliation: pass/fail
model turn issued: must be no
```

- [ ] **Step 4: Run one real fully-unused five-hour activation acceptance**

Run the existing `RealActivationAcceptanceTest.cs` only when the real account has a fully unused eligible five-hour window.

Required result:

```text
eligibility observed at exact zero
one guarded activation issued
reset/activation success verified
no duplicate activation
cleanup/audit complete or safely deferred
```

If the quota precondition is unavailable, publish another RC if useful, but do not approve `v1.0.0`.

- [ ] **Step 5: Run two external-user tests**

Give each tester only this instruction:

```text
Install Codex CLI, run `codex login`, download the latest Codex Usage Widget installer from GitHub Releases, and double-click it.
```

Do not explain build scripts, App Server, local database paths, or source layout.

Pass condition: both users complete installation and first successful quota sync without developer assistance.

- [ ] **Step 6: Record evidence in `docs/acceptance-matrix.md`**

Add a v1 productization section/table with rows for:

```text
CI run URL / commit SHA
Release candidate tag
Widget version
Codex CLI version
Windows version
Authenticode application signature
Authenticode installer signature
Clean-machine A-K
Authenticated read-only smoke test
Real activation acceptance
External tester 1
External tester 2
License present
```

Do not record emails, account IDs, tokens, prompts, or other sensitive values.

- [ ] **Step 7: Run final repository gate**

```powershell
dotnet test CodexUsageWidget.sln -c Release
.\scripts\verify-release.ps1 -Version 1.0.0 -RuntimeIdentifier win-x64 -RequireInstaller -RequireSignatures
```

The second command should be run against the signed `v1.0.0` candidate artifacts before release approval.

- [ ] **Step 8: Commit acceptance evidence**

```bash
git add docs/acceptance-matrix.md
git commit -m "docs: record v1 release acceptance evidence"
```

---

### Task 10: Publish `v1.0.0` and verify the public user path

**Files:**
- No source changes expected; only fix blockers discovered by the final release rehearsal in their owning task/files.

**Interfaces:**
- Input: main commit with all Task 1-9 changes and evidence.
- Output: signed GitHub Release `v1.0.0` with exactly three primary assets.

- [ ] **Step 1: Merge only with green CI**

Confirm the target `main` commit has passing Windows CI and all required acceptance evidence from Task 9.

- [ ] **Step 2: Create and push the stable tag**

```bash
git checkout main
git pull --ff-only
git tag v1.0.0
git push origin v1.0.0
```

- [ ] **Step 3: Review the `stable-release` approval gate**

Before approval, verify:

```text
signing secrets configured
license committed
clean-machine evidence complete
real activation acceptance complete
two external testers passed
```

Approve only when all five are true.

- [ ] **Step 4: Verify the published Release**

The public release must contain exactly:

```text
CodexUsageWidget-Setup-1.0.0.exe
CodexUsageWidget-1.0.0-win-x64.zip
SHA256SUMS.txt
```

Download the public assets onto a clean Windows x64 machine and verify SHA-256 values against `SHA256SUMS.txt`.

- [ ] **Step 5: Perform the final public-path smoke test**

On the clean machine, use only:

```text
codex login
Download Setup EXE
Double-click
```

Pass condition: the widget starts, reports a valid quota state, and does not request a .NET runtime/SDK, PowerShell build command, source checkout, or elevation.

- [ ] **Step 6: Treat any failure as a release blocker, not a documentation workaround**

If the final smoke test requires a developer-only workaround, delete/mark the release as prerelease as appropriate, fix the owning task, issue a new RC, and repeat Task 9. Do not document repository-internal workarounds as the normal-user path.

---

## Final Verification Checklist

Before claiming v1 productization complete, verify all of these are true:

- [ ] `CodexUsageWidget.exe --version` returns the tagged semantic version and exits without starting WPF.
- [ ] App Server `ClientInformation` uses the same application version instead of hard-coded `1.0.0`.
- [ ] Missing CLI, missing login, unsupported auth, and protocol incompatibility are distinguishable to the user.
- [ ] Every blocked startup state disables automatic activation and uses no-op activation behavior.
- [ ] `package.ps1` creates a clean versioned portable ZIP from a self-contained `win-x64` publish.
- [ ] `verify-release.ps1` checks version, archive contents, hashes, and signatures when required.
- [ ] Installer is per-user, no-elevation, creates Start Menu/uninstall entries, and preserves local state.
- [ ] CI passes on `main` and PRs.
- [ ] Tagged release workflow builds from the tagged commit and publishes only the three primary assets.
- [ ] Stable artifacts are Authenticode-signed and timestamped.
- [ ] README normal-user path contains no source build requirement.
- [ ] Root `LICENSE` exists with the owner's explicit license choice.
- [ ] Clean-machine acceptance passes without .NET SDK/runtime installation.
- [ ] Authenticated read-only smoke test passes on the release candidate.
- [ ] Real fully-unused five-hour activation acceptance passes once on the release candidate.
- [ ] Two external users complete setup without developer assistance.
- [ ] `docs/acceptance-matrix.md` records non-sensitive v1 evidence.
- [ ] Public `v1.0.0` setup path is: install Codex CLI → `codex login` → download Setup EXE → double-click → widget works.
