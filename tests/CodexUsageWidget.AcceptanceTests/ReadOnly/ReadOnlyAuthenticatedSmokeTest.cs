using System.Globalization;
using CodexUsageWidget.AcceptanceTests.Testing;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Monitoring;
using CodexUsageWidget.Core.Quota;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using CodexUsageWidget.Infrastructure.Time;
using Xunit;

namespace CodexUsageWidget.AcceptanceTests.ReadOnlySmoke;

/// <summary>
/// Read-only authenticated acceptance test for OpenSpec 7.5.
///
/// The test starts the real Codex App Server, verifies five-hour/weekly quota
/// mapping, freshness, countdown behavior, and the 60-second reconciliation
/// boundary without creating any model turns.
/// </summary>
public sealed class ReadOnlyAuthenticatedSmokeTest
{
    [EnvironmentFact("CODEX_ACCEPTANCE_DATA_PATH")]
    public async Task RealAccountMapsFiveHourAndWeeklyBuckets()
    {
        string codexPath = ResolveCodexPath();
        string dataDirectory = CreateDataDirectory();

        var settings = AppServerSupervisorSettings.Default;
        AppServerSupervisor supervisor = new(
            new SystemProcessHost(),
            new ProcessStartRequest(codexPath, ["app-server"], dataDirectory),
            new ClientInformation("codex-usage-widget-acceptance", "1.0.0", "Codex Usage Widget Acceptance"),
            TimeSpan.FromSeconds(5),
            new TaskDelay(),
            settings,
            healthyDelay: new TaskDelay(),
            graceDelay: new TaskDelay());

        AppServerQuotaSource source = new(supervisor);
        QuotaMonitor monitor = new(
            source,
            new SystemClock(),
            new TaskDelay(),
            pollInterval: TimeSpan.FromSeconds(60),
            staleThreshold: TimeSpan.FromSeconds(120));

        try
        {
            using var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await supervisor.StartAsync(startupCts.Token);
            await monitor.StartAsync(startupCts.Token);

            QuotaSnapshot snapshot = await WaitForFreshSnapshotAsync(monitor, TimeSpan.FromSeconds(60));

            Assert.Equal(MonitoringConnectionState.Connected, snapshot.ConnectionState);
            Assert.True(snapshot.IsFresh, "The first snapshot should be fresh.");
            Assert.True(snapshot.FiveHour.IsAvailable, "The five-hour bucket should be available.");
            Assert.Equal(300L, snapshot.FiveHour.WindowDurationMinutes);
            Assert.InRange(snapshot.FiveHour.UsedPercent, 0, 100);

            Assert.True(snapshot.Weekly.IsAvailable, "The weekly bucket should be available.");
            Assert.True(
                snapshot.Weekly.WindowDurationMinutes is null or >= 10020 and <= 10140,
                "The weekly bucket duration should be close to 10080 minutes.");
            Assert.InRange(snapshot.Weekly.UsedPercent, 0, 100);

            if (snapshot.FiveHour.ResetsAt is not null)
            {
                Assert.True(snapshot.FiveHour.ResetsAt > DateTimeOffset.UtcNow, "The five-hour reset time should be in the future.");
            }

            // Countdown behavior: the local countdown should advance within a few seconds.
            TimeSpan? initialCountdown = snapshot.Countdown;
            Assert.NotNull(initialCountdown);

            await Task.Delay(TimeSpan.FromSeconds(2));
            QuotaSnapshot laterSnapshot = monitor.CurrentSnapshot!;
            Assert.NotNull(laterSnapshot.Countdown);
            Assert.True(laterSnapshot.Countdown < initialCountdown, "The countdown should decrease over time.");

            // 60-second reconciliation: wait for the monitor's poll to refresh the snapshot.
            DateTimeOffset initialSyncedAt = snapshot.SyncedAt;
            QuotaSnapshot reconciled = await WaitForReconciliationAsync(
                monitor,
                initialSyncedAt + TimeSpan.FromSeconds(55),
                TimeSpan.FromSeconds(75));
            Assert.True(reconciled.SyncedAt > initialSyncedAt, "The snapshot should be refreshed by the reconciliation poll.");
        }
        finally
        {
            try { await monitor.StopAsync(); } catch { /* best effort */ }
            await monitor.DisposeAsync();
            try { await supervisor.StopAsync(CancellationToken.None); } catch { /* best effort */ }
            await supervisor.DisposeAsync();
            source.Dispose();

            try
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; the App Server child may still hold files briefly.
            }
        }
    }

    private static string ResolveCodexPath()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("CODEX_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        CodexExecutableResolution resolution = CodexExecutableLocator.CreateSystem().Locate();
        if (!resolution.Found)
        {
            Assert.Fail($"Codex executable not found: {resolution.Diagnostic}");
        }

        return resolution.Command!;
    }

    private static string CreateDataDirectory()
    {
        string? basePath = Environment.GetEnvironmentVariable("CODEX_ACCEPTANCE_DATA_PATH");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException("CODEX_ACCEPTANCE_DATA_PATH is not set.");
        }

        string path = Path.Combine(
            basePath,
            $"acceptance-{DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<QuotaSnapshot> WaitForFreshSnapshotAsync(
        QuotaMonitor monitor,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            QuotaSnapshot? snapshot = monitor.CurrentSnapshot;
            if (snapshot is not null && snapshot.IsFresh && snapshot.ConnectionState == MonitoringConnectionState.Connected)
            {
                return snapshot;
            }

            if (DateTime.UtcNow >= deadline)
            {
                string state = snapshot is null
                    ? "no snapshot"
                    : $"{snapshot.ConnectionState}, fresh={snapshot.IsFresh}";
                throw new TimeoutException($"Did not receive a fresh connected snapshot in time. State: {state}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private static async Task<QuotaSnapshot> WaitForReconciliationAsync(
        QuotaMonitor monitor,
        DateTimeOffset minSyncedAt,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            QuotaSnapshot? snapshot = monitor.CurrentSnapshot;
            if (snapshot is not null && snapshot.SyncedAt > minSyncedAt)
            {
                return snapshot;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The reconciliation poll did not refresh the snapshot in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
