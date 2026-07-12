using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Windows;

/// <summary>
/// Per-user Windows startup registration using the current user's Run registry key.
/// Does not require administrator rights.
/// </summary>
public sealed class StartupRegistration : IStartupRegistration
{
    private readonly string _appName;
    private readonly string _executablePath;
    private readonly IRunRegistryKey _runKey;

    public StartupRegistration(
        string appName,
        string executablePath,
        IRunRegistryKey runKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        _appName = appName;
        _executablePath = executablePath;
        _runKey = runKey ?? throw new ArgumentNullException(nameof(runKey));
    }

    /// <inheritdoc/>
    public bool IsRegistered => !string.IsNullOrEmpty(_runKey.GetValue(_appName));

    /// <inheritdoc/>
    public Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _runKey.SetValue(_appName, _executablePath);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _runKey.DeleteValue(_appName);
        return Task.CompletedTask;
    }
}
