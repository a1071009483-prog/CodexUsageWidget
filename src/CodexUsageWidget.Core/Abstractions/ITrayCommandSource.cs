namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Command and state surface exposed by the main view model for tray binding.
/// Commands are framework-agnostic so that Core does not depend on WPF.
/// </summary>
public interface ITrayCommandSource
{
    /// <summary>Text for the Show/Hide tray menu item.</summary>
    string ShowHideHeader { get; }

    /// <summary>Text for the Pause/Resume tray menu item.</summary>
    string PauseResumeHeader { get; }

    /// <summary>Toggles main window visibility.</summary>
    ITrayCommand ShowHideCommand { get; }

    /// <summary>Forces an immediate quota refresh.</summary>
    ITrayCommand RefreshNowCommand { get; }

    /// <summary>Toggles automatic activation.</summary>
    ITrayCommand ToggleAutomationCommand { get; }

    /// <summary>Opens the audit window.</summary>
    ITrayCommand OpenAuditCommand { get; }

    /// <summary>Reconnects the quota monitor.</summary>
    ITrayCommand ReconnectCommand { get; }

    /// <summary>Exits the application.</summary>
    ITrayCommand ExitCommand { get; }

    /// <summary>Whether the application is registered to start with Windows.</summary>
    bool StartWithWindows { get; set; }
}
