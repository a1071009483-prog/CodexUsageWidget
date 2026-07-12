using System.Collections.Concurrent;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class ManualDelay : IDelay, IDisposable
{
    private readonly ManualClock _clock;
    private readonly ConcurrentDictionary<Guid, DelayEntry> _entries = new();

    public ManualDelay(ManualClock clock)
    {
        _clock = clock;
    }

    public int PendingCount => _entries.Count;

    public DateTimeOffset NextDeadline
    {
        get
        {
            if (_entries.IsEmpty)
            {
                return DateTimeOffset.MaxValue;
            }

            return _entries.Values.Select(e => e.Deadline).Min();
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        DelayEntry entry = new(_clock.UtcNow + delay);
        _entries[entry.Id] = entry;

        cancellationToken.Register(() => entry.TrySetCanceled());
        return entry.Task;
    }

    public void AdvanceAsync(TimeSpan delta)
    {
        DateTimeOffset now = _clock.UtcNow + delta;
        _clock.Set(now);

        foreach (DelayEntry entry in _entries.Values.ToList())
        {
            if (entry.Deadline <= now)
            {
                entry.TrySetResult();
                _entries.TryRemove(entry.Id, out _);
            }
        }
    }

    public void Dispose()
    {
        foreach (DelayEntry entry in _entries.Values)
        {
            entry.TrySetCanceled();
        }

        _entries.Clear();
    }

    private sealed class DelayEntry
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DelayEntry(DateTimeOffset deadline)
        {
            Id = Guid.NewGuid();
            Deadline = deadline;
        }

        public Guid Id { get; }
        public DateTimeOffset Deadline { get; }
        public Task Task => _tcs.Task;

        public bool TrySetResult() => _tcs.TrySetResult();

        public bool TrySetCanceled() => _tcs.TrySetCanceled();
    }
}
