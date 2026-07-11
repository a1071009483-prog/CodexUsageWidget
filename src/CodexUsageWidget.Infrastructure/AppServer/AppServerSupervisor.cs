using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

/// <summary>
/// Restart/backoff/healthy-reset tuning for <see cref="AppServerSupervisor"/>.
/// </summary>
public sealed record AppServerSupervisorSettings(
    TimeSpan InitialDelay,
    TimeSpan MaximumDelay,
    TimeSpan HealthyResetInterval)
{
    public static AppServerSupervisorSettings Default { get; } = new(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60));
}

/// <summary>
/// A published App Server session tagged with the supervisor generation that owns it.
/// Consumers compare <see cref="GenerationId"/> before acting on a forwarded event so a
/// retired generation's late frames cannot be mistaken for current work.
/// </summary>
public sealed record AppServerGenerationSession(AppServerSession Session, long GenerationId);

public sealed class AppServerSupervisorEventArgs(AppServerGenerationSession generation) : EventArgs
{
    public AppServerGenerationSession Generation { get; } = generation;
}

/// <summary>
/// Owns successive one-generation <see cref="AppServerProcess"/> instances with capped
/// backoff, current-generation notification forwarding, and retired-generation filtering.
/// It never replays pending requests across a disconnect and never invokes generation
/// methods (<c>thread/start</c>/<c>turn/start</c>); it only hosts the connection and
/// forwards rate-limit notifications from the current generation.
/// </summary>
public sealed class AppServerSupervisor : IAsyncDisposable
{
    private static readonly Task NeverCompletingTask =
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

    private readonly IProcessHost _processHost;
    private readonly ProcessStartRequest _startRequest;
    private readonly ClientInformation _clientInformation;
    private readonly TimeSpan _gracefulStopDelay;
    private readonly IDelay _backoffDelay;
    private readonly IDelay _healthyDelay;
    private readonly IDelay _graceDelay;
    private readonly AppServerSupervisorSettings _settings;
    private readonly IRedactingLog? _log;

    private readonly CancellationTokenSource _stopCts = new();
    private readonly object _currentLock = new();

    private int _started;
    private int _stopped;
    private int _disposed;
    private long _generation;
    private long _currentGeneration;
    private AppServerGenerationSession? _current;
    private CodexAppServerGateway? _currentGateway;
    private EventHandler<RateLimitsUpdatedEventArgs>? _currentForwardHandler;
    private int _backoffStep;
    private Task? _runTask;
    private CancellationTokenSource? _runLinkedCts;

    public AppServerSupervisor(
        IProcessHost processHost,
        ProcessStartRequest startRequest,
        ClientInformation clientInformation,
        TimeSpan gracefulStopDelay,
        IDelay backoffDelay,
        AppServerSupervisorSettings? settings = null,
        IDelay? healthyDelay = null,
        IDelay? graceDelay = null,
        IRedactingLog? log = null)
    {
        _processHost = processHost ?? throw new ArgumentNullException(nameof(processHost));
        _startRequest = startRequest ?? throw new ArgumentNullException(nameof(startRequest));
        _clientInformation = clientInformation ?? throw new ArgumentNullException(nameof(clientInformation));
        _gracefulStopDelay = gracefulStopDelay;
        _backoffDelay = backoffDelay ?? throw new ArgumentNullException(nameof(backoffDelay));
        _settings = settings ?? AppServerSupervisorSettings.Default;
        _healthyDelay = healthyDelay ?? backoffDelay;
        _graceDelay = graceDelay ?? new SystemDelay();
        _log = log;
    }

    public event EventHandler<AppServerSupervisorEventArgs>? SessionPublished;

    /// <summary>
    /// Raised when the current generation survives the configured healthy-reset
    /// interval without faulting. At that point the supervisor considers the
    /// generation stable and resets its restart backoff. This is a lifecycle
    /// signal distinct from handshake completion: a child that exits immediately
    /// after <c>initialize</c> never reaches it.
    /// </summary>
    public event EventHandler<AppServerSupervisorEventArgs>? GenerationConfirmedHealthy;

    public event EventHandler<RateLimitsUpdatedEventArgs>? RateLimitsUpdated;

    public AppServerGenerationSession? CurrentGeneration
    {
        get
        {
            lock (_currentLock)
            {
                return _current;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The supervisor has already been started.");
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Volatile.Write(ref _stopped, 0);
        _runLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
        _runTask = RunAsync(_runLinkedCts.Token);
        return _runTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopped, 1);
        _stopCts.Cancel();
        Task? runTask = _runTask;
        if (runTask is not null)
        {
            try
            {
                await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The run loop surfaces restart/stop exceptions only as a completed task.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _stopped, 1);
        _stopCts.Cancel();
        Task? runTask = _runTask;
        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _runLinkedCts?.Dispose();
        _stopCts.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            long generation = Interlocked.Increment(ref _generation);
            AppServerProcess process = CreateProcess();
            AppServerSession session;

            try
            {
                session = await process.StartAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await StopProcessAsync(process).ConfigureAwait(false);
                break;
            }
            catch
            {
                // Initialization failure: AppServerProcess already cleaned up the child.
                await StopProcessAsync(process).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (await HandleFaultAsync(becameHealthy: false, token).ConfigureAwait(false))
                {
                    break;
                }

                continue;
            }

            var generationSession = new AppServerGenerationSession(session, generation);
            EventHandler<RateLimitsUpdatedEventArgs> forwarder = (_, e) => ForwardRateLimits(generation, e);

            lock (_currentLock)
            {
                _current = generationSession;
                Volatile.Write(ref _currentGeneration, generation);
                _currentGateway = session.Gateway;
                _currentForwardHandler = forwarder;
                session.Gateway.RateLimitsUpdated += forwarder;
            }

            SessionPublished?.Invoke(this, new AppServerSupervisorEventArgs(generationSession));

            bool becameHealthy = await AwaitFaultAsync(session, generation, token)
                .ConfigureAwait(false);

            RetireGeneration(generation);
            await StopProcessAsync(process).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                break;
            }

            if (await HandleFaultAsync(becameHealthy, token).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private AppServerProcess CreateProcess() => new(
        _processHost,
        _startRequest,
        _clientInformation,
        _gracefulStopDelay,
        _graceDelay,
        _log);

    /// <summary>
    /// Waits for the current generation to fault, to be confirmed healthy (the healthy
    /// interval elapses), or to be cancelled. Returns true if the generation survived
    /// the healthy interval before faulting.
    /// </summary>
    private async Task<bool> AwaitFaultAsync(
        AppServerSession session,
        long generation,
        CancellationToken token)
    {
        using var exitCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task healthyTask = _healthyDelay.DelayAsync(_settings.HealthyResetInterval, exitCts.Token);
        Task cancelTask = Task.Delay(Timeout.InfiniteTimeSpan, exitCts.Token);
        Task faultTask = session.Completion;
        bool becameHealthy = false;

        try
        {
            while (true)
            {
                Task winner = await Task.WhenAny(faultTask, healthyTask, cancelTask)
                    .ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    return becameHealthy;
                }

                if (ReferenceEquals(winner, healthyTask))
                {
                    if (!becameHealthy)
                    {
                        becameHealthy = true;
                        ResetBackoff();
                        RaiseConfirmedHealthy(generation);
                    }

                    healthyTask = NeverCompletingTask;
                    continue;
                }

                // faultTask (or a cancelTask completion without token cancellation, treated
                // identically to a fault).
                return becameHealthy;
            }
        }
        finally
        {
            exitCts.Cancel();
        }
    }

    private void RetireGeneration(long generation)
    {
        CodexAppServerGateway? gateway;
        EventHandler<RateLimitsUpdatedEventArgs>? handler;

        lock (_currentLock)
        {
            if (_current is null || _current.GenerationId != generation)
            {
                return;
            }

            gateway = _currentGateway;
            handler = _currentForwardHandler;
            _current = null;
            Volatile.Write(ref _currentGeneration, 0);
            _currentGateway = null;
            _currentForwardHandler = null;
        }

        if (gateway is not null && handler is not null)
        {
            gateway.RateLimitsUpdated -= handler;
        }
    }

    private void ForwardRateLimits(long generation, RateLimitsUpdatedEventArgs args)
    {
        _ = args;
        if (Volatile.Read(ref _currentGeneration) != generation)
        {
            return;
        }

        RateLimitsUpdated?.Invoke(this, args);
    }

    private void RaiseConfirmedHealthy(long generation)
    {
        AppServerGenerationSession? current;
        lock (_currentLock)
        {
            current = _current;
        }

        if (current is null || current.GenerationId != generation)
        {
            return;
        }

        GenerationConfirmedHealthy?.Invoke(this, new AppServerSupervisorEventArgs(current));
    }

    /// <summary>
    /// Grows or resets the backoff step depending on whether the retired generation had
    /// been confirmed healthy, then waits the resulting delay. Returns true if the wait
    /// was cancelled (the caller should stop restarting).
    /// </summary>
    private async Task<bool> HandleFaultAsync(bool becameHealthy, CancellationToken token)
    {
        if (becameHealthy)
        {
            ResetBackoff();
        }
        else
        {
            GrowBackoff();
        }

        TimeSpan delay = CurrentBackoffDelay();
        try
        {
            await _backoffDelay.DelayAsync(delay, token).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return true;
        }
    }

    private void GrowBackoff() => Interlocked.Increment(ref _backoffStep);

    private void ResetBackoff() => Volatile.Write(ref _backoffStep, 0);

    private TimeSpan CurrentBackoffDelay()
    {
        int step = Volatile.Read(ref _backoffStep);
        if (step <= 0)
        {
            return _settings.InitialDelay;
        }

        int shift = step - 1;
        if (shift > 62)
        {
            return _settings.MaximumDelay;
        }

        long ticks = _settings.InitialDelay.Ticks << shift;
        TimeSpan delay = TimeSpan.FromTicks(ticks);
        if (delay < TimeSpan.Zero || delay > _settings.MaximumDelay)
        {
            return _settings.MaximumDelay;
        }

        return delay;
    }

    private static async Task StopProcessAsync(AppServerProcess process)
    {
        try
        {
            await process.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: a faulted process must not be orphaned. AppServerProcess.StopAsync
            // is idempotent and terminates the tree if graceful EOF does not suffice.
        }
    }

    private sealed class SystemDelay : IDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(delay, cancellationToken);
    }
}
