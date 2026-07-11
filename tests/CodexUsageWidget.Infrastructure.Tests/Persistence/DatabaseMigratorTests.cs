using System.Globalization;
using CodexUsageWidget.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Persistence;

public sealed class DatabaseMigratorTests
{
    [Fact]
    public async Task FreshDatabaseMigratesToLatestVersionAndCreatesAllTables()
    {
        using SqliteConnection connection = await OpenInMemoryAsync();
        var migrator = new DatabaseMigrator(UsageStateSchema.Migrations);

        await migrator.MigrateAsync(connection, CancellationToken.None);

        Assert.Equal(UsageStateSchema.LatestVersion, await GetUserVersionAsync(connection));

        HashSet<string> tables = await GetTableNamesAsync(connection);
        Assert.Contains("settings", tables);
        Assert.Contains("account_namespaces", tables);
        Assert.Contains("activation_attempts", tables);
        Assert.Contains("notifications", tables);
        Assert.Contains("cleanup_work", tables);
        Assert.Contains("audit_rows", tables);
    }

    [Fact]
    public async Task MigrateIsIdempotentWhenCalledRepeatedly()
    {
        using SqliteConnection connection = await OpenInMemoryAsync();
        var migrator = new DatabaseMigrator(UsageStateSchema.Migrations);

        await migrator.MigrateAsync(connection, CancellationToken.None);
        await migrator.MigrateAsync(connection, CancellationToken.None);
        await migrator.MigrateAsync(connection, CancellationToken.None);

        Assert.Equal(UsageStateSchema.LatestVersion, await GetUserVersionAsync(connection));

        HashSet<string> tables = await GetTableNamesAsync(connection);
        Assert.Equal(6, tables.Count);
    }

    [Fact]
    public async Task StaleVersionAfterCrashReAppliesIdempotentMigrationWithoutError()
    {
        // PRAGMA user_version is advanced after the migration transaction commits, so a crash
        // between commit and version-advance leaves the database with tables present but a
        // stale version. Re-migrating must re-apply the idempotent migration safely and end
        // at the latest version.
        using SqliteConnection connection = await OpenInMemoryAsync();
        var migrator = new DatabaseMigrator(UsageStateSchema.Migrations);

        await migrator.MigrateAsync(connection, CancellationToken.None);
        Assert.Equal(UsageStateSchema.LatestVersion, await GetUserVersionAsync(connection));

        await SetUserVersionAsync(connection, 0);

        await migrator.MigrateAsync(connection, CancellationToken.None);

        Assert.Equal(UsageStateSchema.LatestVersion, await GetUserVersionAsync(connection));
        HashSet<string> tables = await GetTableNamesAsync(connection);
        Assert.Equal(6, tables.Count);
    }

    [Fact]
    public async Task MigrationFailureFailsClosedAndDoesNotSilentlyRecover()
    {
        using SqliteConnection connection = await OpenInMemoryAsync();
        var failingMigrations = new List<Migration>
        {
            new(1, "initial_schema", UsageStateSchema.Migrations[0].UpSql),
            new(2, "bad_migration", "THIS IS NOT VALID SQL;"),
        };
        var migrator = new DatabaseMigrator(failingMigrations);

        await Assert.ThrowsAsync<SqliteException>(
            () => migrator.MigrateAsync(connection, CancellationToken.None));

        Assert.Equal(1, await GetUserVersionAsync(connection));
    }

    [Fact]
    public async Task ProductionFactoryCreatesAndMigratesFileDatabase()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "codex-usage-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        try
        {
            var database = new UsageStateDatabase(tempDir);
            string dbPath = Path.Combine(tempDir, "state.db");

            await using (SqliteConnection connection = await database.CreateConnectionAsync(CancellationToken.None))
            {
                Assert.Equal(UsageStateSchema.LatestVersion, await GetUserVersionAsync(connection));
            }

            Assert.True(File.Exists(dbPath), $"Expected database file at {dbPath}");

            await using (SqliteConnection connection = await database.CreateConnectionAsync(CancellationToken.None))
            {
                Assert.Equal(UsageStateSchema.LatestVersion, await GetUserVersionAsync(connection));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static async Task<SqliteConnection> OpenInMemoryAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Mode=Memory");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<int> GetUserVersionAsync(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task SetUserVersionAsync(SqliteConnection connection, int version)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = string.Format(
            CultureInfo.InvariantCulture,
            "PRAGMA user_version = {0};",
            version);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>> GetTableNamesAsync(SqliteConnection connection)
    {
        HashSet<string> tables = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
