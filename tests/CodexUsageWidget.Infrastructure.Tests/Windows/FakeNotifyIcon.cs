using System.Windows.Forms;
using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Infrastructure.Tests.Windows;

internal sealed class FakeNotifyIcon : INotifyIcon
{
    private readonly List<BalloonTipCall> _balloons = new();

    public bool Visible { get; set; }
    public string Text { get; set; } = string.Empty;
    public System.Drawing.Icon? Icon { get; set; }
    public ContextMenuStrip? ContextMenuStrip { get; set; }

    public IReadOnlyList<BalloonTipCall> Balloons => _balloons;

    public event EventHandler? DoubleClick;

    public void ShowBalloonTip(int timeout, string title, string text, ToolTipIcon icon) =>
        _balloons.Add(new BalloonTipCall(timeout, title, text, icon));

    public void RaiseDoubleClick() => DoubleClick?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}

internal sealed record BalloonTipCall(int Timeout, string Title, string Text, ToolTipIcon Icon);
