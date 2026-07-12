using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Persistence;

/// <summary>
/// Tests for the deferred cleanup queue. Each test uses a fresh temp-file SQLite
/// database so that crash-recovery behavior is exercised against a real file.
/// The class is <see cref="IDisposable"/> to clear connection pools and delete
/// the temp directory between tests.
/// </summary>
public sealed class CleanupQueueTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UsageStateDatabase _database;

    public CleanupQueueTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "codex-cleanup-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        _database = new UsageStateDatabase(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private SqliteCleanupWorkStore CreateQueue() => new(_database);

    [Fact]
    public async Task EnqueueAndTakePendingRoundTrip()
    {
        SqliteCleanupWorkStore queue = CreateQueue();

        await queue.EnqueueAsync("att-1", "thread-1", CancellationToken.None);

        CleanupWorkItem? item = await queue.TryTakePendingAsync(CancellationToken.None);

        Assert.NotNull(item);
        Assert.Equal("att-1", item!.AttemptId);
        Assert.Equal("thread-1", item.ThreadId);
        Assert.Equal(CleanupWorkState.Pending, item.State);
    }

    [Fact]
    public async Task TakePendingReturnsNullWhenQueueIsEmpty()
    {
        SqliteCleanupWorkStore queue = CreateQueue();

        CleanupWorkItem? item = await queue.TryTakePendingAsync(CancellationToken.None);

        Assert.Null(item);
    }

    [Fact]
    public async Task MarkCompletedRemovesItemFromPending()
    {
        SqliteCleanupWorkStore queue = CreateQueue();
        await queue.EnqueueAsync("att-1", "thread-1", CancellationToken.None);
        CleanupWorkItem? item = await queue.TryTakePendingAsync(CancellationToken.None);
        Assert.NotNull(item);

        await queue.MarkCompletedAsync(item!.CleanupId, CancellationToken.None);

        CleanupWorkItem? after = await queue.TryTakePendingAsync(CancellationToken.None);
        Assert.Null(after);
    }

    [Fact]
    public async Task MarkFailedLeavesItemPendingForRetry()
    {
        SqliteCleanupWorkStore queue = CreateQueue();
        await queue.EnqueueAsync("att-1", "thread-1", CancellationToken.None);
        CleanupWorkItem? item = await queue.TryTakePendingAsync(CancellationToken.None);
        Assert.NotNull(item);

        await queue.MarkFailedAsync(item!.CleanupId, CancellationToken.None);

        CleanupWorkItem? retried = await queue.TryTakePendingAsync(CancellationToken.None);
        Assert.NotNull(retried);
        Assert.Equal(item.CleanupId, retried!.CleanupId);
        Assert.Equal(CleanupWorkState.Pending, retried.State);
    }

    [Fact]
    public async Task MultiplePendingItemsAreDequeuedInFifoOrder()
    {
        SqliteCleanupWorkStore queue = CreateQueue();
        await queue.EnqueueAsync("att-1", "thread-1", CancellationToken.None);
        await queue.EnqueueAsync("att-2", "thread-2", CancellationToken.None);

        CleanupWorkItem? first = await queue.TryTakePendingAsync(CancellationToken.None);
        CleanupWorkItem? second = await queue.TryTakePendingAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("att-1", first!.AttemptId);
        Assert.Equal("att-2", second!.AttemptId);
    }

    [Fact]
    public async Task CrashRecoveryReadsPendingItems()
    {
        SqliteCleanupWorkStore firstQueue = CreateQueue();
        await firstQueue.EnqueueAsync("att-crash", "thread-crash", CancellationToken.None);

        SqliteCleanupWorkStore recoveredQueue = CreateQueue();
        CleanupWorkItem? item = await recoveredQueue.TryTakePendingAsync(CancellationToken.None);

        Assert.NotNull(item);
        Assert.Equal("att-crash", item!.AttemptId);
        Assert.Equal("thread-crash", item.ThreadId);
    }

    [Fact]
    public async Task EnqueueIsIdempotentForSameAttemptAndThread()
    {
        SqliteCleanupWorkStore queue = CreateQueue();

        await queue.EnqueueAsync("att-1", "thread-1", CancellationToken.None);
        await queue.EnqueueAsync("att-1", "thread-1", CancellationToken.None);

        CleanupWorkItem? first = await queue.TryTakePendingAsync(CancellationToken.None);
        CleanupWorkItem? second = await queue.TryTakePendingAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
    }
}
