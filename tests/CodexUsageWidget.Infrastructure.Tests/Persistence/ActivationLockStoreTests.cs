using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Persistence;

/// <summary>
/// Tests for the durable activation lock store. Each test uses a fresh temp-file
/// SQLite database so that crash-recovery semantics are exercised against a real
/// persisted file. The class is <see cref="IDisposable"/> to clear connection
/// pools and delete the temp directory between tests.
/// </summary>
public sealed class ActivationLockStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UsageStateDatabase _database;

    public ActivationLockStoreTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "codex-activation-lock-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
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

    private static ActivationAttempt NewAttempt(
        string attemptId = "att-1",
        string namespaceHash = "ns-hash-a",
        string workspaceScope = "global",
        string windowKey = "win-2026-07-12T08:00:00Z",
        string windowKind = "authoritative") =>
        new(
            AttemptId: attemptId,
            NamespaceHash: namespaceHash,
            WorkspaceScope: workspaceScope,
            WindowKey: windowKey,
            WindowKind: windowKind,
            SuppressionDeadline: "2026-07-12T13:00:00Z",
            ObservedAt: "2026-07-12T07:45:00Z",
            AttemptAt: "2026-07-12T07:46:00Z",
            PreUsedPercent: 0,
            PreResetsAt: null,
            ModelId: null,
            TurnStarted: false,
            TerminalOutcome: null,
            PostUsedPercent: null,
            PostResetsAt: null,
            CleanupState: "none");

    private ActivationLockStore CreateStore() => new(_database);

    [Fact]
    public async Task TryAcquireInsertsPendingAttemptWithTurnStartedFalse()
    {
        ActivationLockStore store = CreateStore();
        ActivationAttempt attempt = NewAttempt();

        AcquisitionResult result = await store.TryAcquireAsync(attempt, CancellationToken.None);

        Assert.True(result.Acquired);
        Assert.Null(result.Existing);

        ActivationAttempt? loaded = await store.GetActiveAsync(
            attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey,
            CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(attempt.AttemptId, loaded!.AttemptId);
        Assert.False(loaded.TurnStarted);
        Assert.Null(loaded.TerminalOutcome);
        Assert.Equal("none", loaded.CleanupState);
        Assert.Equal(0, loaded.PreUsedPercent);
    }

    [Fact]
    public async Task DuplicateKeyBlocksSecondAcquireAndReturnsExisting()
    {
        ActivationLockStore store = CreateStore();
        ActivationAttempt first = NewAttempt(attemptId: "att-1");
        ActivationAttempt second = NewAttempt(attemptId: "att-2");

        AcquisitionResult firstResult = await store.TryAcquireAsync(first, CancellationToken.None);
        Assert.True(firstResult.Acquired);

        AcquisitionResult secondResult = await store.TryAcquireAsync(second, CancellationToken.None);

        Assert.False(secondResult.Acquired);
        Assert.NotNull(secondResult.Existing);
        Assert.Equal(first.AttemptId, secondResult.Existing!.AttemptId);
        Assert.Equal(first.NamespaceHash, secondResult.Existing.NamespaceHash);
        Assert.Equal(first.WindowKey, secondResult.Existing.WindowKey);
    }

    [Fact]
    public async Task DifferentKeysAreIndependent()
    {
        ActivationLockStore store = CreateStore();

        AcquisitionResult a = await store.TryAcquireAsync(
            NewAttempt(attemptId: "att-a", namespaceHash: "ns-a", windowKey: "win-a"),
            CancellationToken.None);
        AcquisitionResult b = await store.TryAcquireAsync(
            NewAttempt(attemptId: "att-b", namespaceHash: "ns-b", windowKey: "win-b"),
            CancellationToken.None);

        Assert.True(a.Acquired);
        Assert.True(b.Acquired);

        ActivationAttempt? loadedA = await store.GetActiveAsync("ns-a", "global", "win-a", CancellationToken.None);
        ActivationAttempt? loadedB = await store.GetActiveAsync("ns-b", "global", "win-b", CancellationToken.None);
        Assert.NotNull(loadedA);
        Assert.NotNull(loadedB);
        Assert.Equal("att-a", loadedA!.AttemptId);
        Assert.Equal("att-b", loadedB!.AttemptId);
    }

    [Fact]
    public async Task CrashRecoveryLoadsExistingLock()
    {
        ActivationAttempt attempt = NewAttempt();

        // First instance acquires the lock, then is "lost" (simulating a crash).
        ActivationLockStore firstStore = CreateStore();
        await firstStore.TryAcquireAsync(attempt, CancellationToken.None);

        // A brand-new store instance against the same database file simulates a restart.
        // The recovered lock must be visible so the at-most-once guard survives.
        ActivationLockStore recoveredStore = CreateStore();

        ActivationAttempt? recovered = await recoveredStore.GetActiveAsync(
            attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey,
            CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(attempt.AttemptId, recovered!.AttemptId);
        Assert.False(recovered.TurnStarted);

        // A duplicate acquire against the recovered store must also be blocked.
        AcquisitionResult duplicate = await recoveredStore.TryAcquireAsync(
            NewAttempt(attemptId: "att-late"),
            CancellationToken.None);
        Assert.False(duplicate.Acquired);
        Assert.NotNull(duplicate.Existing);
        Assert.Equal(attempt.AttemptId, duplicate.Existing!.AttemptId);
    }

    [Fact]
    public async Task MarkTurnStartedSetsFlag()
    {
        ActivationLockStore store = CreateStore();
        ActivationAttempt attempt = NewAttempt();
        await store.TryAcquireAsync(attempt, CancellationToken.None);

        await store.MarkTurnStartedAsync(attempt.AttemptId, CancellationToken.None);

        ActivationAttempt? loaded = await store.GetActiveAsync(
            attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey,
            CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(loaded!.TurnStarted);

        // Idempotent: calling again produces no exception and flag remains true.
        await store.MarkTurnStartedAsync(attempt.AttemptId, CancellationToken.None);
        ActivationAttempt? reloaded = await store.GetActiveAsync(
            attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey,
            CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.TurnStarted);
    }

    [Fact]
    public async Task MarkTerminalRecordsOutcomeAndPostQuota()
    {
        ActivationLockStore store = CreateStore();
        ActivationAttempt attempt = NewAttempt();
        await store.TryAcquireAsync(attempt, CancellationToken.None);

        await store.MarkTerminalAsync(
            attempt.AttemptId,
            terminalOutcome: "succeeded",
            postUsedPercent: 1,
            postResetsAt: "2026-07-12T13:05:00Z",
            cleanupState: "completed",
            CancellationToken.None);

        ActivationAttempt? loaded = await store.GetActiveAsync(
            attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey,
            CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("succeeded", loaded!.TerminalOutcome);
        Assert.Equal(1, loaded.PostUsedPercent);
        Assert.Equal("2026-07-12T13:05:00Z", loaded.PostResetsAt);
        Assert.Equal("completed", loaded.CleanupState);
    }

    [Fact]
    public async Task ExtendSuppressionDeadlineUpdatesDeadline()
    {
        ActivationLockStore store = CreateStore();
        ActivationAttempt attempt = NewAttempt();
        await store.TryAcquireAsync(attempt, CancellationToken.None);

        const string newDeadline = "2026-07-12T14:30:00Z";
        await store.ExtendSuppressionDeadlineAsync(
            attempt.AttemptId, newDeadline, CancellationToken.None);

        ActivationAttempt? loaded = await store.GetActiveAsync(
            attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey,
            CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(newDeadline, loaded!.SuppressionDeadline);
    }

    [Fact]
    public async Task OtherSqliteErrorFailsClosedAndThrows()
    {
        ActivationLockStore store = CreateStore();

        // Passing null for a NOT NULL column produces a provider-level error that
        // is NOT a UNIQUE conflict. The store must fail closed by throwing rather
        // than silently treating it as a duplicate or returning Acquired=true.
        ActivationAttempt badAttempt = NewAttempt() with { NamespaceHash = null! };

        Exception? thrown = await Record.ExceptionAsync(
            () => store.TryAcquireAsync(badAttempt, CancellationToken.None));
        Assert.NotNull(thrown);
    }

    [Fact]
    public async Task PrimaryKeyCollisionIsFailSafeAndDoesNotAcquire()
    {
        // A collision on the attempt_id PRIMARY KEY (a caller bug: reusing an attempt id
        // for a different scoped key) is reported by SQLite/Microsoft.Data.Sqlite as a
        // UNIQUE-style constraint on the implicit primary-key index. The store treats it
        // as a deduplication block (Acquired=false) rather than throwing or — critically —
        // returning Acquired=true. This is the fail-safe direction: no generation can
        // result from a primary-key collision. (In practice attempt ids are unique GUIDs,
        // so this path is unreachable; the test pins the fail-safe behavior.)
        ActivationLockStore store = CreateStore();

        ActivationAttempt first = NewAttempt(
            attemptId: "att-shared",
            namespaceHash: "ns-hash-a",
            workspaceScope: "global",
            windowKey: "win-a");
        Assert.True((await store.TryAcquireAsync(first, CancellationToken.None)).Acquired);

        // Same attempt_id (PRIMARY KEY collision) but a DIFFERENT scoped key.
        ActivationAttempt second = NewAttempt(
            attemptId: "att-shared",
            namespaceHash: "ns-hash-b",
            workspaceScope: "team",
            windowKey: "win-b");

        Exception? thrown = await Record.ExceptionAsync(
            () => store.TryAcquireAsync(second, CancellationToken.None));
        Assert.Null(thrown); // fail-safe: no exception escapes.

        AcquisitionResult result = await store.TryAcquireAsync(second, CancellationToken.None);
        Assert.False(result.Acquired); // and it never claims acquisition.
    }
}
