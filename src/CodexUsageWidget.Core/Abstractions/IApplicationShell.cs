namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Framework-agnostic surface for controlling the application shell from view models.
/// </summary>
public interface IApplicationShell
{
    /// <summary>Shows or brings forward the main widget window.</summary>
    void ShowMainWindow();

    /// <summary>Hides the main widget window without exiting the application.</summary>
    void HideMainWindow();

    /// <summary>Opens the local redacted audit log window.</summary>
    void OpenAuditWindow();

    /// <summary>Shuts down the application.</summary>
    void Shutdown();
}
