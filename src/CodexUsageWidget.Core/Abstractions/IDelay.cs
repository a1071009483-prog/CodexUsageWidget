namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Schedules cancellable waits without binding domain code to wall-clock time.
/// </summary>
public interface IDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
