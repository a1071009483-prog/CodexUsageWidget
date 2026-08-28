using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using CodexUsageWidget.App.Services;
using CodexUsageWidget.App.ViewModels;
using CodexUsageWidget.App.Views;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Monitoring;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using CodexUsageWidget.Infrastructure.IO;
using CodexUsageWidget.Infrastructure.Logging;
using CodexUsageWidget.Infrastructure.Persistence;
using CodexUsageWidget.Infrastructure.Security;
using CodexUsageWidget.Infrastructure.Settings;
using CodexUsageWidget.Infrastructure.Time;
using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.App;

/// <summary>
/// Application entry point for the Codex Usage Widget. Wires view models, tray,
/// startup registration, placement persistence, single-instance enforcement, and
/// the quota monitor.
/// </summary>
#pragma warning disable CA1001
public partial class App : System.Windows.Application
{
    private const string AppName = "CodexUsageWidget";

    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private TrayIconService? _trayIconService;
    private QuotaMonitor? _monitor;
    private SingleInstanceCoordinator? _singleInstance;
    private JsonSettingsStore? _settingsStore;
    private CrashReportWriter? _crashReportWriter;
    private AppServerSupervisor? _appServerSupervisor;
    private AppServerQuotaSource? _appServerQuotaSource;
    private ProtectedSaltStore? _saltStore;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        string executablePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? System.IO.Path.Combine(AppContext.BaseDirectory, $"{AppName}.exe");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string crashesDirectory = System.IO.Path.Combine(localAppData, AppName, "crashes");

        IAppFileSystem fileSystem = new LocalAppFileSystem();
        IClock clock = new SystemClock();
        _crashReportWriter = new CrashReportWriter(
            fileSystem,
            clock,
            crashesDirectory,
            AppName);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _singleInstance = new SingleInstanceCoordinator(AppName, new NullRedactingLog());
        if (!_singleInstance.TryAcquireInstance())
        {
            try
            {
                await _singleInstance.SignalExistingInstanceAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                // Best-effort bring-forward; shutdown either way.
            }

            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        IWindowPlacementService placementService = new WindowPlacementService(fileSystem);
        IStartupRegistration startupRegistration = new StartupRegistration(
            AppName,
            executablePath,
            new CurrentUserRunRegistryKey(AppName));

        _settingsStore = new JsonSettingsStore(fileSystem);
        WidgetSettings settings = await _settingsStore.LoadAsync().ConfigureAwait(true);

        INotifyIcon notifyIcon = new NotifyIconAdapter(new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = AppName,
        });

        TrayIconService trayIconService = new TrayIconService(notifyIcon, AppName);
        _trayIconService = trayIconService;

        IUserNotifier notifier = new WindowsNotificationService(notifyIcon);
        IDelay delay = new TaskDelay();
        (IQuotaSource quotaSource, IActivationCoordinator activationCoordinator, AccountIdentity identity, StartupEnvironmentStatus environment) =
            await CreateLiveServicesAsync(localAppData, clock, delay, notifier).ConfigureAwait(true);

        _monitor = new QuotaMonitor(
            quotaSource,
            clock,
            delay,
            pollInterval: TimeSpan.FromSeconds(30),
            staleThreshold: TimeSpan.FromSeconds(120));
        IDispatcher dispatcher = new WpfDispatcher();

        _mainWindow = new MainWindow
        {
            PlacementService = placementService,
        };

        IApplicationShell shell = new ApplicationShell(
            _mainWindow,
            () => new AuditViewModel(new DesignAuditStore(), dispatcher));

        _mainViewModel = new MainViewModel(
            _monitor,
            activationCoordinator,
            identity,
            startupRegistration,
            trayIconService,
            shell,
            dispatcher);

        // Apply persisted (or default) preferences before the tray menu is built.
        _mainViewModel.IsAutomationEnabled = settings.IsAutomationEnabled;
        _mainViewModel.StartWithWindows = settings.StartWithWindows;
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;

        _mainViewModel.ApplyStartupEnvironment(environment);

        trayIconService.Initialize(_mainViewModel);
        _mainWindow.DataContext = _mainViewModel;
        _mainWindow.Show();
        trayIconService.Show();

        _singleInstance.StartListening(
            async _ =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _mainViewModel?.SetMainWindowVisible(true);
                    _mainWindow?.Show();
                    if (_mainWindow?.WindowState == WindowState.Minimized)
                    {
                        _mainWindow.WindowState = WindowState.Normal;
                    }

                    _mainWindow?.Activate();
                });
            },
            CancellationToken.None);

        try
        {
            await _mainViewModel.StartAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when startup is cancelled.
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);

        if (_mainViewModel is not null)
        {
            _mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        }

        if (_mainWindow is not null)
        {
            try
            {
                await _mainWindow.SavePlacementAsync().ConfigureAwait(true);
            }
            catch
            {
                // Non-fatal on exit.
            }
        }

        _mainViewModel?.Dispose();
        _trayIconService?.Dispose();
        _singleInstance?.Dispose();

        if (_monitor is IAsyncDisposable monitorAsync)
        {
            try
            {
                await monitorAsync.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Non-fatal on exit.
            }
        }

        _appServerQuotaSource?.Dispose();

        if (_appServerSupervisor is not null)
        {
            try
            {
                await _appServerSupervisor.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Non-fatal on exit.
            }
        }

        _saltStore?.Dispose();
    }

    private async Task<(IQuotaSource QuotaSource, IActivationCoordinator ActivationCoordinator, AccountIdentity Identity, StartupEnvironmentStatus Environment)> CreateLiveServicesAsync(
        string localAppData,
        IClock clock,
        IDelay delay,
        IUserNotifier notifier)
    {
        string dataDirectory = System.IO.Path.Combine(localAppData, AppName, "Data");
        string appServerWorkingDirectory = System.IO.Path.Combine(dataDirectory, "app-server");
        Directory.CreateDirectory(appServerWorkingDirectory);

        AccountIdentity fallbackIdentity = new("design@local.invalid", "design", "global");
        string widgetVersion = ApplicationVersion.Current;
        string windowsVersion = Environment.OSVersion.VersionString;

        StartupEnvironmentStatus Blocked(StartupEnvironmentKind kind, string message, string? cliVersion) =>
            new(kind, message, widgetVersion, cliVersion, windowsVersion, CanActivate: false);

        CodexExecutableResolution resolution = CodexExecutableLocator.CreateSystem().Locate();
        if (!resolution.Found)
        {
            return (new DesignQuotaSource(), new NoOpActivationCoordinator(), fallbackIdentity,
                Blocked(
                    StartupEnvironmentKind.CodexCliMissing,
                    "未找到 Codex CLI。请先安装 Codex CLI，然后运行 codex login。",
                    null));
        }

        using var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Best-effort CLI version probe for diagnostics and acceptance evidence.
        string? cliVersionText = null;
        try
        {
            CodexCliVersionResult cliVersion = await new CodexCliVersionProbe(new SystemProcessHost())
                .GetVersionAsync(resolution.Command!, startupCts.Token).ConfigureAwait(true);
            cliVersionText = cliVersion.Version;
        }
        catch (OperationCanceledException)
        {
            // Version probing is diagnostic-only; startup continues without it.
        }

        string? incompatibility = null;

        try
        {
            UsageStateDatabase database = new(dataDirectory);
            IProtectedData protectedData = new DpapiProtectedData();
            _saltStore = new ProtectedSaltStore(dataDirectory, protectedData);
            IAccountNamespaceHasher namespaceHasher = new AccountNamespaceHasher(_saltStore);
            IAuditStore auditStore = new SqliteAuditStore(database);
            IActivationLockStore lockStore = new ActivationLockStore(database);
            ICleanupWorkStore cleanupStore = new SqliteCleanupWorkStore(database);

            ClientInformation clientInformation = new(
                "codex-usage-widget",
                ApplicationVersion.Current,
                "Codex Usage Widget");
            ProcessStartRequest startRequest = new(
                resolution.Command!,
                ["app-server"],
                appServerWorkingDirectory);

            Func<CancellationToken, Task<AppServerCapabilityResult>> capabilityPreflight =
                AppServerCapabilityPreflight.ForProcess(
                    new SystemProcessHost(),
                    startRequest,
                    clientInformation,
                    TimeSpan.FromSeconds(5),
                    delay,
                    new NullRedactingLog());

            _appServerSupervisor = new AppServerSupervisor(
                new SystemProcessHost(),
                startRequest,
                clientInformation,
                TimeSpan.FromSeconds(5),
                delay,
                AppServerSupervisorSettings.Default,
                healthyDelay: delay,
                graceDelay: delay,
                log: new NullRedactingLog(),
                capabilityPreflight: capabilityPreflight);

            _appServerQuotaSource = new AppServerQuotaSource(_appServerSupervisor);
            IModelCatalog modelCatalog = new AppServerModelCatalog(_appServerSupervisor);
            IModelBoundary modelBoundary = new CurrentGenerationModelBoundary(_appServerSupervisor);

            ActivationCoordinator coordinator = new(
                lockStore,
                modelCatalog,
                modelBoundary,
                _appServerQuotaSource,
                auditStore,
                cleanupStore,
                namespaceHasher,
                notifier,
                clock,
                delay,
                new ActivationCoordinatorOptions
                {
                    IsAutomationEnabled = true,
                    WorkingDirectory = appServerWorkingDirectory,
                });

            var readyTcs = new TaskCompletionSource();
            EventHandler<AppServerSupervisorEventArgs> onReady = (_, _) => readyTcs.TrySetResult();
            EventHandler<AppServerIncompatibleEventArgs> onIncompatible = (_, args) =>
            {
                incompatibility =
                    $"The Codex App Server is missing required methods: {string.Join(", ", args.MissingMethods)}.";
                readyTcs.TrySetException(new InvalidOperationException(incompatibility));
            };
            _appServerSupervisor.SessionPublished += onReady;
            _appServerSupervisor.IncompatibleDetected += onIncompatible;
            try
            {
                _ = _appServerSupervisor.StartAsync(startupCts.Token);
                await readyTcs.Task.WaitAsync(startupCts.Token).ConfigureAwait(true);
            }
            finally
            {
                _appServerSupervisor.SessionPublished -= onReady;
                _appServerSupervisor.IncompatibleDetected -= onIncompatible;
            }

            AppServerGenerationSession? generation = _appServerSupervisor.CurrentGeneration;
            if (generation is null)
            {
                throw new InvalidOperationException("The App Server session is not available.");
            }

            AccountReadResponse accountResponse = await generation.Session.Gateway
                .ReadAccountAsync(refreshToken: false, startupCts.Token).ConfigureAwait(true);
            AuthenticationAssessment assessment = new AccountAuthenticationEvaluator().Evaluate(accountResponse);

            if (assessment.State == AuthenticationState.Required)
            {
                await DisposeLiveServicesAsync().ConfigureAwait(false);
                return (new DesignQuotaSource(), new NoOpActivationCoordinator(), fallbackIdentity,
                    Blocked(
                        StartupEnvironmentKind.AuthenticationRequired,
                        "Codex 尚未登录。请在终端运行 codex login，然后重新连接。",
                        cliVersionText));
            }

            if (assessment.State == AuthenticationState.Unsupported)
            {
                await DisposeLiveServicesAsync().ConfigureAwait(false);
                return (new DesignQuotaSource(), new NoOpActivationCoordinator(), fallbackIdentity,
                    Blocked(
                        StartupEnvironmentKind.UnsupportedAuthentication,
                        "需要使用 ChatGPT 账号登录 Codex；仅 API Key 的认证方式暂不支持。",
                        cliVersionText));
            }

            AccountIdentity identity = new(
                assessment.IdentityMaterial ?? string.Empty,
                assessment.PlanType,
                assessment.WorkspaceIdentity);

            return (_appServerQuotaSource, coordinator, identity,
                new StartupEnvironmentStatus(
                    StartupEnvironmentKind.Ready,
                    string.Empty,
                    widgetVersion,
                    cliVersionText,
                    windowsVersion,
                    CanActivate: true));
        }
        catch
        {
            await DisposeLiveServicesAsync().ConfigureAwait(false);

            StartupEnvironmentKind kind = incompatibility is not null
                ? StartupEnvironmentKind.AppServerIncompatible
                : StartupEnvironmentKind.StartupError;
            string message = incompatibility is not null
                ? "当前 Codex CLI 与 Codex Usage Widget 的 App Server 协议不兼容。"
                : "Codex Usage Widget 启动时发生错误。请确认 Codex CLI 可用后重新启动应用。";

            return (new DesignQuotaSource(), new NoOpActivationCoordinator(), fallbackIdentity,
                Blocked(kind, message, cliVersionText));
        }
    }

    private async Task DisposeLiveServicesAsync()
    {
        _appServerQuotaSource?.Dispose();
        _appServerQuotaSource = null;

        if (_appServerSupervisor is not null)
        {
            try
            {
                await _appServerSupervisor.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            _appServerSupervisor = null;
        }

        _saltStore?.Dispose();
        _saltStore = null;
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_mainViewModel is null || _settingsStore is null)
        {
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.IsAutomationEnabled)
            or nameof(MainViewModel.StartWithWindows))
        {
            WidgetSettings updated = new(
                _mainViewModel.StartWithWindows,
                _mainViewModel.IsAutomationEnabled);
            _ = _settingsStore.SaveAsync(updated);
        }
    }

    private async void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_crashReportWriter is not null)
        {
            try
            {
                await _crashReportWriter.WriteAsync(e.Exception, CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                // Non-fatal: the original exception is already unhandled.
            }
        }

        Shutdown();
    }
}
#pragma warning restore CA1001
