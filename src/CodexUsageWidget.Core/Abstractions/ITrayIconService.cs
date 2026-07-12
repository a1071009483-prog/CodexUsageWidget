namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Manages the resident system-tray icon and its context menu.
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>
    /// Creates the tray icon and binds its commands to the supplied source.
    /// </summary>
    void Initialize(ITrayCommandSource commandSource);

    /// <summary>Shows the tray icon.</summary>
    void Show();

    /// <summary>Hides the tray icon.</summary>
    void Hide();

    /// <summary>Updates the Pause/Resume menu label.</summary>
    void SetPauseResumeLabel(string label);

    /// <summary>Updates the Show/Hide menu label.</summary>
    void SetShowHideLabel(string label);

    /// <summary>Updates the checked state of the Start with Windows item.</summary>
    void SetStartWithWindowsChecked(bool isChecked);
}
