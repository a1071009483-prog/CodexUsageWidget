using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.Core.Monitoring;

/// <summary>
/// Polls an <see cref="IQuotaSource"/> on a fixed interval, reacts to push notifications,
/// republishes a local countdown every second, and tracks freshness/connection state.
/// </summary>
public sealed class QuotaMonitor : IAsyncDisposable
{
    private readonly IQuotaSource _source;
    private readonly IClock _clock;
    private readonly IDelay _delay;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _staleThreshold;
    private readonly TimeSpan _notificationDebounce;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _currentDelayCts;
    private Task? _runTask;
    private QuotaSnapshot? _currentSnapshot;
    private DateTimeOffset _nextReadAt;
    private TimeSpan _backoff;
    private bool _notificationPending;
    private bool _disposed;
    private TaskCompletionSource? _loopStartedTcs;
    private readonly SemaphoreSlim _readSemaphore = new(1, 1);
    private TaskCompletionSource? _refreshCompletion;
    private DateTimeOffset _refreshOriginalNextReadAt;
    private bool _refreshPending;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuotaMonitor"/> class.
    /// </summary>
    /// <param name="source">The quota source to monitor.</param>
    /// <param name="clock">The clock used for all time measurements.</param>
    /// <param name="delay">The delay abstraction used for scheduling.</param>
    /// <param name="pollInterval">Optional interval between reconciliation reads. Defaults to 60 seconds.</param>
    /// <param name="staleThreshold">Optional age after which data is considered stale. Defaults to 120 seconds.</param>
    /// <param name="notificationDebounce">Optional debounce applied after a push notification. Defaults to zero.</param>
    public QuotaMonitor(
        IQuotaSource source,
        IClock clock,
        IDelay delay,
        TimeSpan? pollInterval = null,
        TimeSpan? staleThreshold = null,
        TimeSpan? notificationDebounce = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(delay);

        _source = source;
        _clock = clock;
        _delay = delay;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(60);
        _staleThreshold = staleThreshold ?? TimeSpan.FromSeconds(120);
        _notificationDebounce = notificationDebounce ?? TimeSpan.Zero;
        _nextReadAt = _clock.UtcNow;
    }

    /// <summary>
    /// Raised whenever the current snapshot changes, including countdown ticks and state transitions.
    /// </summary>
    public event EventHandler<QuotaSnapshot>? SnapshotChanged;

    /// <summary>
    /// Gets the most recently published snapshot, or <c>null</c> before the first sync.
    /// </summary>
    public QuotaSnapshot? CurrentSnapshot
    {
        get
        {
            lock (_lock)
            {
                return _currentSnapshot;
            }
        }
    }

    /// <summary>
    /// Starts the monitor and performs an immediate read.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can stop startup.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cts is not null)
        {
            throw new InvalidOperationException("The monitor has already been started.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _source.Updated += OnSourceUpdated;

        try
        {
            await ReadAndPublishAsync(ReadReason.Startup, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // ReadAndPublishAsync publishes a failure snapshot; StartupAsync should not throw.
        }

        _runTask = RunLoopAsync(_cts.Token);
        await _loopStartedTcs.Task.WaitAsync(_cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Forces an immediate quota refresh without stopping the background loop.
    /// The next scheduled poll remains aligned with the original cadence.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can cancel the refresh wait.</param>
    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cts is null)
        {
            throw new InvalidOperationException("The monitor has not been started.");
        }

        CancellationTokenSource? delayCts;
        TaskCompletionSource? completion;

        lock (_lock)
        {
            if (_refreshPending)
            {
                completion = _refreshCompletion;
                delayCts = _currentDelayCts;
            }
            else
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _refreshCompletion = completion;
                _refreshPending = true;
                _refreshOriginalNextReadAt = _nextReadAt;
                _nextReadAt = _clock.UtcNow;
                _notificationPending = false;
                delayCts = _currentDelayCts;
            }
        }

        delayCts?.Cancel();

        try
        {
            await _readSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_lock)
            {
                _refreshPending = false;
                _refreshCompletion = null;
            }

            completion?.TrySetCanceled(cancellationToken);
            throw;
        }

        try
        {
            lock (_lock)
            {
                if (!_refreshPending)
                {
                    // The background loop served this refresh while we were waiting for the semaphore.
                    return;
                }
            }

            await ReadAndPublishAsync(ReadReason.Refresh, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _readSemaphore.Release();
        }
    }

    /// <summary>
    /// Stops the monitor and waits for the background loop to exit.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can stop waiting for shutdown.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        _source.Updated -= OnSourceUpdated;

        Task? runTask = _runTask;
        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the monitor is stopped.
            }
        }

        _cts.Dispose();
        _cts = null;
        _runTask = null;
    }

    /// <summary>
    /// Disposes the monitor by stopping it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan delay = ComputeNextDelay();

            if (delay > TimeSpan.Zero)
            {
                CancellationTokenSource delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                lock (_lock)
                {
                    _currentDelayCts = delayCts;
                }

                Task delayTask = _delay.DelayAsync(delay, delayCts.Token);
                _loopStartedTcs?.TrySetResult();

                try
                {
                    await delayTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    // A notification cancelled the delay so the loop can react immediately.
                }
                finally
                {
                    lock (_lock)
                    {
                        _currentDelayCts = null;
                    }

                    delayCts.Dispose();
                }
            }
            else
            {
                _loopStartedTcs?.TrySetResult();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await _readSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                bool readForNotification;
                bool readForPoll;

                lock (_lock)
                {
                    readForNotification = _notificationPending;

                    if (readForNotification)
                    {
                        _notificationPending = false;

                        if (_notificationDebounce > TimeSpan.Zero)
                        {
                            _nextReadAt = _clock.UtcNow + _notificationDebounce;
                            continue;
                        }
                    }

                    readForPoll = _clock.UtcNow >= _nextReadAt;
                }

                if (readForNotification)
                {
                    await ReadAndPublishAsync(ReadReason.Notification, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (readForPoll)
                {
                    await ReadAndPublishAsync(ReadReason.Poll, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                RepublishCountdown();
            }
            finally
            {
                _readSemaphore.Release();
            }
        }
    }

    private TimeSpan ComputeNextDelay()
    {
        DateTimeOffset now = _clock.UtcNow;
        TimeSpan untilRead = _nextReadAt > now ? _nextReadAt - now : TimeSpan.Zero;
        TimeSpan oneSecond = TimeSpan.FromSeconds(1);
        return untilRead < oneSecond ? untilRead : oneSecond;
    }

    private async Task ReadAndPublishAsync(ReadReason reason, CancellationToken cancellationToken)
    {
        QuotaSnapshot? previous;
        QuotaSnapshot snapshot;

        lock (_lock)
        {
            previous = _currentSnapshot;
        }

        try
        {
            QuotaSourceResult result = await _source.ReadAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess && result.Snapshot is not null)
            {
                DateTimeOffset syncedAt = _clock.UtcNow;
                snapshot = QuotaNormalizer.Normalize(result.Snapshot, syncedAt, MonitoringConnectionState.Connected);
                snapshot = snapshot with
                {
                    Countdown = ComputeCountdown(snapshot.FiveHour),
                    WeeklyCountdown = ComputeCountdown(snapshot.Weekly),
                };

                lock (_lock)
                {
                    _backoff = TimeSpan.Zero;
                    _nextReadAt = syncedAt + _pollInterval;

                    if (_refreshPending && _refreshOriginalNextReadAt > _clock.UtcNow)
                    {
                        _nextReadAt = _refreshOriginalNextReadAt;
                    }
                }
            }
            else
            {
                snapshot = BuildFailureSnapshot(previous, MonitoringConnectionState.Error);
                ScheduleRetry();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            snapshot = BuildFailureSnapshot(previous, MonitoringConnectionState.Error);
            ScheduleRetry();
        }

        PublishSnapshot(snapshot);

        TaskCompletionSource? refreshCompletion;

        lock (_lock)
        {
            _refreshPending = false;
            refreshCompletion = _refreshCompletion;
            _refreshCompletion = null;
        }

        refreshCompletion?.TrySetResult();
    }

    private QuotaSnapshot BuildFailureSnapshot(QuotaSnapshot? previous, MonitoringConnectionState state)
    {
        DateTimeOffset now = _clock.UtcNow;

        if (previous is not null)
        {
            return previous with
            {
                IsFresh = false,
                ConnectionState = state,
                Countdown = ComputeCountdown(previous.FiveHour),
                WeeklyCountdown = ComputeCountdown(previous.Weekly),
            };
        }

        return new QuotaSnapshot(
            null,
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, 0, 0, null, null, false),
            new QuotaBucketSnapshot(QuotaBucket.Weekly, 0, 0, null, null, false),
            now,
            false,
            state,
            null,
            null,
            HasSuccessfulSync: false);
    }

    private void ScheduleRetry()
    {
        lock (_lock)
        {
            if (_backoff == TimeSpan.Zero)
            {
                _backoff = TimeSpan.FromSeconds(1);
            }
            else
            {
                long doubled = _backoff.Ticks * 2;
                long max = TimeSpan.FromSeconds(30).Ticks;
                _backoff = TimeSpan.FromTicks(doubled > max ? max : doubled);
            }

            _nextReadAt = _clock.UtcNow + _backoff;
        }
    }

    private void RepublishCountdown()
    {
        QuotaSnapshot? current;

        lock (_lock)
        {
            current = _currentSnapshot;
        }

        if (current is null)
        {
            return;
        }

        DateTimeOffset now = _clock.UtcNow;
        bool isFresh = current.ConnectionState == MonitoringConnectionState.Connected
            && now - current.SyncedAt < _staleThreshold;

        QuotaSnapshot updated = current with
        {
            IsFresh = isFresh,
            Countdown = ComputeCountdown(current.FiveHour),
            WeeklyCountdown = ComputeCountdown(current.Weekly),
        };

        PublishSnapshot(updated);
    }

    private void PublishSnapshot(QuotaSnapshot snapshot)
    {
        QuotaSnapshot? previous;

        lock (_lock)
        {
            previous = _currentSnapshot;
            _currentSnapshot = snapshot;
        }

        if (previous != snapshot)
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private TimeSpan? ComputeCountdown(QuotaBucketSnapshot bucket)
    {
        if (!bucket.IsAvailable || bucket.ResetsAt is null)
        {
            return null;
        }

        TimeSpan remaining = bucket.ResetsAt.Value - _clock.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void OnSourceUpdated(object? _, EventArgs e)
    {
        CancellationTokenSource? delayCts;

        lock (_lock)
        {
            _notificationPending = true;
            delayCts = _currentDelayCts;
        }

        try
        {
            delayCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The delay token source was disposed between the read and the cancel call.
        }
    }

    private enum ReadReason
    {
        Startup,
        Poll,
        Notification,
        Refresh,
    }
}
