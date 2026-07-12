using System.Windows.Forms;

namespace CodexUsageWidget.Infrastructure.Windows;

/// <summary>
/// Adapter that wraps a <see cref="NotifyIcon"/> behind the <see cref="INotifyIcon"/> seam.
/// </summary>
public sealed class NotifyIconAdapter : INotifyIcon
{
    private readonly NotifyIcon _notifyIcon;

    public NotifyIconAdapter(NotifyIcon notifyIcon)
    {
        _notifyIcon = notifyIcon ?? throw new ArgumentNullException(nameof(notifyIcon));
    }

    public bool Visible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    public string Text
    {
        get => _notifyIcon.Text;
        set => _notifyIcon.Text = value;
    }

    public System.Drawing.Icon? Icon
    {
        get => _notifyIcon.Icon;
        set => _notifyIcon.Icon = value;
    }

    public ContextMenuStrip? ContextMenuStrip
    {
        get => _notifyIcon.ContextMenuStrip;
        set => _notifyIcon.ContextMenuStrip = value;
    }

    public event EventHandler? DoubleClick
    {
        add => _notifyIcon.DoubleClick += value;
        remove => _notifyIcon.DoubleClick -= value;
    }

    public void ShowBalloonTip(int timeout, string title, string text, ToolTipIcon icon) => _notifyIcon.ShowBalloonTip(timeout, title, text, icon);

    public void Dispose() => _notifyIcon.Dispose();
}
