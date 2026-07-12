using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using CodexUsageWidget.App.Services;
using CodexUsageWidget.App.ViewModels;
using CodexUsageWidget.App.Views;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Monitoring;
using CodexUsageWidget.Infrastructure.IO;
using CodexUsageWidget.Infrastructure.Logging;
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
                await _singleInstance.SignalExistingInstanceAsync(CancellationToken.None).ConfigureAwait(false);
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
        IQuotaSource quotaSource = new DesignQuotaSource();
        IActivationCoordinator activationCoordinator = new NoOpActivationCoordinator();

        _monitor = new QuotaMonitor(quotaSource, clock, delay);
        AccountIdentity identity = new("design@local.invalid", "design", "global");
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
                await _crashReportWriter.WriteAsync(e.Exception, CancellationToken.None).ConfigureAwait(false);
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
