namespace CodexUsageWidget.Infrastructure.Windows;

/// <summary>
/// Testable seam around a system-tray notify icon.
/// </summary>
public interface INotifyIcon : IDisposable
{
    /// <summary>Gets or sets whether the icon is visible in the tray.</summary>
    bool Visible { get; set; }

    /// <summary>Gets or sets the tooltip text.</summary>
    string Text { get; set; }

    /// <summary>Gets or sets the icon image.</summary>
    System.Drawing.Icon? Icon { get; set; }

    /// <summary>Gets or sets the context menu.</summary>
    System.Windows.Forms.ContextMenuStrip? ContextMenuStrip { get; set; }

    /// <summary>Raised when the icon is double-clicked.</summary>
    event EventHandler? DoubleClick;

    /// <summary>Displays a balloon tip.</summary>
    void ShowBalloonTip(int timeout, string title, string text, System.Windows.Forms.ToolTipIcon icon);
}
