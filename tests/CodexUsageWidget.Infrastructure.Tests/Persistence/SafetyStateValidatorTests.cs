using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Persistence;

/// <summary>
/// Tests for the fail-closed safety-state validator. Each test uses a fresh
/// temp-file SQLite database so structural corruption, missing tables, and row
/// invariants are exercised against a real persisted file. The class is
/// <see cref="IDisposable"/> to clear connection pools and delete the temp
/// directory between tests (mirroring <see cref="ActivationLockStoreTests"/>).
/// </summary>
public sealed class SafetyStateValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UsageStateDatabase _database;

    public SafetyStateValidatorTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "codex-safety-state-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
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

    private SafetyStateValidator CreateValidator() => new(_database);

    [Fact]
    public async Task ValidFreshDatabasePassesValidation()
    {
        // A freshly created + migrated database is the normal startup path and
        // must validate cleanly so activation is permitted.
        SafetyStateValidator validator = CreateValidator();

        SafetyStateValidationResult result = await validator.ValidateAsync(CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(SafetyStateFailureKind.None, result.FailureKind);
    }

    [Fact]
    public async Task CorruptedDatabaseFailsClosedAndDoesNotThrow()
    {
        // First open + migrate so a valid state.db exists, then close all pooled
        // connections and corrupt the file at the byte level. The validator must
        // fail closed (return invalid) rather than silently rebuilding the state
        // or throwing.
        await MigrateFreshDatabaseAsync();
        SqliteConnection.ClearAllPools();

        CorruptDatabaseFile();

        SafetyStateValidator validator = CreateValidator();
        SafetyStateValidationResult result =
            await validator.ValidateAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotEqual(SafetyStateFailureKind.None, result.FailureKind);
        // Byte-level corruption manifests as either structural damage (integrity
        // check failure) or an unreadable/open failure; both are fail-closed.
        Assert.True(
            result.FailureKind == SafetyStateFailureKind.Corruption
                || result.FailureKind == SafetyStateFailureKind.Unreadable
                || result.FailureKind == SafetyStateFailureKind.DurableWriteFailure,
            $"Unexpected failure kind for corrupted database: {result.FailureKind}");
    }

    [Fact]
    public async Task MissingTableFailsClosedAsInconsistentRows()
    {
        // Dropping a required table after migration simulates a damaged schema.
        // The validator must detect the missing table and fail closed.
        await MigrateFreshDatabaseAsync();
        SqliteConnection.ClearAllPools();

        await ExecuteRawAsync("DROP TABLE activation_attempts;");

        SafetyStateValidator validator = CreateValidator();
        SafetyStateValidationResult result =
            await validator.ValidateAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(SafetyStateFailureKind.InconsistentRows, result.FailureKind);
    }

    [Fact]
    public async Task InconsistentRowsFailClosed()
    {
        // A row that has a terminal outcome but turn_started = 0 is a logical
        // contradiction: a terminal outcome implies a turn was started. Its
        // presence means the anti-repeat state is internally inconsistent and
        // must disable activation rather than risk a duplicate generation.
        await MigrateFreshDatabaseAsync();
        SqliteConnection.ClearAllPools();

        await ExecuteRawAsync("""
INSERT INTO activation_attempts (
    attempt_id, namespace_hash, workspace_scope, window_key, window_kind,
    suppression_deadline, observed_at, attempt_at, pre_used_percent,
    pre_resets_at, model_id, turn_started, terminal_outcome,
    post_used_percent, post_resets_at, cleanup_state
) VALUES (
    'att-bad', 'ns-bad', 'global', 'win-bad', 'authoritative',
    '2026-07-12T13:00:00Z', '2026-07-12T07:45:00Z', '2026-07-12T07:46:00Z', 0,
    NULL, NULL, 0, 'succeeded',
    NULL, NULL, 'none'
);
""");

        SafetyStateValidator validator = CreateValidator();
        SafetyStateValidationResult result =
            await validator.ValidateAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(SafetyStateFailureKind.InconsistentRows, result.FailureKind);
    }

    [Fact]
    public async Task UnreadableDatabaseFailsClosedAndDoesNotThrow()
    {
        // Construct and migrate a valid database, close all pooled connections,
        // then delete the entire directory so the database file can no longer be
        // opened (SQLite returns SQLITE_CANTOPEN). The validator must fail closed
        // without throwing. This is reliable across WSL/Windows without depending
        // on filesystem ACLs.
        await MigrateFreshDatabaseAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        SafetyStateValidator validator = CreateValidator();
        SafetyStateValidationResult result =
            await validator.ValidateAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.True(
            result.FailureKind == SafetyStateFailureKind.Unreadable
                || result.FailureKind == SafetyStateFailureKind.DurableWriteFailure,
            $"Unexpected failure kind for unreadable database: {result.FailureKind}");
    }

    [Fact]
    public async Task ValidationIsIdempotentAcrossRepeatedCalls()
    {
        // Repeated validation against a healthy database must remain valid and
        // must not mutate state (no silent rebuild).
        SafetyStateValidator validator = CreateValidator();

        SafetyStateValidationResult first = await validator.ValidateAsync(CancellationToken.None);
        SafetyStateValidationResult second = await validator.ValidateAsync(CancellationToken.None);

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
    }

    [Fact]
    public async Task ValidDatabaseWithConsistentRowsPasses()
    {
        // A row with turn_started = 1 and a terminal outcome is the normal
        // post-completion state and must NOT trip the invariant.
        await MigrateFreshDatabaseAsync();
        SqliteConnection.ClearAllPools();

        await ExecuteRawAsync("""
INSERT INTO activation_attempts (
    attempt_id, namespace_hash, workspace_scope, window_key, window_kind,
    suppression_deadline, observed_at, attempt_at, pre_used_percent,
    pre_resets_at, model_id, turn_started, terminal_outcome,
    post_used_percent, post_resets_at, cleanup_state
) VALUES (
    'att-ok', 'ns-ok', 'global', 'win-ok', 'authoritative',
    '2026-07-12T13:00:00Z', '2026-07-12T07:45:00Z', '2026-07-12T07:46:00Z', 0,
    NULL, 'gpt-foo', 1, 'succeeded',
    1, '2026-07-12T13:05:00Z', 'completed'
);
""");

        SafetyStateValidator validator = CreateValidator();
        SafetyStateValidationResult result =
            await validator.ValidateAsync(CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(SafetyStateFailureKind.None, result.FailureKind);
    }

    private async Task MigrateFreshDatabaseAsync()
    {
        await using SqliteConnection connection =
            await _database.CreateConnectionAsync(CancellationToken.None);
    }

    private async Task ExecuteRawAsync(string sql)
    {
        await using SqliteConnection connection =
            await _database.CreateConnectionAsync(CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private void CorruptDatabaseFile()
    {
        string dbPath = Path.Combine(_tempDir, "state.db");
        Assert.True(File.Exists(dbPath), $"Expected database file at {dbPath}");

        byte[] bytes = File.ReadAllBytes(dbPath);
        // Overwrite the middle of the file with random bytes, leaving the header
        // intact so the file may still open but fail integrity checks (or fail to
        // open if page structures are damaged). Either path fails closed.
        Random random = new(Seed: 0xC0FFEE);
        int start = Math.Max(1, bytes.Length / 4);
        int length = Math.Max(1, bytes.Length / 2);
        for (int i = start; i < Math.Min(start + length, bytes.Length); i++)
        {
            bytes[i] = (byte)random.Next(0, 256);
        }

        File.WriteAllBytes(dbPath, bytes);
    }
}
