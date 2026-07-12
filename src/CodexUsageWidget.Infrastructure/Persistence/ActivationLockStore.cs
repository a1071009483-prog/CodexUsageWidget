using CodexUsageWidget.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace CodexUsageWidget.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed durable activation lock store implementing atomic write-ahead
/// deduplication for the at-most-once generation guarantee.
///
/// Every operation opens a fresh connection, sets <c>PRAGMA synchronous = FULL</c>
/// for durable flush, and executes inside a transaction that is committed before
/// the connection is returned. A UNIQUE-constraint conflict on
/// <c>(namespace_hash, workspace_scope, window_key)</c> is treated as a benign
/// deduplication block: the transaction is rolled back, the existing attempt is
/// read, and <see cref="AcquisitionResult.Acquired"/>=<c>false</c> is returned
/// without throwing. Any other SQLite/IO error fails closed by propagating the
/// exception after rollback.
///
/// This store performs NO model consumption (<c>thread/start</c>/<c>turn/start</c>).
/// </summary>
public sealed class ActivationLockStore : IActivationLockStore
{
    /// <summary>
    /// SQLite extended result code for a UNIQUE constraint violation
    /// (<c>SQLITE_CONSTRAINT_UNIQUE</c> = 19 + 8*256 = 2067).
    /// </summary>
    private const int SqliteConstraintUniqueExtended = 2067;

    /// <summary>
    /// SQLite base result code for any constraint violation
    /// (<c>SQLITE_CONSTRAINT</c> = 19).
    /// </summary>
    private const int SqliteConstraintBase = 19;

    private readonly UsageStateDatabase _database;

    public ActivationLockStore(UsageStateDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc/>
    public async Task<AcquisitionResult> TryAcquireAsync(
        ActivationAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        cancellationToken.ThrowIfCancellationRequested();

        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SetSynchronousFullAsync(connection, cancellationToken).ConfigureAwait(false);

        await using DbTransactionWrapper transaction = new(
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));

        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction.Transaction;
            command.CommandText = """
INSERT INTO activation_attempts (
    attempt_id, namespace_hash, workspace_scope, window_key, window_kind,
    suppression_deadline, observed_at, attempt_at, pre_used_percent,
    pre_resets_at, model_id, turn_started, terminal_outcome,
    post_used_percent, post_resets_at, cleanup_state
) VALUES (
    @attempt_id, @namespace_hash, @workspace_scope, @window_key, @window_kind,
    @suppression_deadline, @observed_at, @attempt_at, @pre_used_percent,
    @pre_resets_at, @model_id, @turn_started, @terminal_outcome,
    @post_used_percent, @post_resets_at, @cleanup_state
)
""";
            command.Parameters.Add("@attempt_id", SqliteType.Text).Value = attempt.AttemptId;
            command.Parameters.Add("@namespace_hash", SqliteType.Text).Value = attempt.NamespaceHash;
            command.Parameters.Add("@workspace_scope", SqliteType.Text).Value = attempt.WorkspaceScope;
            command.Parameters.Add("@window_key", SqliteType.Text).Value = attempt.WindowKey;
            command.Parameters.Add("@window_kind", SqliteType.Text).Value = attempt.WindowKind;
            command.Parameters.Add("@suppression_deadline", SqliteType.Text).Value = attempt.SuppressionDeadline;
            command.Parameters.Add("@observed_at", SqliteType.Text).Value = attempt.ObservedAt;
            command.Parameters.Add("@attempt_at", SqliteType.Text).Value = attempt.AttemptAt;
            command.Parameters.Add("@pre_used_percent", SqliteType.Integer).Value = attempt.PreUsedPercent;
            AddNullableStringParameter(command, "@pre_resets_at", attempt.PreResetsAt);
            AddNullableStringParameter(command, "@model_id", attempt.ModelId);
            command.Parameters.Add("@turn_started", SqliteType.Integer).Value = attempt.TurnStarted ? 1 : 0;
            AddNullableStringParameter(command, "@terminal_outcome", attempt.TerminalOutcome);
            AddNullableIntParameter(command, "@post_used_percent", attempt.PostUsedPercent);
            AddNullableStringParameter(command, "@post_resets_at", attempt.PostResetsAt);
            command.Parameters.Add("@cleanup_state", SqliteType.Text).Value = attempt.CleanupState;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new AcquisitionResult(Acquired: true, Existing: null);
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            ActivationAttempt? existing = await ReadAttemptAsync(
                connection, attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey,
                cancellationToken).ConfigureAwait(false);
            return new AcquisitionResult(Acquired: false, Existing: existing);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<ActivationAttempt?> GetActiveAsync(
        string namespaceHash,
        string workspaceScope,
        string windowKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        cancellationToken.ThrowIfCancellationRequested();

        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SetSynchronousFullAsync(connection, cancellationToken).ConfigureAwait(false);

        return await ReadAttemptAsync(
            connection, namespaceHash, workspaceScope, windowKey,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task MarkTurnStartedAsync(
        string attemptId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        cancellationToken.ThrowIfCancellationRequested();

        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SetSynchronousFullAsync(connection, cancellationToken).ConfigureAwait(false);

        await using DbTransactionWrapper transaction = new(
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction.Transaction;
            command.CommandText =
                "UPDATE activation_attempts SET turn_started = 1 WHERE attempt_id = @attempt_id;";
            command.Parameters.Add("@attempt_id", SqliteType.Text).Value = attemptId;
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
    public async Task MarkTerminalAsync(
        string attemptId,
        string terminalOutcome,
        int? postUsedPercent,
        string? postResetsAt,
        string cleanupState,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalOutcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(cleanupState);
        cancellationToken.ThrowIfCancellationRequested();

        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SetSynchronousFullAsync(connection, cancellationToken).ConfigureAwait(false);

        await using DbTransactionWrapper transaction = new(
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction.Transaction;
            command.CommandText = """
UPDATE activation_attempts
SET terminal_outcome = @terminal_outcome,
    post_used_percent = @post_used_percent,
    post_resets_at = @post_resets_at,
    cleanup_state = @cleanup_state
WHERE attempt_id = @attempt_id;
""";
            command.Parameters.Add("@attempt_id", SqliteType.Text).Value = attemptId;
            command.Parameters.Add("@terminal_outcome", SqliteType.Text).Value = terminalOutcome;
            AddNullableIntParameter(command, "@post_used_percent", postUsedPercent);
            AddNullableStringParameter(command, "@post_resets_at", postResetsAt);
            command.Parameters.Add("@cleanup_state", SqliteType.Text).Value = cleanupState;
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
    public async Task ExtendSuppressionDeadlineAsync(
        string attemptId,
        string newSuppressionDeadline,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newSuppressionDeadline);
        cancellationToken.ThrowIfCancellationRequested();

        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SetSynchronousFullAsync(connection, cancellationToken).ConfigureAwait(false);

        await using DbTransactionWrapper transaction = new(
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction.Transaction;
            command.CommandText =
                "UPDATE activation_attempts SET suppression_deadline = @deadline WHERE attempt_id = @attempt_id;";
            command.Parameters.Add("@attempt_id", SqliteType.Text).Value = attemptId;
            command.Parameters.Add("@deadline", SqliteType.Text).Value = newSuppressionDeadline;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Sets durability and contention pragmas so every commit is durably flushed to disk
    /// before the transaction is reported as committed, and a concurrent writer waits
    /// briefly for the lock instead of failing immediately. <c>PRAGMA synchronous = FULL</c>
    /// is the core of the at-most-once guarantee: a crash after commit must not lose the
    /// lock record. <c>PRAGMA busy_timeout</c> lets a second concurrent <c>TryAcquireAsync</c>
    /// for the same scoped key wait for the first to commit, then observe the UNIQUE block
    /// (returning the existing attempt) rather than throwing a transient
    /// <c>SQLITE_BUSY</c>.
    /// </summary>
    private static async Task SetSynchronousFullAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
PRAGMA synchronous = FULL;
PRAGMA busy_timeout = 5000;
""";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the single attempt matching the scoped key, or <c>null</c> if none exists.
    /// </summary>
    private static async Task<ActivationAttempt?> ReadAttemptAsync(
        SqliteConnection connection,
        string namespaceHash,
        string workspaceScope,
        string windowKey,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
SELECT attempt_id, namespace_hash, workspace_scope, window_key, window_kind,
       suppression_deadline, observed_at, attempt_at, pre_used_percent,
       pre_resets_at, model_id, turn_started, terminal_outcome,
       post_used_percent, post_resets_at, cleanup_state
FROM activation_attempts
WHERE namespace_hash = @ns AND workspace_scope = @ws AND window_key = @wk;
""";
        command.Parameters.Add("@ns", SqliteType.Text).Value = namespaceHash;
        command.Parameters.Add("@ws", SqliteType.Text).Value = workspaceScope;
        command.Parameters.Add("@wk", SqliteType.Text).Value = windowKey;

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new ActivationAttempt(
            AttemptId: reader.GetString(0),
            NamespaceHash: reader.GetString(1),
            WorkspaceScope: reader.GetString(2),
            WindowKey: reader.GetString(3),
            WindowKind: reader.GetString(4),
            SuppressionDeadline: reader.GetString(5),
            ObservedAt: reader.GetString(6),
            AttemptAt: reader.GetString(7),
            PreUsedPercent: reader.GetInt32(8),
            PreResetsAt: reader.IsDBNull(9) ? null : reader.GetString(9),
            ModelId: reader.IsDBNull(10) ? null : reader.GetString(10),
            TurnStarted: reader.GetInt32(11) != 0,
            TerminalOutcome: reader.IsDBNull(12) ? null : reader.GetString(12),
            PostUsedPercent: reader.IsDBNull(13) ? null : reader.GetInt32(13),
            PostResetsAt: reader.IsDBNull(14) ? null : reader.GetString(14),
            CleanupState: reader.GetString(15));
    }

    /// <summary>
    /// Determines whether a <see cref="SqliteException"/> represents a UNIQUE
    /// constraint violation (the deduplication block case). Microsoft.Data.Sqlite
    /// exposes the SQLite extended result code via <see cref="SqliteException.SqliteErrorCode"/>;
    /// <c>SQLITE_CONSTRAINT_UNIQUE</c> is 2067. As a robustness fallback, a base
    /// <c>SQLITE_CONSTRAINT</c> (19) is accepted only when the error message
    /// explicitly names the UNIQUE constraint, so that a NOT NULL or CHECK
    /// violation (also base 19) fails closed instead of being mistaken for a
    /// duplicate.
    /// </summary>
    private static bool IsUniqueConstraintViolation(SqliteException exception)
    {
        int code = exception.SqliteErrorCode;
        if (code == SqliteConstraintUniqueExtended)
        {
            return true;
        }

        if (code == SqliteConstraintBase
            && exception.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static void AddNullableStringParameter(
        SqliteCommand command, string name, string? value)
    {
        SqliteParameter parameter = command.Parameters.Add(name, SqliteType.Text);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private static void AddNullableIntParameter(
        SqliteCommand command, string name, int? value)
    {
        SqliteParameter parameter = command.Parameters.Add(name, SqliteType.Integer);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    /// <summary>
    /// Thin wrapper around <see cref="SqliteTransaction"/> that ensures rollback on
    /// dispose if the transaction was not explicitly committed. This guards
    /// against a missing commit after an exception escapes the using block.
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
