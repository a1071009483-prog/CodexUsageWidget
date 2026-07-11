using System.Data.Common;
using System.Globalization;

namespace CodexUsageWidget.Infrastructure.Persistence;

/// <summary>
/// Versioned migration runner for the usage-state SQLite database.
///
/// Reads PRAGMA user_version, applies each pending migration in ascending version order
/// inside a single transaction, and advances user_version after a successful commit.
/// Idempotent: repeated calls at the same version are a no-op. Fail-closed: any migration
/// exception propagates to the caller and the version is not advanced past the failure.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly IReadOnlyList<Migration> _migrations;

    public DatabaseMigrator(IReadOnlyList<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        _migrations = [.. migrations.OrderBy(m => m.Version)];
    }

    public async Task MigrateAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        int currentVersion = await GetUserVersionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        foreach (Migration migration in _migrations)
        {
            if (migration.Version <= currentVersion)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            await ApplyMigrationAsync(connection, migration, cancellationToken)
                .ConfigureAwait(false);
            currentVersion = migration.Version;
        }
    }

    private static async Task ApplyMigrationAsync(
        DbConnection connection,
        Migration migration,
        CancellationToken cancellationToken)
    {
        await using DbTransaction transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await ExecuteSqlAsync(connection, transaction, migration.UpSql, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await SetUserVersionAsync(connection, migration.Version, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> GetUserVersionAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? result = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task SetUserVersionAsync(
        DbConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = string.Format(
            CultureInfo.InvariantCulture,
            "PRAGMA user_version = {0};",
            version);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteSqlAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
