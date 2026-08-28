using CodexUsageWidget.App.Services;
using CodexUsageWidget.App.Tests.Testing;
using CodexUsageWidget.App.ViewModels;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Monitoring;
using Xunit;

namespace CodexUsageWidget.App.Tests.ViewModels;

public sealed class StartupDiagnosticsTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly AccountIdentity Identity = new("test@local.invalid", "test", "global");

    private readonly ManualDelay _delay;
    private readonly QuotaMonitor _monitor;
    private readonly MainViewModel _viewModel;

    public StartupDiagnosticsTests()
    {
        var clock = new ManualClock(Start);
        _delay = new ManualDelay(clock);
        var source = new FakeQuotaSource();
        _monitor = new QuotaMonitor(source, clock, _delay);
        _viewModel = new MainViewModel(
            _monitor,
            new FakeActivationCoordinator(),
            Identity,
            new FakeStartupRegistration(),
            new FakeTrayIconService(),
            new FakeApplicationShell(),
            new SynchronousDispatcher());
    }

    [Theory]
    [InlineData(StartupEnvironmentKind.CodexCliMissing, "未找到 Codex CLI。请先安装 Codex CLI，然后运行 codex login。")]
    [InlineData(StartupEnvironmentKind.AuthenticationRequired, "Codex 尚未登录。请在终端运行 codex login，然后重新连接。")]
    [InlineData(StartupEnvironmentKind.UnsupportedAuthentication, "需要使用 ChatGPT 账号登录 Codex；仅 API Key 的认证方式暂不支持。")]
    [InlineData(StartupEnvironmentKind.AppServerIncompatible, "当前 Codex CLI 与 Codex Usage Widget 的 App Server 协议不兼容。")]
    public void ApplyStartupEnvironmentBlockedStateDisablesAutomation(
        StartupEnvironmentKind kind,
        string expectedMessage)
    {
        _viewModel.IsAutomationEnabled = true;

        _viewModel.ApplyStartupEnvironment(new StartupEnvironmentStatus(
            kind,
            expectedMessage,
            "1.0.0",
            "0.148.0-alpha.9",
            "Windows 11",
            CanActivate: false));

        Assert.False(_viewModel.IsAutomationEnabled);
        Assert.Equal(expectedMessage, _viewModel.EnvironmentDiagnosticText);
        Assert.True(_viewModel.HasEnvironmentDiagnostic);
    }

    [Fact]
    public void ApplyStartupEnvironmentReadyClearsDiagnosticAndKeepsAutomation()
    {
        _viewModel.ApplyStartupEnvironment(new StartupEnvironmentStatus(
            StartupEnvironmentKind.CodexCliMissing,
            "未找到 Codex CLI。请先安装 Codex CLI，然后运行 codex login。",
            "1.0.0",
            null,
            "Windows 11",
            CanActivate: false));
        Assert.True(_viewModel.HasEnvironmentDiagnostic);

        _viewModel.IsAutomationEnabled = true;
        _viewModel.ApplyStartupEnvironment(new StartupEnvironmentStatus(
            StartupEnvironmentKind.Ready,
            string.Empty,
            "1.0.0",
            "0.148.0-alpha.9",
            "Windows 11",
            CanActivate: true));

        Assert.True(_viewModel.IsAutomationEnabled);
        Assert.Equal(string.Empty, _viewModel.EnvironmentDiagnosticText);
        Assert.False(_viewModel.HasEnvironmentDiagnostic);
    }

    [Fact]
    public void ApplyStartupEnvironmentExposesNonSensitiveRuntimeDiagnostics()
    {
        _viewModel.ApplyStartupEnvironment(new StartupEnvironmentStatus(
            StartupEnvironmentKind.StartupError,
            "error",
            "1.0.0",
            null,
            "Windows 11",
            CanActivate: false));

        Assert.Contains("Widget 1.0.0", _viewModel.RuntimeDiagnosticText, StringComparison.Ordinal);
        Assert.Contains("Codex 未知", _viewModel.RuntimeDiagnosticText, StringComparison.Ordinal);
        Assert.Contains("Windows 11", _viewModel.RuntimeDiagnosticText, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _delay.Dispose();
        _monitor.StopAsync().GetAwaiter().GetResult();
        _viewModel.Dispose();
    }
}
