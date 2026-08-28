using System.Windows.Input;
using CodexUsageWidget.App.Helpers;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Monitoring;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.App.ViewModels;

/// <summary>
/// View model for the main widget window and tray experience.
/// </summary>
public sealed class MainViewModel : ViewModelBase, ITrayCommandSource, IDisposable
{
    private readonly QuotaMonitor _monitor;
    private readonly IActivationCoordinator _activationCoordinator;
    private readonly AccountIdentity _accountIdentity;
    private readonly IStartupRegistration _startupRegistration;
    private readonly ITrayIconService _trayIconService;
    private readonly IApplicationShell _shell;
    private readonly IDispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetimeCts = new();

    private bool _isAutomationEnabled;
    private bool _startWithWindows;
    private bool _isMainWindowVisible = true;
    private string _connectionStateText = "未连接";
    private string _manualActivationStatusText = "安全检查不会绕过额度条件";
    private int _manualActivationGate;
    private bool _disposed;

    public MainViewModel(
        QuotaMonitor monitor,
        IActivationCoordinator activationCoordinator,
        AccountIdentity accountIdentity,
        IStartupRegistration startupRegistration,
        ITrayIconService trayIconService,
        IApplicationShell shell,
        IDispatcher dispatcher)
        : base(dispatcher)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _activationCoordinator = activationCoordinator ?? throw new ArgumentNullException(nameof(activationCoordinator));
        _accountIdentity = accountIdentity ?? throw new ArgumentNullException(nameof(accountIdentity));
        _startupRegistration = startupRegistration ?? throw new ArgumentNullException(nameof(startupRegistration));
        _trayIconService = trayIconService ?? throw new ArgumentNullException(nameof(trayIconService));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        FiveHour = new QuotaCardViewModel(QuotaBucket.FiveHour, dispatcher);
        Weekly = new QuotaCardViewModel(QuotaBucket.Weekly, dispatcher);

        _isAutomationEnabled = false;
        _startWithWindows = _startupRegistration.IsRegistered;

        ShowHideCommand = new RelayCommand(ToggleShowHide);
        RefreshNowCommand = new RelayCommand(() => _ = ForceRefreshAsync());
        ManualActivationCommand = new RelayCommand(
            () => _ = RunManualActivationAsync(),
            () => !IsManualActivationRunning);
        ToggleAutomationCommand = new RelayCommand(ToggleAutomation);
        OpenAuditCommand = new RelayCommand(() => _shell.OpenAuditWindow());
        ReconnectCommand = new RelayCommand(() => _ = ForceRefreshAsync());
        ExitCommand = new RelayCommand(() => _shell.Shutdown());

        _monitor.SnapshotChanged += OnSnapshotChanged;
    }

    /// <summary>The five-hour quota card.</summary>
    public QuotaCardViewModel FiveHour { get; }

    /// <summary>The weekly quota card.</summary>
    public QuotaCardViewModel Weekly { get; }

    /// <summary>Localized connection state text.</summary>
    public string ConnectionStateText
    {
        get => _connectionStateText;
        private set => SetProperty(ref _connectionStateText, value);
    }

    /// <summary>Concise progress or terminal state for the safe manual check.</summary>
    public string ManualActivationStatusText
    {
        get => _manualActivationStatusText;
        private set => SetProperty(ref _manualActivationStatusText, value);
    }

    /// <summary>Whether a user-invoked guarded evaluation is currently running.</summary>
    public bool IsManualActivationRunning => Volatile.Read(ref _manualActivationGate) != 0;

    /// <summary>Localized button label for the safe manual check.</summary>
    public string ManualActivationButtonText => IsManualActivationRunning ? "正在检查…" : "检查并触发";

    /// <summary>Whether automatic five-hour activation is enabled.</summary>
    public bool IsAutomationEnabled
    {
        get => _isAutomationEnabled;
        set
        {
            if (SetProperty(ref _isAutomationEnabled, value))
            {
                OnPropertyChanged(nameof(PauseResumeHeader));
                _trayIconService.SetPauseResumeLabel(PauseResumeHeader);
            }
        }
    }

    /// <summary>Whether the application starts with the current user's Windows session.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                _trayIconService.SetStartWithWindowsChecked(value);
                _ = value
                    ? _startupRegistration.RegisterAsync(_lifetimeCts.Token)
                    : _startupRegistration.UnregisterAsync(_lifetimeCts.Token);
            }
        }
    }

    /// <summary>Header for the Show/Hide tray menu item.</summary>
    public string ShowHideHeader => _isMainWindowVisible ? "隐藏" : "显示";

    /// <summary>Header for the Pause/Resume tray menu item.</summary>
    public string PauseResumeHeader => _isAutomationEnabled ? "暂停自动触发" : "恢复自动触发";

    /// <summary>Shows or hides the main window.</summary>
    public ICommand ShowHideCommand { get; }

    /// <summary>Forces an immediate quota refresh.</summary>
    public ICommand RefreshNowCommand { get; }

    /// <summary>Runs one guarded activation evaluation without changing automatic-trigger preference.</summary>
    public ICommand ManualActivationCommand { get; }

    /// <summary>Toggles automatic five-hour activation.</summary>
    public ICommand ToggleAutomationCommand { get; }

    /// <summary>Opens the local audit window.</summary>
    public ICommand OpenAuditCommand { get; }

    /// <summary>Reconnects the quota monitor.</summary>
    public ICommand ReconnectCommand { get; }

    /// <summary>Shuts down the application.</summary>
    public ICommand ExitCommand { get; }

    ITrayCommand ITrayCommandSource.ShowHideCommand => (ITrayCommand)ShowHideCommand;
    ITrayCommand ITrayCommandSource.RefreshNowCommand => (ITrayCommand)RefreshNowCommand;
    ITrayCommand ITrayCommandSource.ToggleAutomationCommand => (ITrayCommand)ToggleAutomationCommand;
    ITrayCommand ITrayCommandSource.OpenAuditCommand => (ITrayCommand)OpenAuditCommand;
    ITrayCommand ITrayCommandSource.ReconnectCommand => (ITrayCommand)ReconnectCommand;
    ITrayCommand ITrayCommandSource.ExitCommand => (ITrayCommand)ExitCommand;
    string ITrayCommandSource.ShowHideHeader => ShowHideHeader;
    string ITrayCommandSource.PauseResumeHeader => PauseResumeHeader;
    bool ITrayCommandSource.StartWithWindows
    {
        get => StartWithWindows;
        set => StartWithWindows = value;
    }

    /// <summary>
    /// Informs the view model that authentication is required so the UI reflects
    /// the blocked state and automatic activation stays disabled.
    /// </summary>
    public void SetAuthenticationRequired()
    {
        IsAutomationEnabled = false;
        ConnectionStateText = FormatConnectionState(
            MonitoringConnectionState.AuthenticatingRequired,
            scopeLabel: null);
    }

    /// <summary>Informs the view model that the main window visibility changed.</summary>
    public void SetMainWindowVisible(bool visible)
    {
        if (_isMainWindowVisible == visible)
        {
            return;
        }

        _isMainWindowVisible = visible;
        OnPropertyChanged(nameof(ShowHideHeader));
        _trayIconService.SetShowHideLabel(ShowHideHeader);
    }

    /// <summary>Starts the monitor.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _monitor.StartAsync(cancellationToken);
    }

    /// <summary>Stops the monitor and releases tray resources.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _monitor.SnapshotChanged -= OnSnapshotChanged;
        await _monitor.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.SnapshotChanged -= OnSnapshotChanged;
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        _trayIconService.Dispose();
        _ = _monitor.StopAsync();
    }

    private void OnSnapshotChanged(object? sender, QuotaSnapshot snapshot)
    {
        _dispatcher.Invoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(QuotaSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        TimeSpan? countdown = snapshot.Countdown;
        DateTimeOffset? lastSuccessfulSync = snapshot.HasSuccessfulSync ? snapshot.SyncedAt : null;
        FiveHour.Update(snapshot.FiveHour, snapshot.IsFresh, lastSuccessfulSync, countdown);
        Weekly.Update(snapshot.Weekly, snapshot.IsFresh, lastSuccessfulSync, snapshot.WeeklyCountdown);
        ConnectionStateText = FormatConnectionState(snapshot.ConnectionState, snapshot.ScopeLabel);

        if (ShouldActivate(snapshot))
        {
            _ = Task.Run(() => _activationCoordinator.TryActivateAsync(
                _accountIdentity,
                snapshot,
                new ActivationRequest(_isAutomationEnabled),
                _lifetimeCts.Token));
        }
    }

    private bool ShouldActivate(QuotaSnapshot snapshot)
    {
        return _isAutomationEnabled
            && !IsManualActivationRunning
            && snapshot.IsFresh
            && snapshot.FiveHour.IsAvailable
            && snapshot.FiveHour.UsedPercent == 0;
    }

    private void ToggleShowHide()
    {
        if (_isMainWindowVisible)
        {
            _shell.HideMainWindow();
        }
        else
        {
            _shell.ShowMainWindow();
        }
    }

    private void ToggleAutomation()
    {
        IsAutomationEnabled = !_isAutomationEnabled;
    }

    private async Task ForceRefreshAsync()
    {
        try
        {
            await _monitor.RefreshNowAsync(_lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    private async Task RunManualActivationAsync()
    {
        if (Interlocked.CompareExchange(ref _manualActivationGate, 1, 0) != 0)
        {
            return;
        }

        PublishManualActivationState("正在核验额度与安全状态…");

        try
        {
            await _monitor.RefreshNowAsync(_lifetimeCts.Token).ConfigureAwait(false);
            QuotaSnapshot? snapshot = _monitor.CurrentSnapshot;
            if (snapshot is null)
            {
                PublishManualActivationStatus("暂无可用额度数据");
                return;
            }

            ActivationResult result = await _activationCoordinator.TryActivateAsync(
                    _accountIdentity,
                    snapshot,
                    new ActivationRequest(
                        IsAutomationEnabled: _isAutomationEnabled,
                        IsUserInitiated: true),
                    _lifetimeCts.Token)
                .ConfigureAwait(false);

            PublishManualActivationStatus(FormatManualActivationResult(result));
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch
        {
            PublishManualActivationStatus("检查失败，请查看审计记录");
        }
        finally
        {
            Interlocked.Exchange(ref _manualActivationGate, 0);
            _dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(IsManualActivationRunning));
                OnPropertyChanged(nameof(ManualActivationButtonText));
                RelayCommand.RaiseCanExecuteChanged();
            });
        }
    }

    private void PublishManualActivationState(string status)
    {
        _dispatcher.Invoke(() =>
        {
            ManualActivationStatusText = status;
            OnPropertyChanged(nameof(IsManualActivationRunning));
            OnPropertyChanged(nameof(ManualActivationButtonText));
            RelayCommand.RaiseCanExecuteChanged();
        });
    }

    private void PublishManualActivationStatus(string status)
    {
        _dispatcher.Invoke(() => ManualActivationStatusText = status);
    }

    private static string FormatManualActivationResult(ActivationResult result) =>
        result.Outcome switch
        {
            ActivationOutcome.Succeeded => "已触发并确认",
            ActivationOutcome.NotEligible => "当前窗口无需触发",
            ActivationOutcome.Suppressed => "当前窗口无需触发",
            ActivationOutcome.ExternallySatisfied => "当前窗口无需触发",
            ActivationOutcome.NoModel => "未找到可用模型",
            ActivationOutcome.Ambiguous => "结果待确认，不会重试",
            _ => "检查失败，请查看审计记录",
        };

    private static string FormatConnectionState(MonitoringConnectionState state, string? scopeLabel)
    {
        string label = state switch
        {
            MonitoringConnectionState.Connected => "已同步",
            MonitoringConnectionState.Connecting => "连接中...",
            MonitoringConnectionState.Disconnected => "未连接",
            MonitoringConnectionState.AuthenticatingRequired => "需要认证",
            MonitoringConnectionState.Error => "连接错误",
            _ => state.ToString(),
        };

        return string.IsNullOrWhiteSpace(scopeLabel) ? label : $"{label} · {scopeLabel}";
    }
}
