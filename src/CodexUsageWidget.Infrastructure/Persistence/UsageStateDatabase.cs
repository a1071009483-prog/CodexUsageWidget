using Microsoft.Data.Sqlite;

namespace CodexUsageWidget.Infrastructure.Persistence;

/// <summary>
/// Production factory for the durable usage-state SQLite database.
///
/// Accepts an injected directory path (production: %LOCALAPPDATA%\CodexUsageWidget;
/// tests: a temp directory). Creates or opens state.db in that directory, runs all
/// pending migrations, and returns a ready-to-use connection. Fail-closed: if the
/// directory is not writable or migration fails, the exception propagates and no
/// partially-initialized connection is returned.
/// </summary>
public sealed class UsageStateDatabase
{
    private readonly string _connectionString;
    private readonly DatabaseMigrator _migrator;

    public UsageStateDatabase(string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);

        string resolvedPath = directoryPath.Trim();
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            throw new ArgumentException(
                "Directory path must not be empty or whitespace.",
                nameof(directoryPath));
        }

        Directory.CreateDirectory(resolvedPath);
        string dbPath = Path.Combine(resolvedPath, "state.db");
        _connectionString = $"Data Source={dbPath}";
        _migrator = new DatabaseMigrator(UsageStateSchema.Migrations);
    }

    public async Task<SqliteConnection> CreateConnectionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        try
        {
            await _migrator.MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            await SetPragmasAsync(connection, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }

    private static async Task SetPragmasAsync(
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
}
