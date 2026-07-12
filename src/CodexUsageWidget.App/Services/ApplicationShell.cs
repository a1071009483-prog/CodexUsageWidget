using System.Windows;
using CodexUsageWidget.App.ViewModels;
using CodexUsageWidget.App.Views;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Services;

/// <summary>
/// Application shell that controls the main widget and audit windows.
/// </summary>
public sealed class ApplicationShell : IApplicationShell
{
    private readonly MainWindow _mainWindow;
    private readonly Func<AuditViewModel> _auditViewModelFactory;
    private AuditWindow? _auditWindow;

    public ApplicationShell(MainWindow mainWindow, Func<AuditViewModel> auditViewModelFactory)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _auditViewModelFactory = auditViewModelFactory ?? throw new ArgumentNullException(nameof(auditViewModelFactory));

        _mainWindow.IsVisibleChanged += (_, _) =>
        {
            if (_mainWindow.DataContext is MainViewModel viewModel)
            {
                viewModel.SetMainWindowVisible(_mainWindow.IsVisible);
            }
        };
    }

    public void ShowMainWindow()
    {
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    public void HideMainWindow()
    {
        _mainWindow.Hide();
    }

    public void OpenAuditWindow()
    {
        if (_auditWindow is null)
        {
            _auditWindow = new AuditWindow
            {
                DataContext = _auditViewModelFactory(),
                Owner = _mainWindow,
            };
            _auditWindow.Closed += (_, _) => _auditWindow = null;
        }

        _auditWindow.Show();
        _auditWindow.Activate();
    }

    public void Shutdown()
    {
        _mainWindow.IsShuttingDown = true;
        System.Windows.Application.Current?.Shutdown();
    }
}
