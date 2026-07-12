using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace CodexUsageWidget.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed durable redacted audit store. Persists only the non-sensitive
/// metadata defined by <see cref="AuditEntry"/u003e; it never stores tokens,
/// cookies, raw credentials, prompt/response bodies, raw email, or workspace
/// content.
/// </summary>
public sealed class SqliteAuditStore : IAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly UsageStateDatabase _database;

    public SqliteAuditStore(UsageStateDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
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
INSERT INTO audit_rows (
    audit_id, attempt_id, namespace_hash, model_id, observed_at,
    pre_quota, post_quota, resets_at, turn_crossed_boundary,
    outcome, error_category, recorded_at
) VALUES (
    @audit_id, @attempt_id, @namespace_hash, @model_id, @observed_at,
    @pre_quota, @post_quota, @resets_at, @turn_crossed_boundary,
    @outcome, @error_category, @recorded_at
)
ON CONFLICT(audit_id) DO UPDATE SET
    attempt_id = excluded.attempt_id,
    namespace_hash = excluded.namespace_hash,
    model_id = excluded.model_id,
    observed_at = excluded.observed_at,
    pre_quota = excluded.pre_quota,
    post_quota = excluded.post_quota,
    resets_at = excluded.resets_at,
    turn_crossed_boundary = excluded.turn_crossed_boundary,
    outcome = excluded.outcome,
    error_category = excluded.error_category,
    recorded_at = excluded.recorded_at;
""";
            command.Parameters.Add("@audit_id", SqliteType.Text).Value = entry.AuditId;
            AddNullableStringParameter(command, "@attempt_id", entry.AttemptId);
            command.Parameters.Add("@namespace_hash", SqliteType.Text).Value = entry.NamespaceHash;
            AddNullableStringParameter(command, "@model_id", entry.ModelId);
            command.Parameters.Add("@observed_at", SqliteType.Text).Value = entry.ObservedAt;
            AddNullableStringParameter(command, "@pre_quota", SerializeQuota(entry.PreQuota));
            AddNullableStringParameter(command, "@post_quota", SerializeQuota(entry.PostQuota));
            AddNullableStringParameter(command, "@resets_at", SelectResetsAt(entry));
            command.Parameters.Add("@turn_crossed_boundary", SqliteType.Integer).Value = entry.TurnCrossedBoundary ? 1 : 0;
            AddNullableStringParameter(command, "@outcome", entry.Outcome);
            AddNullableStringParameter(command, "@error_category", entry.ErrorCategory);
            command.Parameters.Add("@recorded_at", SqliteType.Text).Value = entry.RecordedAt;

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
    public async Task<AuditEntry?> ReadAsync(string auditId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditId);
        cancellationToken.ThrowIfCancellationRequested();

        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
SELECT audit_id, attempt_id, namespace_hash, model_id, observed_at,
       pre_quota, post_quota, resets_at, turn_crossed_boundary,
       outcome, error_category, recorded_at
FROM audit_rows
WHERE audit_id = @audit_id;
""";
        command.Parameters.Add("@audit_id", SqliteType.Text).Value = auditId;

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadEntry(reader);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AuditEntry> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
SELECT audit_id, attempt_id, namespace_hash, model_id, observed_at,
       pre_quota, post_quota, resets_at, turn_crossed_boundary,
       outcome, error_category, recorded_at
FROM audit_rows
ORDER BY recorded_at DESC, audit_id DESC;
""";

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return ReadEntry(reader);
        }
    }

    private static AuditEntry ReadEntry(SqliteDataReader reader) =>
        new(
            AuditId: reader.GetString(0),
            AttemptId: reader.IsDBNull(1) ? null : reader.GetString(1),
            NamespaceHash: reader.GetString(2),
            ModelId: reader.IsDBNull(3) ? null : reader.GetString(3),
            ObservedAt: reader.GetString(4),
            PreQuota: DeserializeQuota(reader.IsDBNull(5) ? null : reader.GetString(5)),
            PostQuota: DeserializeQuota(reader.IsDBNull(6) ? null : reader.GetString(6)),
            // Column resets_at (index 7) is retained for compatibility but is
            // superseded by the reset instants stored inside the quota snapshots.
            TurnCrossedBoundary: reader.GetInt32(8) != 0,
            Outcome: reader.IsDBNull(9) ? null : reader.GetString(9),
            ErrorCategory: reader.IsDBNull(10) ? null : reader.GetString(10),
            RecordedAt: reader.GetString(11));

    private static string? SelectResetsAt(AuditEntry entry)
    {
        // Prefer the post-activation reset; fall back to the pre-activation reset.
        // The quota snapshot JSON already captures this, but the standalone column
        // remains for simple queries.
        return entry.PostQuota?.ResetsAt ?? entry.PreQuota?.ResetsAt;
    }

    private static string? SerializeQuota(AuditQuotaSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private static AuditQuotaSnapshot? DeserializeQuota(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<AuditQuotaSnapshot>(json, JsonOptions);
    }

    private static async Task SetSynchronousFullAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA synchronous = FULL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddNullableStringParameter(
        SqliteCommand command, string name, string? value)
    {
        SqliteParameter parameter = command.Parameters.Add(name, SqliteType.Text);
        parameter.Value = value is null ? DBNull.Value : value;
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
