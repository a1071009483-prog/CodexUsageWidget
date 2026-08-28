using System.Globalization;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Monitoring;
using CodexUsageWidget.Core.Quota;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using CodexUsageWidget.Infrastructure.Tests.Testing;
using CodexUsageWidget.Infrastructure.Time;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

/// <summary>
/// End-to-end tests that launch the real <see cref="FakeCodexAppServer.Program"/>
/// process through <see cref="SystemProcessHost"/>. These tests exercise stdio
/// JSON-RPC, process lifecycle, supervisor restart, and notification forwarding
/// against a scripted fake Codex App Server.
/// </summary>
public sealed class AppServerEndToEndTests : IDisposable
{
    private readonly string _tempDir;

    public AppServerEndToEndTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "codex-e2e-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
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
    public async Task HandshakeAndReadRateLimits()
    {
        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .ExpectRequest("account/rateLimits/read", RateLimitsResult(usedPercent: 37))
            .WaitForEof()
            .Exit(0);

        AppServerProcess process = CreateProcess(script);

        AppServerSession session = await process
            .StartAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        RateLimitsReadResponse response = await session.Gateway
            .ReadRateLimitsAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(response.RateLimits.Primary);
        Assert.Equal(37, response.RateLimits.Primary!.UsedPercent);

        await process.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await process.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SupervisorRestartsAfterUnexpectedExit()
    {
        // The fake server completes one handshake+read cycle and then exits. The
        // supervisor must start a second process so that a later read can succeed.
        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .ExpectRequest("account/rateLimits/read", RateLimitsResult(usedPercent: 10))
            .Exit(1);

        AppServerSupervisor supervisor = CreateSupervisor(script, healthyIntervalSeconds: 60);

        var sessions = new List<AppServerGenerationSession>();
        supervisor.SessionPublished += (_, args) => sessions.Add(args.Generation);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task startTask = supervisor.StartAsync(cts.Token);

        try
        {
            await WaitForSessionsAsync(sessions, count: 1, timeout: TimeSpan.FromSeconds(10));

            RateLimitsReadResponse first = await sessions[0].Session.Gateway
                .ReadRateLimitsAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(10, first.RateLimits.Primary!.UsedPercent);

            await WaitForSessionsAsync(sessions, count: 2, timeout: TimeSpan.FromSeconds(20), supervisorForDiagnostics: supervisor);

            RateLimitsReadResponse second = await sessions[1].Session.Gateway
                .ReadRateLimitsAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(10, second.RateLimits.Primary!.UsedPercent);

            await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await startTask.WaitAsync(TimeSpan.FromSeconds(10));
            await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await CleanupSupervisorAsync(supervisor, cts, startTask);
        }
    }

    [Fact]
    public async Task SupervisorForwardsRateLimitNotification()
    {
        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .EmitNotification(
                "account/rateLimits/updated",
                new { rateLimits = new { primary = new { usedPercent = 42 } } })
            .HangAfterEof();

        AppServerSupervisor supervisor = CreateSupervisor(script, healthyIntervalSeconds: 60);

        var notifications = new List<RateLimitSnapshot>();
        supervisor.RateLimitsUpdated += (_, args) => notifications.Add(args.RateLimits);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task startTask = supervisor.StartAsync(cts.Token);

        try
        {
            await WaitForNotificationsAsync(notifications, count: 1, timeout: TimeSpan.FromSeconds(5));

            Assert.Single(notifications);
            Assert.Equal(42, notifications[0].Primary!.UsedPercent);

            await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await startTask.WaitAsync(TimeSpan.FromSeconds(10));
            await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await CleanupSupervisorAsync(supervisor, cts, startTask);
        }
    }

    [Fact]
    public async Task SupervisorSkipsRetiredGenerationFrames()
    {
        // Each fresh fake-server process reads the same script from the start, so we
        // drive the per-generation notification value through an environment variable.
        // gen1 emits usedPercent 7 and exits; after we observe it we change the env var
        // to 8 so gen2 emits a different notification. Any late frame from gen1 must be
        // dropped by the supervisor.
        const string envName = "FAKE_CODEX_NOTIFY_VALUE";
        Environment.SetEnvironmentVariable(
            envName,
            NotificationLine(usedPercent: 7));

        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .WriteEnvironmentVariable(envName)
            .Exit(0);

        AppServerSupervisor supervisor = CreateSupervisor(script, healthyIntervalSeconds: 60);

        var notifications = new List<RateLimitSnapshot>();
        supervisor.RateLimitsUpdated += (_, args) =>
        {
            notifications.Add(args.RateLimits);
            if (notifications.Count == 1)
            {
                Environment.SetEnvironmentVariable(envName, NotificationLine(usedPercent: 8));
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task startTask = supervisor.StartAsync(cts.Token);

        try
        {
            await WaitForNotificationsAsync(notifications, count: 2, timeout: TimeSpan.FromSeconds(20));

            Assert.Equal(7, notifications[0].Primary!.UsedPercent);
            Assert.Equal(8, notifications[1].Primary!.UsedPercent);

            await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await startTask.WaitAsync(TimeSpan.FromSeconds(10));
            await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await CleanupSupervisorAsync(supervisor, cts, startTask);
        }
    }

    [Fact]
    public async Task MonitorMarksFiveHourSnapshotStaleAfterThreshold()
    {
        long resetsAtFiveHour = new DateTimeOffset(2026, 7, 12, 13, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long resetsAtWeekly = new DateTimeOffset(2026, 7, 19, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .ExpectRequest("account/rateLimits/read", RateLimitsResult(0, 0, resetsAtFiveHour, resetsAtWeekly))
            .HangAfterEof();

        var clock = new ManualClock();
        var delay = new ManualDelay(clock);
        AppServerSupervisor supervisor = CreateSupervisor(
            script,
            healthyIntervalSeconds: 60,
            backoffDelay: delay,
            healthyDelay: delay);

        var sessions = new List<AppServerGenerationSession>();
        supervisor.SessionPublished += (_, args) => sessions.Add(args.Generation);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task startTask = supervisor.StartAsync(cts.Token);

        try
        {
            await WaitForSessionsAsync(sessions, count: 1, timeout: TimeSpan.FromSeconds(10));

            using var quotaSource = new AppServerQuotaSource(supervisor);
            await using var monitor = new QuotaMonitor(
                quotaSource,
                clock,
                delay,
                pollInterval: TimeSpan.FromHours(1),
                staleThreshold: TimeSpan.FromSeconds(120),
                notificationDebounce: TimeSpan.Zero);

            await monitor.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            QuotaSnapshot? first = monitor.CurrentSnapshot;
            Assert.NotNull(first);
            Assert.True(first.IsFresh);
            Assert.True(first.FiveHour.IsAvailable);
            Assert.Equal(0, first.FiveHour.UsedPercent);
            Assert.Equal(100, first.FiveHour.RemainingPercent);

            var staleTcs = new TaskCompletionSource();
            monitor.SnapshotChanged += (_, s) =>
            {
                if (!s.IsFresh)
                {
                    staleTcs.TrySetResult();
                }
            };

            await delay.AdvanceAsync(TimeSpan.FromSeconds(121));
            await staleTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            QuotaSnapshot? stale = monitor.CurrentSnapshot;
            Assert.NotNull(stale);
            Assert.False(stale.IsFresh);
            Assert.Equal(0, stale.FiveHour.UsedPercent);
            Assert.Equal(100, stale.FiveHour.RemainingPercent);

            ActivationEligibilityResult eligibility = ActivationEligibility.Evaluate(
                stale,
                automationEnabled: true,
                activeAttempt: null,
                clock.UtcNow);
            Assert.False(eligibility.IsEligible);
            Assert.Equal("stale", eligibility.Reason);

            await monitor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await CleanupSupervisorAsync(supervisor, cts, startTask);
        }
    }

    [Fact]
    public async Task MonitorDetectsExternalUsageBeforeActivation()
    {
        long initialResetsAtFiveHour = new DateTimeOffset(2026, 7, 12, 13, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long externalResetsAtFiveHour = new DateTimeOffset(2026, 7, 12, 13, 5, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long resetsAtWeekly = new DateTimeOffset(2026, 7, 19, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .ExpectRequest("account/rateLimits/read", RateLimitsResult(0, 0, initialResetsAtFiveHour, resetsAtWeekly))
            .EmitNotification(
                "account/rateLimits/updated",
                new
                {
                    rateLimits = new
                    {
                        secondary = new
                        {
                            usedPercent = 5,
                            resetsAt = externalResetsAtFiveHour,
                            windowDurationMins = 300L,
                        },
                    },
                })
            .ExpectRequest("account/rateLimits/read", RateLimitsResult(0, 5, externalResetsAtFiveHour, resetsAtWeekly))
            .HangAfterEof();

        var clock = new ManualClock();
        var delay = new ManualDelay(clock);
        AppServerSupervisor supervisor = CreateSupervisor(
            script,
            healthyIntervalSeconds: 60,
            backoffDelay: delay,
            healthyDelay: delay);

        var sessions = new List<AppServerGenerationSession>();
        supervisor.SessionPublished += (_, args) => sessions.Add(args.Generation);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task startTask = supervisor.StartAsync(cts.Token);

        try
        {
            await WaitForSessionsAsync(sessions, count: 1, timeout: TimeSpan.FromSeconds(10));

            using var quotaSource = new AppServerQuotaSource(supervisor);
            await using var monitor = new QuotaMonitor(
                quotaSource,
                clock,
                delay,
                pollInterval: TimeSpan.FromHours(1),
                staleThreshold: TimeSpan.FromSeconds(120),
                notificationDebounce: TimeSpan.Zero);

            var updatedTcs = new TaskCompletionSource<QuotaSnapshot>();
            monitor.SnapshotChanged += (_, s) =>
            {
                if (s.FiveHour.UsedPercent > 0)
                {
                    updatedTcs.TrySetResult(s);
                }
            };

            await monitor.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Keep advancing manual time until the notification-driven read publishes.
            // A notification that arrives between loop iterations parks the manual
            // delay, so a single fixed advance is not guaranteed to observe it.
            QuotaSnapshot updated = await WaitForWithTimeAdvancesAsync(
                updatedTcs.Task,
                delay,
                TimeSpan.FromSeconds(15));

            Assert.True(updated.IsFresh);
            Assert.True(updated.FiveHour.IsAvailable);
            Assert.Equal(5, updated.FiveHour.UsedPercent);
            Assert.Equal(95, updated.FiveHour.RemainingPercent);
            Assert.NotNull(updated.FiveHour.ResetsAt);

            ActivationEligibilityResult eligibility = ActivationEligibility.Evaluate(
                updated,
                automationEnabled: true,
                activeAttempt: null,
                clock.UtcNow);
            Assert.False(eligibility.IsEligible);
            Assert.Equal("usage-nonzero", eligibility.Reason);

            await monitor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await CleanupSupervisorAsync(supervisor, cts, startTask);
        }
    }

    private static object InitializeResult() => new
    {
        codexHome = "C:\\Codex",
        platformFamily = "windows",
        platformOs = "windows",
        userAgent = "fake-codex-e2e",
    };

    private static object RateLimitsResult(int usedPercent) => new
    {
        rateLimits = new
        {
            primary = new { usedPercent },
        },
    };

    private static object RateLimitsResult(
        int primaryUsedPercent,
        int secondaryUsedPercent,
        long? secondaryResetsAt,
        long? primaryResetsAt = null) => new
    {
        rateLimits = new
        {
            primary = new
            {
                usedPercent = primaryUsedPercent,
                resetsAt = primaryResetsAt,
                windowDurationMins = 10080L,
            },
            secondary = new
            {
                usedPercent = secondaryUsedPercent,
                resetsAt = secondaryResetsAt,
                windowDurationMins = 300L,
            },
        },
    };

    /// <summary>
    /// Awaits a task while repeatedly advancing the manual clock so monitor loop
    /// ticks parked on <see cref="ManualDelay"/> keep running. Fails with a real
    /// timeout if the task does not complete in time.
    /// </summary>
    private static async Task<T> WaitForWithTimeAdvancesAsync<T>(
        Task<T> task,
        ManualDelay delay,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(50)));
            if (completed == task)
            {
                return await task;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"The observed task did not complete within {timeout}.");
            }

            await delay.AdvanceAsync(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// Best-effort teardown that always reclaims the supervisor and its fake
    /// App Server child processes, even when a test fails or times out midway.
    /// </summary>
    private static async Task CleanupSupervisorAsync(
        AppServerSupervisor supervisor,
        CancellationTokenSource cts,
        Task startTask)
    {
        cts.Cancel();

        try
        {
            await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Best-effort cleanup; the original test failure takes precedence.
        }

        try
        {
            await startTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Best-effort cleanup; the original test failure takes precedence.
        }
    }

    private static string NotificationLine(int usedPercent) =>
        JsonSerializer.Serialize(new
        {
            method = "account/rateLimits/updated",
            @params = new
            {
                rateLimits = new
                {
                    primary = new { usedPercent },
                },
            },
        });

    private AppServerProcess CreateProcess(FakeAppServerScriptBuilder script)
    {
        string scriptPath = script.WriteToFile(_tempDir);
        return new AppServerProcess(
            new SystemProcessHost(),
            new ProcessStartRequest(FakeServerPath(), [scriptPath]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget E2E"),
            TimeSpan.FromSeconds(2));
    }

    private AppServerSupervisor CreateSupervisor(
        FakeAppServerScriptBuilder script,
        int healthyIntervalSeconds,
        IDelay? backoffDelay = null,
        IDelay? healthyDelay = null)
    {
        string scriptPath = script.WriteToFile(_tempDir);
        var settings = new AppServerSupervisorSettings(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(healthyIntervalSeconds));

        backoffDelay ??= new TaskDelay();
        healthyDelay ??= backoffDelay;

        return new AppServerSupervisor(
            new SystemProcessHost(),
            new ProcessStartRequest(FakeServerPath(), [scriptPath]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget E2E"),
            TimeSpan.FromSeconds(2),
            backoffDelay,
            settings,
            healthyDelay);
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

        // Fallback for builds that copy the dll but not the exe (Linux container builds).
        string dllPath = Path.Combine(testDir, "FakeCodexAppServer.dll");
        return dllPath;
    }

    private static async Task WaitForSessionsAsync(
        List<AppServerGenerationSession> sessions,
        int count,
        TimeSpan timeout,
        AppServerSupervisor? supervisorForDiagnostics = null)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (sessions.Count < count)
        {
            if (DateTime.UtcNow >= deadline)
            {
                string diag = supervisorForDiagnostics is null
                    ? string.Empty
                    : $" CurrentGeneration={supervisorForDiagnostics.CurrentGeneration?.GenerationId.ToString(CultureInfo.InvariantCulture) ?? "null"}.";
                throw new TimeoutException(
                    $"Expected {count} sessions but observed {sessions.Count}.{diag}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    private static async Task WaitForNotificationsAsync(
        List<RateLimitSnapshot> notifications,
        int count,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (notifications.Count < count)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Expected {count} notifications but observed {notifications.Count}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
