using System.Globalization;
using System.Text;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Logging;
using CodexUsageWidget.Infrastructure.Persistence;
using CodexUsageWidget.Infrastructure.Security;
using CodexUsageWidget.Infrastructure.Settings;
using CodexUsageWidget.Infrastructure.Tests.Testing;
using CodexUsageWidget.Infrastructure.Tests.Windows;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Security;

/// <summary>
/// Automated sensitive-data scans proving that logs, SQLite rows, settings files,
/// crash reports, and audit exports contain no tokens, cookies, raw credentials,
/// prompt/response bodies, or unredacted workspace content.
/// </summary>
public sealed class SensitiveDataScanTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UsageStateDatabase _database;
    private readonly ManualClock _clock;

    public SensitiveDataScanTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "codex-sensitive-data-scan-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        _database = new UsageStateDatabase(_tempDir);
        _clock = new ManualClock();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoggerOutputRedactsTokensEmailsAndPaths()
    {
        var output = new StringWriter();
        var logger = new JsonRedactingLogger(output, _clock);

        var properties = new Dictionary<string, string?>
        {
            ["account"] = "user@example.com",
            ["workspacePath"] = "C:\\Users\\Secret\\project",
            ["apiKey"] = "sk-secret-token",
            ["authorization"] = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9",
            ["safeValue"] = "visible-value",
        };

        await logger.WriteAsync(
            new StructuredLogEvent(RedactingLogLevel.Warning, "SensitiveInput", properties),
            CancellationToken.None);

        string content = output.ToString();
        SensitiveDataAsserts.AssertContainsNoSensitiveData(content);
        Assert.Contains("\"safeValue\":\"visible-value\"", content, StringComparison.Ordinal);
        Assert.Contains("\"account\":\"[REDACTED]\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoggerDropsPropertiesWithSensitiveKeyFragments()
    {
        var output = new StringWriter();
        var logger = new JsonRedactingLogger(output, _clock);

        var properties = new Dictionary<string, string?>
        {
            ["prompt"] = "secret-prompt-body",
            ["response"] = "secret-response-body",
            ["turnOutput"] = "secret-response-body",
        };

        await logger.WriteAsync(
            new StructuredLogEvent(RedactingLogLevel.Information, "GenerationEvent", properties),
            CancellationToken.None);

        string content = output.ToString();
        Assert.DoesNotContain("secret-prompt-body", content, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-response-body", content, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsFileContainsOnlyBooleanPreferences()
    {
        var fileSystem = new FakeAppFileSystem();
        const string path = @"C:\AppData\CodexUsageWidget\settings.json";
        var store = new JsonSettingsStore(fileSystem, path);

        await store.SaveAsync(new WidgetSettings(false, false), CancellationToken.None);

        string content = fileSystem.Files[path];
        SensitiveDataAsserts.AssertContainsNoSensitiveData(content);
        Assert.Contains("\"StartWithWindows\":false", content, StringComparison.Ordinal);
        Assert.Contains("\"IsAutomationEnabled\":false", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrashReportRedactsTokenInMessage()
    {
        var fileSystem = new FakeAppFileSystem();
        var writer = new CrashReportWriter(fileSystem, _clock, @"C:\AppData\CodexUsageWidget\crashes", "CodexUsageWidget");

        string? path = await writer.WriteAsync(
            new InvalidOperationException("Unexpected apiKey sk-secret-token in request"),
            CancellationToken.None);

        Assert.NotNull(path);
        Assert.True(fileSystem.Files.TryGetValue(path!, out string? content));
        SensitiveDataAsserts.AssertContainsNoSensitiveData(content);
        Assert.Contains("\"message\":\"[REDACTED]\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditStoreRowsContainNoSensitiveData()
    {
        SqliteAuditStore store = new(_database);
        const string rawEmail = "user@example.com";
        const string rawToken = "sk-secret-token";
        const string rawPrompt = "secret-prompt-body";

        await store.WriteAsync(
            new AuditEntry(
                AuditId: "audit-scan-1",
                NamespaceHash: "ns-hash-safe",
                AttemptId: "attempt-1",
                ModelId: "gpt-lite",
                ObservedAt: "2026-07-12T08:00:00Z",
                PreQuota: new AuditQuotaSnapshot(0, 100, "2026-07-12T13:00:00Z"),
                PostQuota: new AuditQuotaSnapshot(0, 100, "2026-07-12T18:00:00Z"),
                TurnCrossedBoundary: true,
                Outcome: "succeeded",
                ErrorCategory: null,
                RecordedAt: "2026-07-12T08:01:00Z"),
            CancellationToken.None);

        string text = SensitiveDataAsserts.ReadDatabaseText(_tempDir);
        Assert.DoesNotContain(rawEmail, text, StringComparison.Ordinal);
        Assert.DoesNotContain(rawToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(rawPrompt, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditExportContainsNoSensitiveData()
    {
        SqliteAuditStore store = new(_database);
        const string rawToken = "sk-secret-token";

        await store.WriteAsync(
            new AuditEntry(
                AuditId: "audit-export-1",
                NamespaceHash: Convert.ToHexString(Encoding.UTF8.GetBytes(rawToken)).ToLowerInvariant(),
                AttemptId: "attempt-1",
                ModelId: "gpt-lite",
                ObservedAt: "2026-07-12T08:00:00Z",
                PreQuota: new AuditQuotaSnapshot(0, 100, "2026-07-12T13:00:00Z"),
                PostQuota: null,
                TurnCrossedBoundary: false,
                Outcome: "failed",
                ErrorCategory: "model_unavailable",
                RecordedAt: "2026-07-12T08:01:00Z"),
            CancellationToken.None);

        List<AuditEntry> entries = new();
        await foreach (AuditEntry entry in store.ReadAllAsync(CancellationToken.None))
        {
            entries.Add(entry);
        }

        string export = JsonSerializer.Serialize(entries);
        SensitiveDataAsserts.AssertContainsNoSensitiveData(export);
        Assert.Contains("\"NamespaceHash\":\"", export, StringComparison.Ordinal);
        Assert.DoesNotContain(rawToken, export, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivationLockStoreRowsContainNoSensitiveData()
    {
        ActivationLockStore store = new(_database);
        const string rawToken = "Bearer super-secret";

        await store.TryAcquireAsync(
            new ActivationAttempt(
                AttemptId: "attempt-scan-1",
                NamespaceHash: "ns-hash",
                WorkspaceScope: "workspace-safe",
                WindowKey: "window-1",
                WindowKind: "local-eligibility",
                SuppressionDeadline: "2026-07-12T18:00:00Z",
                ObservedAt: "2026-07-12T08:00:00Z",
                AttemptAt: "2026-07-12T08:00:01Z",
                PreUsedPercent: 0,
                PreResetsAt: "2026-07-12T13:00:00Z",
                ModelId: null,
                TurnStarted: false,
                TerminalOutcome: null,
                PostUsedPercent: null,
                PostResetsAt: null,
                CleanupState: "none"),
            CancellationToken.None);

        string text = SensitiveDataAsserts.ReadDatabaseText(_tempDir);
        Assert.DoesNotContain(rawToken, text, StringComparison.Ordinal);
        Assert.Contains("attempt-scan-1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupWorkStoreRowsContainNoSensitiveData()
    {
        SqliteCleanupWorkStore store = new(_database);
        const string rawToken = "sk-live-abc123";

        await store.EnqueueAsync("attempt-1", "thread-safe-id", CancellationToken.None);

        string text = SensitiveDataAsserts.ReadDatabaseText(_tempDir);
        Assert.DoesNotContain(rawToken, text, StringComparison.Ordinal);
        Assert.Contains("thread-safe-id", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FullDatabaseScanAfterMixedWritesContainsNoSensitiveData()
    {
        SqliteAuditStore auditStore = new(_database);
        ActivationLockStore lockStore = new(_database);
        SqliteCleanupWorkStore cleanupStore = new(_database);

        await auditStore.WriteAsync(
            new AuditEntry(
                AuditId: "audit-full-1",
                NamespaceHash: "ns-hash",
                AttemptId: "attempt-full-1",
                ModelId: "gpt-lite",
                ObservedAt: "2026-07-12T08:00:00Z",
                PreQuota: new AuditQuotaSnapshot(0, 100, "2026-07-12T13:00:00Z"),
                PostQuota: new AuditQuotaSnapshot(0, 100, "2026-07-12T18:00:00Z"),
                TurnCrossedBoundary: true,
                Outcome: "succeeded",
                ErrorCategory: null,
                RecordedAt: "2026-07-12T08:01:00Z"),
            CancellationToken.None);

        await lockStore.TryAcquireAsync(
            new ActivationAttempt(
                AttemptId: "attempt-full-1",
                NamespaceHash: "ns-hash",
                WorkspaceScope: "workspace-safe",
                WindowKey: "window-1",
                WindowKind: "local-eligibility",
                SuppressionDeadline: "2026-07-12T18:00:00Z",
                ObservedAt: "2026-07-12T08:00:00Z",
                AttemptAt: "2026-07-12T08:00:01Z",
                PreUsedPercent: 0,
                PreResetsAt: "2026-07-12T13:00:00Z",
                ModelId: "gpt-lite",
                TurnStarted: true,
                TerminalOutcome: "succeeded",
                PostUsedPercent: 0,
                PostResetsAt: "2026-07-12T18:00:00Z",
                CleanupState: "pending"),
            CancellationToken.None);

        await cleanupStore.EnqueueAsync("attempt-full-1", "thread-1", CancellationToken.None);

        string text = SensitiveDataAsserts.ReadDatabaseText(_tempDir);
        SensitiveDataAsserts.AssertContainsNoSensitiveData(text);
    }
}
