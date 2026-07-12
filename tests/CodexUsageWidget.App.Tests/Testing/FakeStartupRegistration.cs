using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class FakeStartupRegistration : IStartupRegistration
{
    public bool IsRegistered { get; private set; }
    public int RegisterCallCount { get; private set; }
    public int UnregisterCallCount { get; private set; }

    public Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        RegisterCallCount++;
        IsRegistered = true;
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
        UnregisterCallCount++;
        IsRegistered = false;
        return Task.CompletedTask;
    }
}
