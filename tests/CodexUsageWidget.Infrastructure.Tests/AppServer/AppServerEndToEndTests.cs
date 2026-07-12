using System.Globalization;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
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
        // supervisor must start a second process to satisfy a later read.
        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .ExpectRequest("account/rateLimits/read", RateLimitsResult(usedPercent: 10))
            .Exit(1)
            .Handshake(InitializeResult())
            .ExpectRequest("account/rateLimits/read", RateLimitsResult(usedPercent: 11))
            .HangAfterEof();

        AppServerSupervisor supervisor = CreateSupervisor(script, healthyIntervalSeconds: 60);

        var sessions = new List<AppServerGenerationSession>();
        supervisor.SessionPublished += (_, args) => sessions.Add(args.Generation);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task startTask = supervisor.StartAsync(cts.Token);

        await WaitForSessionsAsync(sessions, count: 2, timeout: TimeSpan.FromSeconds(20));

        RateLimitsReadResponse first = await sessions[0].Session.Gateway
            .ReadRateLimitsAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(10, first.RateLimits.Primary!.UsedPercent);

        RateLimitsReadResponse second = await sessions[1].Session.Gateway
            .ReadRateLimitsAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(11, second.RateLimits.Primary!.UsedPercent);

        await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await startTask.WaitAsync(TimeSpan.FromSeconds(10));
        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
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

        await WaitForNotificationsAsync(notifications, count: 1, timeout: TimeSpan.FromSeconds(5));

        Assert.Single(notifications);
        Assert.Equal(42, notifications[0].Primary!.UsedPercent);

        await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await startTask.WaitAsync(TimeSpan.FromSeconds(10));
        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SupervisorSkipsRetiredGenerationFrames()
    {
        // gen1: handshake, emit a notification, then stay alive long enough for us
        // to read it, then exit. gen2: handshake, emit a different notification.
        var script = new FakeAppServerScriptBuilder()
            .Handshake(InitializeResult())
            .EmitNotification(
                "account/rateLimits/updated",
                new { rateLimits = new { primary = new { usedPercent = 7 } } })
            .Exit(0)
            .Handshake(InitializeResult())
            .EmitNotification(
                "account/rateLimits/updated",
                new { rateLimits = new { primary = new { usedPercent = 8 } } })
            .HangAfterEof();

        AppServerSupervisor supervisor = CreateSupervisor(script, healthyIntervalSeconds: 60);

        var notifications = new List<RateLimitSnapshot>();
        supervisor.RateLimitsUpdated += (_, args) => notifications.Add(args.RateLimits);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task startTask = supervisor.StartAsync(cts.Token);

        await WaitForNotificationsAsync(notifications, count: 2, timeout: TimeSpan.FromSeconds(20));

        Assert.Equal(7, notifications[0].Primary!.UsedPercent);
        Assert.Equal(8, notifications[1].Primary!.UsedPercent);

        await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await startTask.WaitAsync(TimeSpan.FromSeconds(10));
        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
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
        int healthyIntervalSeconds)
    {
        string scriptPath = script.WriteToFile(_tempDir);
        var settings = new AppServerSupervisorSettings(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(healthyIntervalSeconds));

        return new AppServerSupervisor(
            new SystemProcessHost(),
            new ProcessStartRequest(FakeServerPath(), [scriptPath]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget E2E"),
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

        // Fallback for builds that copy the dll but not the exe (Linux container builds).
        string dllPath = Path.Combine(testDir, "FakeCodexAppServer.dll");
        return dllPath;
    }

    private static async Task WaitForSessionsAsync(
        List<AppServerGenerationSession> sessions,
        int count,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (sessions.Count < count)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Expected {count} sessions but observed {sessions.Count}.");
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
