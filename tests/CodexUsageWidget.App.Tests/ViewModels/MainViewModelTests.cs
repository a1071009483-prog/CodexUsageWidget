using CodexUsageWidget.App.Tests.Testing;
using CodexUsageWidget.App.ViewModels;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Monitoring;
using CodexUsageWidget.Core.Quota;
using Xunit;

namespace CodexUsageWidget.App.Tests.ViewModels;

public sealed class MainViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly AccountIdentity Identity = new("test@local.invalid", "test", "global");

    private readonly ManualClock _clock;
    private readonly ManualDelay _delay;
    private readonly FakeQuotaSource _source;
    private readonly QuotaMonitor _monitor;
    private readonly FakeActivationCoordinator _activation;
    private readonly FakeStartupRegistration _startup;
    private readonly FakeTrayIconService _tray;
    private readonly FakeApplicationShell _shell;
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        _clock = new ManualClock(Start);
        _delay = new ManualDelay(_clock);
        _source = new FakeQuotaSource();
        _monitor = new QuotaMonitor(_source, _clock, _delay);
        _activation = new FakeActivationCoordinator();
        _startup = new FakeStartupRegistration();
        _tray = new FakeTrayIconService();
        _shell = new FakeApplicationShell();

        _viewModel = new MainViewModel(
            _monitor,
            _activation,
            Identity,
            _startup,
            _tray,
            _shell,
            new SynchronousDispatcher());
    }

    [Fact]
    public void SetAuthenticationRequiredDisablesAutomationAndUpdatesConnectionText()
    {
        _viewModel.IsAutomationEnabled = true;

        _viewModel.SetAuthenticationRequired();

        Assert.False(_viewModel.IsAutomationEnabled);
        Assert.Equal("需要认证", _viewModel.ConnectionStateText);
    }

    public void Dispose()
    {
        _delay.Dispose();
        _monitor.StopAsync().GetAwaiter().GetResult();
        _viewModel.Dispose();
    }

    [Fact]
    public void StartWithWindowsReflectsRegistrationState()
    {
        Assert.Equal(_startup.IsRegistered, _viewModel.StartWithWindows);
    }

    [Fact]
    public void ToggleAutomationChangesPropertyAndTrayLabel()
    {
        Assert.False(_viewModel.IsAutomationEnabled);
        Assert.Equal("恢复自动触发", _viewModel.PauseResumeHeader);

        _viewModel.ToggleAutomationCommand.Execute(null);

        Assert.True(_viewModel.IsAutomationEnabled);
        Assert.Equal("暂停自动触发", _viewModel.PauseResumeHeader);
        Assert.Equal("暂停自动触发", _tray.PauseResumeLabel);
    }

    [Fact]
    public void ShowHideCommandTogglesWindowVisibility()
    {
        _viewModel.SetMainWindowVisible(true);
        _viewModel.ShowHideCommand.Execute(null);

        Assert.True(_shell.HideMainWindowCalled);

        _viewModel.SetMainWindowVisible(false);
        _viewModel.ShowHideCommand.Execute(null);

        Assert.True(_shell.ShowMainWindowCalled);
    }

    [Fact]
    public void ExitCommandShutsDownApplication()
    {
        _viewModel.ExitCommand.Execute(null);
        Assert.True(_shell.ShutdownCalled);
    }

    [Fact]
    public void OpenAuditCommandOpensAuditWindow()
    {
        _viewModel.OpenAuditCommand.Execute(null);
        Assert.True(_shell.OpenAuditWindowCalled);
    }

    [Fact]
    public async Task SnapshotUpdatesCardsAndConnectionState()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 25));

        await _viewModel.StartAsync();

        Assert.Equal("已同步 · Pro", _viewModel.ConnectionStateText);
        Assert.Equal(25, _viewModel.FiveHour.UsedPercent);
        Assert.Equal(75, _viewModel.FiveHour.RemainingPercent);
        Assert.Equal("已同步", _viewModel.FiveHour.StatusText);
    }

    [Fact]
    public async Task InitialConnectionFailureDoesNotClaimSuccessfulSynchronizationTime()
    {
        _source.EnqueueResult(new QuotaSourceResult(false, null, "cannot start app server"));

        await _viewModel.StartAsync();

        Assert.Equal("连接错误", _viewModel.ConnectionStateText);
        Assert.Equal("--", _viewModel.FiveHour.LastSyncTimeText);
        Assert.Equal("--", _viewModel.Weekly.LastSyncTimeText);
    }

    [Fact]
    public async Task ActivationTriggeredWhenAutomationEnabledAndFreshZero()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 0));
        _viewModel.IsAutomationEnabled = true;

        await _viewModel.StartAsync();
        await WaitForActivationAsync();

        Assert.Single(_activation.Calls);
    }

    [Fact]
    public async Task ActivationNotTriggeredWhenAutomationPaused()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 0));
        _viewModel.IsAutomationEnabled = false;

        await _viewModel.StartAsync();
        await Task.Delay(50);

        Assert.Empty(_activation.Calls);
    }

    [Fact]
    public async Task ActivationNotTriggeredWhenStale()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 10));
        _viewModel.IsAutomationEnabled = true;

        await _viewModel.StartAsync();
        Assert.Empty(_activation.Calls);

        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 0));
        _source.RaiseUpdated();
        await WaitForActivationAsync();
        Assert.Single(_activation.Calls);

        _clock.Advance(TimeSpan.FromSeconds(121));
        _delay.AdvanceAsync(TimeSpan.FromSeconds(121));
        await Task.Delay(50);

        Assert.Single(_activation.Calls);
        Assert.DoesNotContain("已同步", _viewModel.FiveHour.StatusText);
    }

    [Fact]
    public async Task ActivationNotTriggeredForNonZeroUsage()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 5));
        _viewModel.IsAutomationEnabled = true;

        await _viewModel.StartAsync();
        await Task.Delay(50);

        Assert.Empty(_activation.Calls);
    }

    [Fact]
    public async Task ManualCheckUsesOneShotAuthorizationWithoutChangingPausedPreference()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 5));
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 5));
        _viewModel.IsAutomationEnabled = false;

        await _viewModel.StartAsync();
        _viewModel.ManualActivationCommand.Execute(null);
        await WaitForConditionAsync(() =>
            _activation.Calls.Count == 1
            && _viewModel.ManualActivationStatusText == "当前窗口无需触发");

        ActivationCall call = Assert.Single(_activation.Calls);
        Assert.False(call.Request.IsAutomationEnabled);
        Assert.True(call.Request.IsUserInitiated);
        Assert.False(_viewModel.IsAutomationEnabled);
        Assert.Equal("当前窗口无需触发", _viewModel.ManualActivationStatusText);
        Assert.Equal(2, _source.ReadCount);
    }

    [Fact]
    public async Task ManualCheckIgnoresDuplicateInvocationWhileRunning()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 5));
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 5));
        var completion = new TaskCompletionSource<ActivationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _activation.OnTryActivate = (_, _) => completion.Task;

        await _viewModel.StartAsync();
        _viewModel.ManualActivationCommand.Execute(null);
        await WaitForConditionAsync(() => _activation.Calls.Count == 1);

        Assert.True(_viewModel.IsManualActivationRunning);
        Assert.False(_viewModel.ManualActivationCommand.CanExecute(null));
        Assert.Equal("正在检查…", _viewModel.ManualActivationButtonText);

        _viewModel.ManualActivationCommand.Execute(null);
        Assert.Single(_activation.Calls);
        Assert.Equal(2, _source.ReadCount);

        completion.SetResult(ActivationResult.NotEligible("usage-nonzero"));
        await WaitForConditionAsync(() => !_viewModel.IsManualActivationRunning);

        Assert.True(_viewModel.ManualActivationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ManualCheckOwnsRefreshEvaluationWhenAutomationIsEnabled()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 5));
        _source.EnqueueSuccess(FiveHourSnapshot(usedPercent: 0));
        _viewModel.IsAutomationEnabled = true;
        var completion = new TaskCompletionSource<ActivationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _activation.OnTryActivate = (_, _) => completion.Task;

        await _viewModel.StartAsync();
        _viewModel.ManualActivationCommand.Execute(null);

        try
        {
            await WaitForConditionAsync(() => _activation.Calls.Count > 0);
            await Task.Delay(50);

            ActivationCall call = Assert.Single(_activation.Calls);
            Assert.True(call.Request.IsUserInitiated);
        }
        finally
        {
            completion.TrySetResult(ActivationResult.NotEligible("usage-nonzero"));
        }
    }

    [Fact]
    public void ChangingStartWithWindowsUpdatesRegistryAndTrayCheck()
    {
        _viewModel.StartWithWindows = true;

        Assert.True(_startup.IsRegistered);
        Assert.Equal(1, _startup.RegisterCallCount);
        Assert.True(_tray.StartWithWindowsChecked);

        _viewModel.StartWithWindows = false;

        Assert.False(_startup.IsRegistered);
        Assert.Equal(1, _startup.UnregisterCallCount);
        Assert.False(_tray.StartWithWindowsChecked);
    }

    private static RawRateLimitSnapshot FiveHourSnapshot(int usedPercent)
    {
        return new RawRateLimitSnapshot(
            "limit-5h",
            "5 hour credits",
            "Pro",
            new RawRateLimitWindow(usedPercent, Start.AddHours(5).ToUnixTimeMilliseconds(), 300L));
    }

    private async Task WaitForActivationAsync()
    {
        await WaitForConditionAsync(() => _activation.Calls.Count > 0);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
