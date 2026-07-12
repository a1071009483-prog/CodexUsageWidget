using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

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
            Console.WriteLine($"[ManualDelay] registered delay deadline={deadline:O} pending={_pending.Count}");
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
        Console.WriteLine($"[ManualDelay] AdvanceAsync delta={delta} now={_clock.UtcNow:O}");

        const int MaxIterations = 1000;
        const int RegistrationTimeoutMs = 2000;

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            List<PendingDelay> ready;
            bool hasPending;

            lock (_pending)
            {
                ready = _pending.Where(p => p.Deadline <= _clock.UtcNow).ToList();
                _pending.RemoveAll(p => p.Deadline <= _clock.UtcNow);
                hasPending = _pending.Count > 0;
                Console.WriteLine($"[ManualDelay] iter={iteration} ready={ready.Count} remaining={_pending.Count} hasPending={hasPending}");
            }

            if (ready.Count == 0)
            {
                // If there is already a pending future delay, we have caught up.
                if (hasPending)
                {
                    Console.WriteLine("[ManualDelay] no ready but future pending; break");
                    break;
                }

                // Otherwise wait for the monitor to register its next delay.
                Console.WriteLine("[ManualDelay] waiting for registration...");
                if (!await _registered.WaitAsync(RegistrationTimeoutMs).ConfigureAwait(false))
                {
                    Console.WriteLine("[ManualDelay] registration wait timeout; break");
                    break;
                }

                Console.WriteLine("[ManualDelay] registration signal received; loop");
                continue;
            }

            foreach (PendingDelay pending in ready)
            {
                Console.WriteLine($"[ManualDelay] completing deadline={pending.Deadline:O}");
                pending.Completion.TrySetResult();
            }

            // If no pending delay is registered yet, wait for the monitor to catch up.
            if (!hasPending)
            {
                Console.WriteLine("[ManualDelay] no remaining pending; wait for next registration...");
                if (!await _registered.WaitAsync(RegistrationTimeoutMs).ConfigureAwait(false))
                {
                    Console.WriteLine("[ManualDelay] next registration wait timeout; break");
                    break;
                }
            }
        }

        Console.WriteLine("[ManualDelay] AdvanceAsync returning");
    }

    private sealed record PendingDelay(
        DateTimeOffset Deadline,
        TaskCompletionSource Completion,
        CancellationToken CancellationToken);
}
