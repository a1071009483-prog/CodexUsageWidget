using System.Runtime.CompilerServices;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class FakeCleanupWorkStore : ICleanupWorkStore
{
    private readonly List<CleanupWorkItem> _pending = new();
    private readonly HashSet<string> _completed = new();

    public Exception? ExceptionToThrow { get; set; }

    public IReadOnlyList<CleanupWorkItem> Pending => _pending;

    public Task EnqueueAsync(string attemptId, string threadId, CancellationToken cancellationToken)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        if (!_pending.Any(p => p.AttemptId == attemptId && p.ThreadId == threadId))
        {
            _pending.Add(new CleanupWorkItem(
                CleanupId: Guid.NewGuid().ToString(),
                attemptId,
                threadId,
                EnqueuedAt: DateTimeOffset.UtcNow.ToString("O"),
                State: CleanupWorkState.Pending));
        }

        return Task.CompletedTask;
    }

    public Task<CleanupWorkItem?> TryTakePendingAsync(CancellationToken cancellationToken)
    {
        CleanupWorkItem? item = _pending.FirstOrDefault(p => p.State == CleanupWorkState.Pending);
        return Task.FromResult(item);
    }

    public Task MarkCompletedAsync(string cleanupId, CancellationToken cancellationToken)
    {
        _completed.Add(cleanupId);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string cleanupId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async IAsyncEnumerable<CleanupWorkItem> ReadPendingAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (CleanupWorkItem item in _pending.Where(p => p.State == CleanupWorkState.Pending).OrderBy(p => p.EnqueuedAt))
        {
            await Task.Yield();
            yield return item;
        }
    }
}
