namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Coordinates a single application instance per Windows user. The first process
/// acquires ownership and listens for bring-forward signals; later processes
/// detect the existing instance, signal it, and exit without creating duplicate
/// UI or background work.
/// </summary>
public interface ISingleInstanceCoordinator
{
    /// <summary>
    /// Attempts to acquire the single-instance mutex for the current user.
    /// Returns <c>true</c> if this process is the first instance and now owns
    /// the mutex; returns <c>false</c> if another instance is already running.
    /// </summary>
    bool TryAcquireInstance();

    /// <summary>
    /// Starts a background listener that waits for signals from later instances.
    /// Only the first-instance owner should call this method. The callback is
    /// invoked on a thread-pool thread whenever a signal arrives.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this instance does not own the single-instance mutex.
    /// </exception>
    void StartListening(
        Func<CancellationToken, Task> onBringForward,
        CancellationToken cancellationToken);

    /// <summary>
    /// Connects to the existing instance and asks it to bring its window forward.
    /// Only valid when <see cref="TryAcquireInstance"/> returned <c>false</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this instance owns the single-instance mutex.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the existing instance is not listening on the named pipe.
    /// </exception>
    Task SignalExistingInstanceAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Releases the single-instance mutex and stops the background listener.
    /// Safe to call multiple times.
    /// </summary>
    void ReleaseInstance();
}
