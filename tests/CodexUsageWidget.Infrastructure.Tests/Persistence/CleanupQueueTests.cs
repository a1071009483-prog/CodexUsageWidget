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
        Assert.NotNull(first);
        await queue.MarkCompletedAsync(first!.CleanupId, CancellationToken.None);

        CleanupWorkItem? second = await queue.TryTakePendingAsync(CancellationToken.None);

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
        Assert.NotNull(first);
        await queue.MarkCompletedAsync(first!.CleanupId, CancellationToken.None);

        CleanupWorkItem? second = await queue.TryTakePendingAsync(CancellationToken.None);
        Assert.Null(second);
    }

    [Fact]
    public async Task CrashBetweenTakeAndCompleteLeavesItemPendingForRetry()
    {
        SqliteCleanupWorkStore firstQueue = CreateQueue();
        await firstQueue.EnqueueAsync("att-crash", "thread-crash", CancellationToken.None);

        CleanupWorkItem? taken = await firstQueue.TryTakePendingAsync(CancellationToken.None);
        Assert.NotNull(taken);

        // Simulate a crash by creating a brand-new store against the same database.
        // The new implementation leaves items in 'pending', so the item must still
        // be retrievable and completable by a recovered process.
        SqliteCleanupWorkStore recoveredQueue = CreateQueue();
        CleanupWorkItem? retried = await recoveredQueue.TryTakePendingAsync(CancellationToken.None);

        Assert.NotNull(retried);
        Assert.Equal(taken!.CleanupId, retried!.CleanupId);
        Assert.Equal(CleanupWorkState.Pending, retried.State);

        await recoveredQueue.MarkCompletedAsync(retried.CleanupId, CancellationToken.None);
        CleanupWorkItem? after = await recoveredQueue.TryTakePendingAsync(CancellationToken.None);
        Assert.Null(after);
    }

    [Fact]
    public async Task ConcurrentEnqueuesAndTakerCompleteAllItemsWithoutBusyErrors()
    {
        SqliteCleanupWorkStore queue = CreateQueue();
        const int itemsPerProducer = 5;
        const int producerCount = 4;
        int completed = 0;
        List<Exception> exceptions = new();

        Task[] producers = Enumerable.Range(0, producerCount).SelectMany(p =>
            Enumerable.Range(0, itemsPerProducer).Select(i => Task.Run(async () =>
            {
                try
                {
                    await queue.EnqueueAsync($"att-{p}-{i}", $"thread-{p}-{i}", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }))).ToArray();

        Task consumer = Task.Run(async () =>
        {
            try
            {
                int expected = producerCount * itemsPerProducer;
                while (completed < expected)
                {
                    CleanupWorkItem? item = await queue.TryTakePendingAsync(CancellationToken.None);
                    if (item is null)
                    {
                        await Task.Delay(10, CancellationToken.None);
                        continue;
                    }

                    await queue.MarkCompletedAsync(item.CleanupId, CancellationToken.None);
                    Interlocked.Increment(ref completed);
                }
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll(producers);
        await consumer;

        Assert.Empty(exceptions);
        Assert.Equal(producerCount * itemsPerProducer, completed);
        Assert.Null(await queue.TryTakePendingAsync(CancellationToken.None));
    }
}
