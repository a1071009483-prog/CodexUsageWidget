namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Registers or unregisters the application to start with the current user's Windows session.
/// Implementations must not require administrator rights.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>Whether the application is currently registered to start with Windows.</summary>
    bool IsRegistered { get; }

    /// <summary>Registers the application for the current user.</summary>
    Task RegisterAsync(CancellationToken cancellationToken = default);

    /// <summary>Unregisters the application for the current user.</summary>
    Task UnregisterAsync(CancellationToken cancellationToken = default);
}
