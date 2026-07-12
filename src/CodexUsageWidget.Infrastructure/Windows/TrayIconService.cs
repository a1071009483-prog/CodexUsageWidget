using System.Windows.Forms;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Windows;

/// <summary>
/// Resident system-tray icon and context menu. Context-menu commands are bound to the
/// <see cref="ITrayCommandSource"/> supplied at initialization.
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private readonly INotifyIcon _icon;
    private readonly string _applicationName;
    private ITrayCommandSource? _commandSource;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _showHideItem;
    private ToolStripMenuItem? _refreshItem;
    private ToolStripMenuItem? _pauseItem;
    private ToolStripMenuItem? _startWithWindowsItem;
    private ToolStripMenuItem? _auditItem;
    private ToolStripMenuItem? _reconnectItem;
    private ToolStripMenuItem? _exitItem;
    private bool _disposed;

    public TrayIconService(INotifyIcon icon, string applicationName)
    {
        _icon = icon ?? throw new ArgumentNullException(nameof(icon));
        _applicationName = applicationName ?? throw new ArgumentNullException(nameof(applicationName));
    }

    /// <inheritdoc/>
    public void Initialize(ITrayCommandSource commandSource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commandSource);

        _commandSource = commandSource;
        _menu = BuildMenu(commandSource);
        _icon.ContextMenuStrip = _menu;
        _icon.Text = _applicationName;
        _icon.DoubleClick += OnDoubleClick;
    }

    /// <inheritdoc/>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _icon.Visible = true;
    }

    /// <inheritdoc/>
    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _icon.Visible = false;
    }

    /// <inheritdoc/>
    public void SetPauseResumeLabel(string label)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pauseItem is not null)
        {
            _pauseItem.Text = label;
        }
    }

    /// <inheritdoc/>
    public void SetShowHideLabel(string label)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_showHideItem is not null)
        {
            _showHideItem.Text = label;
        }
    }

    /// <inheritdoc/>
    public void SetStartWithWindowsChecked(bool isChecked)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_startWithWindowsItem is not null)
        {
            _startWithWindowsItem.Checked = isChecked;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.DoubleClick -= OnDoubleClick;
        _icon.Dispose();
        _menu?.Dispose();
    }

    private ContextMenuStrip BuildMenu(ITrayCommandSource source)
    {
        _showHideItem = CreateCommandItem(source.ShowHideHeader, source.ShowHideCommand);
        _refreshItem = CreateCommandItem("立即刷新", source.RefreshNowCommand);
        _pauseItem = CreateCommandItem(source.PauseResumeHeader, source.ToggleAutomationCommand);
        _startWithWindowsItem = new ToolStripMenuItem("开机启动")
        {
            Checked = source.StartWithWindows,
            CheckOnClick = true,
        };
        _startWithWindowsItem.Click += (_, _) =>
        {
            if (_commandSource is not null)
            {
                _commandSource.StartWithWindows = _startWithWindowsItem.Checked;
            }
        };

        _auditItem = CreateCommandItem("审计日志", source.OpenAuditCommand);
        _reconnectItem = CreateCommandItem("重新连接", source.ReconnectCommand);
        _exitItem = CreateCommandItem("退出", source.ExitCommand);

        ContextMenuStrip menu = new();
        menu.Items.Add(_showHideItem);
        menu.Items.Add(_refreshItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startWithWindowsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_auditItem);
        menu.Items.Add(_reconnectItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);
        return menu;
    }

    private ToolStripMenuItem CreateCommandItem(string header, ITrayCommand command)
    {
        ToolStripMenuItem item = new(header);
        item.Click += (_, _) =>
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        };

        void UpdateEnabled(object? _, EventArgs __) => item.Enabled = command.CanExecute(null);

        command.CanExecuteChanged += UpdateEnabled;
        UpdateEnabled(null, EventArgs.Empty);
        return item;
    }

    private void OnDoubleClick(object? sender, EventArgs e)
    {
        ITrayCommand? command = _commandSource?.ShowHideCommand;
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
