using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace CodexUsageWidget.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed durable queue for deferred delete-only cleanup work. A typical
/// item is the deletion of a temporary activation thread. Cleanup failures are
/// retried later; the queue never leads back to model generation.
///
/// Items remain in the <c>pending</c> state until explicitly marked completed.
/// A crash between <see cref="TryTakePendingAsync"/> and
/// <see cref="MarkCompletedAsync"/> therefore leaves the item eligible for
/// retry on the next pass, with no stuck <c>processing</c> state.
/// </summary>
public sealed class SqliteCleanupWorkStore : ICleanupWorkStore
{
    private readonly UsageStateDatabase _database;

    public SqliteCleanupWorkStore(UsageStateDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync(
        string attemptId,
        string threadId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        cancellationToken.ThrowIfCancellationRequested();

        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using DbTransactionWrapper transaction = new(
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction.Transaction;
            command.CommandText = """
INSERT INTO cleanup_work (cleanup_id, attempt_id, thread_id, enqueued_at, state)
VALUES (@cleanup_id, @attempt_id, @thread_id, @enqueued_at, 'pending')
ON CONFLICT(cleanup_id) DO NOTHING;
""";
            command.Parameters.Add("@cleanup_id", SqliteType.Text).Value = ComputeCleanupId(attemptId, threadId);
            command.Parameters.Add("@attempt_id", SqliteType.Text).Value = attemptId;
            command.Parameters.Add("@thread_id", SqliteType.Text).Value = threadId;
            command.Parameters.Add("@enqueued_at", SqliteType.Text).Value =
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<CleanupWorkItem?> TryTakePendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
SELECT cleanup_id, attempt_id, thread_id, enqueued_at, state
FROM cleanup_work
WHERE state = 'pending'
ORDER BY enqueued_at ASC
LIMIT 1;
""";

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadItem(reader);
    }

    /// <inheritdoc/>
    public async Task MarkCompletedAsync(string cleanupId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cleanupId);
        cancellationToken.ThrowIfCancellationRequested();

        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using DbTransactionWrapper transaction = new(
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction.Transaction;
            command.CommandText =
                "UPDATE cleanup_work SET state = 'completed' WHERE cleanup_id = @cleanup_id AND state = 'pending';";
            command.Parameters.Add("@cleanup_id", SqliteType.Text).Value = cleanupId;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task MarkFailedAsync(string cleanupId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cleanupId);
        cancellationToken.ThrowIfCancellationRequested();

        // Items remain in 'pending' after a failed cleanup attempt so they can be
        // retried later. No state change is required.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<CleanupWorkItem> ReadPendingAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
SELECT cleanup_id, attempt_id, thread_id, enqueued_at, state
FROM cleanup_work
WHERE state = 'pending'
ORDER BY enqueued_at ASC;
""";

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return ReadItem(reader);
        }
    }

    private static CleanupWorkItem ReadItem(SqliteDataReader reader) =>
        new(
            CleanupId: reader.GetString(0),
            AttemptId: reader.GetString(1),
            ThreadId: reader.GetString(2),
            EnqueuedAt: reader.GetString(3),
            State: ParseState(reader.GetString(4)));

    private static CleanupWorkState ParseState(string state) =>
        state.ToUpperInvariant() switch
        {
            "COMPLETED" => CleanupWorkState.Completed,
            _ => CleanupWorkState.Pending,
        };

    private static string ComputeCleanupId(string attemptId, string threadId)
    {
        // Deterministic idempotency key: the same attempt+thread pair never
        // produces more than one pending cleanup row.
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes($"{attemptId}\n{threadId}");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Thin wrapper around <see cref="SqliteTransaction"/u003e that ensures rollback on
    /// dispose if the transaction was not explicitly committed.
    /// </summary>
    private sealed class DbTransactionWrapper : IAsyncDisposable
    {
        private readonly SqliteTransaction _transaction;
        private bool _completed;

        public DbTransactionWrapper(SqliteTransaction transaction)
        {
            _transaction = transaction;
        }

        public SqliteTransaction Transaction => _transaction;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            _completed = true;
            return _transaction.CommitAsync(cancellationToken);
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            _completed = true;
            return _transaction.RollbackAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                await _transaction.RollbackAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await _transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}
