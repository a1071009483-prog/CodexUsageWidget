using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using CodexUsageWidget.Infrastructure.Time;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class AppServerQuotaSourceTests : IDisposable
{
    private readonly string _tempDir;

    public AppServerQuotaSourceTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "codex-qsource-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup on test runners that may lock executable files.
        }
    }

    [Fact]
    public async Task ReadAsyncReturnsFailureWhenNoSessionExists()
    {
        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .HangAfterEof();

        AppServerSupervisor supervisor = CreateSupervisor(script);
        var source = new AppServerQuotaSource(supervisor);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        QuotaSourceResult result = await source.ReadAsync(cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("session is not available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        await supervisor.StopAsync(CancellationToken.None);
        await supervisor.DisposeAsync();
        source.Dispose();
    }

    [Fact]
    public async Task ReadAsyncMapsPrimaryAndSecondaryBuckets()
    {
        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .ExpectRequest(
                "account/rateLimits/read",
                new
                {
                    rateLimits = new
                    {
                        limitId = "limit-1",
                        limitName = "Pro",
                        planType = "ChatGPT Pro",
                        primary = new { usedPercent = 0, resetsAt = 1752345600L, windowDurationMins = 300L },
                        secondary = new { usedPercent = 12, resetsAt = 1752777600L, windowDurationMins = 10080L },
                    },
                })
            .HangAfterEof();

        AppServerSupervisor supervisor = CreateSupervisor(script);
        var source = new AppServerQuotaSource(supervisor);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task startTask = supervisor.StartAsync(cts.Token);

        await WaitForSessionAsync(supervisor, TimeSpan.FromSeconds(5));

        QuotaSourceResult result = await source.ReadAsync(cts.Token);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Snapshot);
        Assert.Equal("ChatGPT Pro", result.Snapshot!.PlanType);
        Assert.NotNull(result.Snapshot.Primary);
        Assert.Equal(0, result.Snapshot.Primary!.UsedPercent);
        Assert.Equal(300L, result.Snapshot.Primary.WindowDurationMins);
        Assert.True(result.Snapshot.BucketsByLimitId?.ContainsKey("secondary"));
        Assert.Equal(12, result.Snapshot.BucketsByLimitId!["secondary"].UsedPercent);
        Assert.Equal(10080L, result.Snapshot.BucketsByLimitId["secondary"].WindowDurationMins);

        await supervisor.StopAsync(CancellationToken.None);
        await startTask;
        await supervisor.DisposeAsync();
        source.Dispose();
    }

    [Fact]
    public async Task ReadAsyncKeepsTopLevelCodexWeeklyWindowDistinctFromOtherLimitFamilies()
    {
        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .ExpectRequest(
                "account/rateLimits/read",
                new
                {
                    rateLimits = new
                    {
                        limitId = "codex",
                        planType = "plus",
                        primary = new { usedPercent = 24, resetsAt = 1_787_836_013L, windowDurationMins = 300L },
                        secondary = new { usedPercent = 4, resetsAt = 1_788_327_847L, windowDurationMins = 10080L },
                    },
                    rateLimitsByLimitId = new Dictionary<string, object>
                    {
                        ["base_model_inference"] = new
                        {
                            limitId = "base_model_inference",
                            limitName = "gpt-reserve",
                            planType = "plus",
                            primary = new { usedPercent = 0, resetsAt = 1_788_424_259L, windowDurationMins = 10080L },
                        },
                        ["codex"] = new
                        {
                            limitId = "codex",
                            planType = "plus",
                            primary = new { usedPercent = 24, resetsAt = 1_787_836_013L, windowDurationMins = 300L },
                            secondary = new { usedPercent = 4, resetsAt = 1_788_327_847L, windowDurationMins = 10080L },
                        },
                    },
                })
            .HangAfterEof();

        AppServerSupervisor supervisor = CreateSupervisor(script);
        var source = new AppServerQuotaSource(supervisor);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task startTask = supervisor.StartAsync(cts.Token);
        await WaitForSessionAsync(supervisor, TimeSpan.FromSeconds(5));

        QuotaSourceResult result = await source.ReadAsync(cts.Token);
        QuotaSnapshot normalized = QuotaNormalizer.Normalize(
            result.Snapshot!,
            DateTimeOffset.FromUnixTimeSeconds(1_787_800_000L));

        Assert.True(normalized.Weekly.IsAvailable);
        Assert.Equal(4, normalized.Weekly.UsedPercent);
        Assert.Equal(10080L, normalized.Weekly.WindowDurationMinutes);

        await supervisor.StopAsync(CancellationToken.None);
        await startTask;
        await supervisor.DisposeAsync();
        source.Dispose();
    }

    [Fact]
    public async Task UpdatedEventForwardsSupervisorNotifications()
    {
        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .EmitNotification(
                "account/rateLimits/updated",
                new { rateLimits = new { primary = new { usedPercent = 7 } } })
            .HangAfterEof();

        AppServerSupervisor supervisor = CreateSupervisor(script);
        var source = new AppServerQuotaSource(supervisor);

        var updatedCount = 0;
        source.Updated += (_, _) => Interlocked.Increment(ref updatedCount);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task startTask = supervisor.StartAsync(cts.Token);

        await WaitForNotificationAsync(() => Volatile.Read(ref updatedCount), 1, TimeSpan.FromSeconds(5));

        Assert.Equal(1, updatedCount);

        await supervisor.StopAsync(CancellationToken.None);
        await startTask;
        await supervisor.DisposeAsync();
        source.Dispose();
    }

    private AppServerSupervisor CreateSupervisor(FakeAppServerScriptBuilder script)
    {
        string scriptPath = script.WriteToFile(_tempDir);
        var settings = new AppServerSupervisorSettings(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(60));

        return new AppServerSupervisor(
            new SystemProcessHost(),
            new ProcessStartRequest(FakeServerPath(), [scriptPath]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget QuotaSource Test"),
            TimeSpan.FromSeconds(2),
            new TaskDelay(),
            settings);
    }

    private static string FakeServerPath()
    {
        string testDir = AppContext.BaseDirectory;
        string exeName = "FakeCodexAppServer.exe";
        string path = Path.Combine(testDir, exeName);
        if (File.Exists(path))
        {
            return path;
        }

        string dllPath = Path.Combine(testDir, "FakeCodexAppServer.dll");
        return dllPath;
    }

    private static object InitializeResult() => new
    {
        codexHome = "C:\\Codex",
        platformFamily = "windows",
        platformOs = "windows",
        userAgent = "fake-codex-qsource",
    };

    private static async Task WaitForSessionAsync(
        AppServerSupervisor supervisor,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (supervisor.CurrentGeneration is null)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Expected a supervisor session but none was published.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    private static async Task WaitForNotificationAsync(
        Func<int> getObserved,
        int count,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (getObserved() < count)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Expected {count} notifications but observed {getObserved()}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
