using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Persistence;

/// <summary>
/// Tests for the durable redacted audit store. Each test uses a fresh temp-file
/// SQLite database so that persistence and crash-recovery behavior is exercised
/// against a real file. The class is <see cref="IDisposable"/> to clear connection
/// pools and delete the temp directory between tests.
/// </summary>
public sealed class AuditStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UsageStateDatabase _database;

    public AuditStoreTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "codex-audit-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
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

    private SqliteAuditStore CreateStore() => new(_database);

    private static AuditEntry NewEntry(
        string auditId = "audit-1",
        string namespaceHash = "ns-hash-a",
        string? attemptId = "att-1",
        string? modelId = "model-a",
        string observedAt = "2026-07-12T07:45:00Z",
        AuditQuotaSnapshot? preQuota = null,
        AuditQuotaSnapshot? postQuota = null,
        bool turnCrossedBoundary = false,
        string? outcome = null,
        string? errorCategory = null,
        string? recordedAt = null) =>
        new(
            AuditId: auditId,
            NamespaceHash: namespaceHash,
            AttemptId: attemptId,
            ModelId: modelId,
            ObservedAt: observedAt,
            PreQuota: preQuota,
            PostQuota: postQuota,
            TurnCrossedBoundary: turnCrossedBoundary,
            Outcome: outcome,
            ErrorCategory: errorCategory,
            RecordedAt: recordedAt ?? "2026-07-12T07:46:00Z");

    [Fact]
    public async Task WriteAndReadRoundTrip()
    {
        SqliteAuditStore store = CreateStore();
        AuditEntry entry = NewEntry(
            preQuota: new AuditQuotaSnapshot(UsedPercent: 0, RemainingPercent: 100, ResetsAt: "2026-07-12T12:00:00Z"),
            postQuota: new AuditQuotaSnapshot(UsedPercent: 0, RemainingPercent: 100, ResetsAt: "2026-07-12T17:00:00Z"),
            turnCrossedBoundary: true,
            outcome: "succeeded",
            errorCategory: null);

        await store.WriteAsync(entry, CancellationToken.None);
        AuditEntry? loaded = await store.ReadAsync(entry.AuditId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(entry.AuditId, loaded!.AuditId);
        Assert.Equal(entry.NamespaceHash, loaded.NamespaceHash);
        Assert.Equal(entry.AttemptId, loaded.AttemptId);
        Assert.Equal(entry.ModelId, loaded.ModelId);
        Assert.Equal(entry.ObservedAt, loaded.ObservedAt);
        Assert.Equal(entry.RecordedAt, loaded.RecordedAt);
        Assert.True(loaded.TurnCrossedBoundary);
        Assert.Equal("succeeded", loaded.Outcome);
        Assert.Null(loaded.ErrorCategory);
        Assert.NotNull(loaded.PreQuota);
        Assert.Equal(0, loaded.PreQuota!.UsedPercent);
        Assert.Equal(100, loaded.PreQuota.RemainingPercent);
        Assert.Equal("2026-07-12T12:00:00Z", loaded.PreQuota.ResetsAt);
        Assert.NotNull(loaded.PostQuota);
        Assert.Equal("2026-07-12T17:00:00Z", loaded.PostQuota!.ResetsAt);
    }

    [Fact]
    public async Task ReadMissingReturnsNull()
    {
        SqliteAuditStore store = CreateStore();

        AuditEntry? loaded = await store.ReadAsync("no-such-audit", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task ReadAllReturnsEntriesOrderedByRecordedAtDescending()
    {
        SqliteAuditStore store = CreateStore();

        await store.WriteAsync(
            NewEntry(auditId: "audit-1", recordedAt: "2026-07-12T07:46:00Z"),
            CancellationToken.None);
        await store.WriteAsync(
            NewEntry(auditId: "audit-2", recordedAt: "2026-07-12T07:48:00Z"),
            CancellationToken.None);
        await store.WriteAsync(
            NewEntry(auditId: "audit-3", recordedAt: "2026-07-12T07:47:00Z"),
            CancellationToken.None);

        List<AuditEntry> entries = new();
        await foreach (AuditEntry entry in store.ReadAllAsync(CancellationToken.None))
        {
            entries.Add(entry);
        }

        Assert.Equal(3, entries.Count);
        Assert.Equal(["audit-2", "audit-3", "audit-1"], entries.Select(e => e.AuditId));
    }

    [Fact]
    public async Task WriteWithoutAttemptIdStoresNull()
    {
        SqliteAuditStore store = CreateStore();
        AuditEntry entry = NewEntry(auditId: "audit-no-attempt", attemptId: null);

        await store.WriteAsync(entry, CancellationToken.None);
        AuditEntry? loaded = await store.ReadAsync(entry.AuditId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Null(loaded!.AttemptId);
    }

    [Fact]
    public async Task RawSensitiveDataIsAbsentFromStoredRows()
    {
        SqliteAuditStore store = CreateStore();
        const string rawEmail = "user@example.com";
        const string rawToken = "sk-secret-token";
        const string rawPrompt = "This is a secret prompt";

        await store.WriteAsync(
            NewEntry(
                auditId: "audit-sensitive",
                preQuota: new AuditQuotaSnapshot(0, 100, "2026-07-12T12:00:00Z"),
                outcome: "failed",
                errorCategory: "model_unavailable"),
            CancellationToken.None);

        string dbPath = Path.Combine(_tempDir, "state.db");
        Assert.True(File.Exists(dbPath));
        byte[] bytes;
        // The pooled SQLite connection may still hold the file open; read with
        // sharing so the raw-bytes assertion works on Windows file locking.
        await using (FileStream stream = new(
            dbPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            bytes = memory.ToArray();
        }

        string text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain(rawEmail, text);
        Assert.DoesNotContain(rawToken, text);
        Assert.DoesNotContain(rawPrompt, text);
    }

    [Fact]
    public async Task CrashRecoveryReadsPreviouslyWrittenAudit()
    {
        SqliteAuditStore firstStore = CreateStore();
        AuditEntry entry = NewEntry(auditId: "audit-crash");
        await firstStore.WriteAsync(entry, CancellationToken.None);

        SqliteAuditStore recoveredStore = CreateStore();
        AuditEntry? loaded = await recoveredStore.ReadAsync(entry.AuditId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(entry.AuditId, loaded!.AuditId);
        Assert.Equal(entry.NamespaceHash, loaded.NamespaceHash);
    }
}
