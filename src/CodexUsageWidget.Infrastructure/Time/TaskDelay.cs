using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Time;

/// <summary>
/// Production implementation of <see cref="IDelay"/> that uses <see cref="Task.Delay"/>.
/// </summary>
public sealed class TaskDelay : IDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
