using System.Runtime.CompilerServices;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Tests.Testing;

internal sealed class ManualDelay : IDelay, IDisposable
{
    private readonly ManualClock _clock;
    private readonly List<PendingDelay> _pending = new();
    private readonly SemaphoreSlim _registered = new(0, int.MaxValue);

    public ManualDelay(ManualClock clock) => _clock = clock;

    public void Dispose() => _registered.Dispose();

    public int PendingCount
    {
        get
        {
            lock (_pending)
            {
                return _pending.Count;
            }
        }
    }

    public DateTimeOffset? NextDeadline
    {
        get
        {
            lock (_pending)
            {
                return _pending.MinBy(p => p.Deadline)?.Deadline;
            }
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        var deadline = _clock.UtcNow + delay;
        var pending = new PendingDelay(deadline, tcs, cancellationToken);

        lock (_pending)
        {
            _pending.Add(pending);
        }

        _registered.Release();

        if (cancellationToken.IsCancellationRequested)
        {
            tcs.TrySetCanceled(cancellationToken);
        }
        else
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }

    public async Task AdvanceAsync(TimeSpan delta)
    {
        _clock.Advance(delta);

        const int MaxIterations = 1000;
        const int RegistrationTimeoutMs = 500;

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            List<PendingDelay> ready;
            bool hasPending;

            lock (_pending)
            {
                ready = _pending.Where(p => p.Deadline <= _clock.UtcNow).ToList();
                _pending.RemoveAll(p => p.Deadline <= _clock.UtcNow);
                hasPending = _pending.Count > 0;
            }

            if (ready.Count == 0)
            {
                if (hasPending)
                {
                    break;
                }

                if (!await _registered.WaitAsync(RegistrationTimeoutMs).ConfigureAwait(false))
                {
                    break;
                }

                continue;
            }

            foreach (PendingDelay pending in ready)
            {
                pending.Completion.TrySetResult();
            }

            if (!hasPending)
            {
                if (!await _registered.WaitAsync(RegistrationTimeoutMs).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
    }

    private sealed record PendingDelay(
        DateTimeOffset Deadline,
        TaskCompletionSource Completion,
        CancellationToken CancellationToken);
}
