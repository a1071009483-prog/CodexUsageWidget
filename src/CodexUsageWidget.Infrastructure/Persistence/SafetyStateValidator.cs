using System.Data.Common;
using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace CodexUsageWidget.Infrastructure.Persistence;

/// <summary>
/// Read-only <see cref="ISafetyStateValidator"/> backed by <see cref="UsageStateDatabase"/>.
///
/// Opens the durable usage-state database (which runs pending migrations — a
/// migration failure fails closed), then verifies, in read-only fashion:
/// <list type="bullet">
/// <item><c>PRAGMA integrity_check</c> returns <c>ok</c> (else <see cref="SafetyStateFailureKind.Corruption"/>);</item>
/// <item><c>PRAGMA user_version</c> equals <see cref="UsageStateSchema.LatestVersion"/> (else
/// <see cref="SafetyStateFailureKind.MigrationMismatch"/>);</item>
/// <item>all required tables are present in <c>sqlite_master</c> (else
/// <see cref="SafetyStateFailureKind.InconsistentRows"/>);</item>
/// <item>no <c>activation_attempts</c> row has a <c>terminal_outcome</c> while
/// <c>turn_started = 0</c> — a terminal outcome without a started turn is a logical
/// contradiction (else <see cref="SafetyStateFailureKind.InconsistentRows"/>).</item>
/// </list>
///
/// Any exception during open/migration/read is caught and returned as an invalid
/// result (fail-closed); the validator never throws for database/IO errors and
/// never silently rebuilds or repairs state. See design.md decision 4 + risks:
/// "State corruption could erase a live guard → disable activation rather than
/// recreate state automatically."
///
/// This validator performs NO model consumption (<c>thread/start</c>/<c>turn/start</c>)
/// and persists no credentials, raw email, or sensitive payloads.
/// </summary>
public sealed class SafetyStateValidator : ISafetyStateValidator
{
    /// <summary>
    /// The required tables that must all be present in <c>sqlite_master</c>.
    /// </summary>
    private static readonly string[] RequiredTables =
        ["settings", "account_namespaces", "activation_attempts", "notifications", "cleanup_work", "audit_rows"];

    private readonly UsageStateDatabase _database;

    public SafetyStateValidator(UsageStateDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc/>
    public async Task<SafetyStateValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SqliteConnection? connection = null;
        try
        {
            // Opening the connection runs pending migrations. A migration failure
            // (or a corrupt file that cannot be opened) fails closed here.
            connection = await _database.CreateConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            SafetyStateValidationResult integrity =
                await CheckIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!integrity.IsValid)
            {
                return integrity;
            }

            SafetyStateValidationResult version =
                await CheckUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!version.IsValid)
            {
                return version;
            }

            SafetyStateValidationResult tables =
                await CheckRequiredTablesAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!tables.IsValid)
            {
                return tables;
            }

            SafetyStateValidationResult rows =
                await CheckRowInvariantsAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!rows.IsValid)
            {
                return rows;
            }

            return SafetyStateValidationResult.Valid;
        }
        catch (SqliteException ex)
        {
            // A SQLite-level failure during open/migration/read. Distinguish a
            // cannot-open condition (missing file/directory or sharing violation —
            // SQLITE_CANTOPEN = 14) from structural damage so the caller can report
            // an unreadable database distinctly from a corrupted one. Both fail
            // closed; the validator never rebuilds state.
            SafetyStateFailureKind kind = IsCannotOpen(ex)
                ? SafetyStateFailureKind.Unreadable
                : SafetyStateFailureKind.Corruption;
            return SafetyStateValidationResult.Failed(
                kind,
                "The safety-state database could not be opened or read: " + ex.SqliteErrorCode);
        }
        catch (IOException ex)
        {
            // A filesystem-level read failure distinct from structural corruption.
            return SafetyStateValidationResult.Failed(
                SafetyStateFailureKind.Unreadable,
                "The safety-state database file could not be read: " + ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any other unexpected failure fails closed as a durable-write failure
            // rather than permitting activation against an indeterminate state.
            return SafetyStateValidationResult.Failed(
                SafetyStateFailureKind.DurableWriteFailure,
                "The safety-state database could not be validated: " + ex.Message);
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Runs <c>PRAGMA integrity_check;</c> and fails closed as
    /// <see cref="SafetyStateFailureKind.Corruption"/> unless the result is
    /// exactly <c>ok</c>.
    /// </summary>
    private static async Task<SafetyStateValidationResult> CheckIntegrityAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        string integrityCheck = await ExecuteScalarAsStringAsync(
            connection, "PRAGMA integrity_check;", cancellationToken).ConfigureAwait(false);
        if (!string.Equals(integrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return SafetyStateValidationResult.Failed(
                SafetyStateFailureKind.Corruption,
                "integrity_check did not return ok; the database is structurally damaged.");
        }

        return SafetyStateValidationResult.Valid;
    }

    /// <summary>
    /// Reads <c>PRAGMA user_version</c> and fails closed as
    /// <see cref="SafetyStateFailureKind.MigrationMismatch"/> unless it equals
    /// <see cref="UsageStateSchema.LatestVersion"/>. Because migrations run on
    /// connection open, a still-stale version means migration failed to advance
    /// the schema — an indeterminate state that must disable activation.
    /// </summary>
    private static async Task<SafetyStateValidationResult> CheckUserVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        int currentVersion = await GetUserVersionAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        if (currentVersion != UsageStateSchema.LatestVersion)
        {
            return SafetyStateValidationResult.Failed(
                SafetyStateFailureKind.MigrationMismatch,
                $"user_version {currentVersion} does not match expected {UsageStateSchema.LatestVersion}.");
        }

        return SafetyStateValidationResult.Valid;
    }

    /// <summary>
    /// Verifies every required table is present in <c>sqlite_master</c>; fails
    /// closed as <see cref="SafetyStateFailureKind.InconsistentRows"/> if any is
    /// missing (a damaged schema cannot safely back the at-most-once guard).
    /// </summary>
    private static async Task<SafetyStateValidationResult> CheckRequiredTablesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        HashSet<string> tables = await GetTableNamesAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        foreach (string required in RequiredTables)
        {
            if (!tables.Contains(required))
            {
                return SafetyStateValidationResult.Failed(
                    SafetyStateFailureKind.InconsistentRows,
                    $"Required table '{required}' is missing from the safety-state database.");
            }
        }

        return SafetyStateValidationResult.Valid;
    }

    /// <summary>
    /// Checks the row invariant: no <c>activation_attempts</c> row may have a
    /// non-null <c>terminal_outcome</c> while <c>turn_started = 0</c>. A terminal
    /// outcome without a started turn is a logical contradiction — the
    /// anti-repeat state is internally inconsistent and must disable activation
    /// rather than risk a duplicate generation. Fails closed as
    /// <see cref="SafetyStateFailureKind.InconsistentRows"/>.
    /// </summary>
    private static async Task<SafetyStateValidationResult> CheckRowInvariantsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        long inconsistentCount = await ExecuteScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM activation_attempts WHERE terminal_outcome IS NOT NULL AND turn_started = 0;",
            cancellationToken).ConfigureAwait(false);
        if (inconsistentCount > 0)
        {
            return SafetyStateValidationResult.Failed(
                SafetyStateFailureKind.InconsistentRows,
                "Found activation_attempts rows with a terminal outcome but turn_started = 0; anti-repeat state is internally inconsistent.");
        }

        return SafetyStateValidationResult.Valid;
    }

    /// <summary>
    /// Determines whether a <see cref="SqliteException"/> represents a
    /// cannot-open condition (<c>SQLITE_CANTOPEN</c> = 14), indicating the
    /// database file or its directory is missing, locked, or otherwise
    /// unreadable — distinct from structural corruption.
    /// </summary>
    private static bool IsCannotOpen(SqliteException exception)
    {
        const int sqliteCantOpen = 14;
        return exception.SqliteErrorCode == sqliteCantOpen;
    }

    private static async Task<string> ExecuteScalarAsStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<long> ExecuteScalarInt64Async(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> GetUserVersionAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<HashSet<string>> GetTableNamesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        HashSet<string> tables = new(StringComparer.OrdinalIgnoreCase);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
