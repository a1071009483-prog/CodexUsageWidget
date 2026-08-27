using System.Reflection;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Monitoring;
using CodexUsageWidget.Core.Quota;
using CodexUsageWidget.Core.Tests.Testing;
using Xunit;

namespace CodexUsageWidget.Core.Tests.Monitoring;

public sealed class QuotaMonitorTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResetsAt = Start.AddHours(5);
    private const int YieldLimit = 1000;

    private readonly ManualClock _clock;
    private readonly ManualDelay _delay;
    private readonly FakeQuotaSource _source;

    public QuotaMonitorTests()
    {
        _clock = new ManualClock(Start);
        _delay = new ManualDelay(_clock);
        _source = new FakeQuotaSource();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _delay.Dispose();

    [Fact]
    public async Task StartupSynchronizesWithinSixtySeconds()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(10));
        QuotaMonitor monitor = new(_source, _clock, _delay);

        await monitor.StartAsync();

        try
        {
            Assert.NotNull(monitor.CurrentSnapshot);
            Assert.Equal("Pro", monitor.CurrentSnapshot.ScopeLabel);
            Assert.Equal(MonitoringConnectionState.Connected, monitor.CurrentSnapshot.ConnectionState);
            Assert.True(monitor.CurrentSnapshot.IsFresh);
            Assert.Equal(1, _source.ReadCount);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task NotificationPublishesWithinOneSecond()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(10));
        QuotaMonitor monitor = new(_source, _clock, _delay);
        List<QuotaSnapshot> changes = new();
        monitor.SnapshotChanged += (_, s) => changes.Add(s);

        await monitor.StartAsync();

        try
        {
            _source.EnqueueSuccess(FiveHourSnapshot(20));
            _source.RaiseUpdated();

            await WaitUntilAsync(() => changes.Count == 2);

            Assert.Equal(2, _source.ReadCount);
            Assert.Equal(2, changes.Count);
            Assert.Equal(20, monitor.CurrentSnapshot!.FiveHour.UsedPercent);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task PollConvergesWithinSixtySeconds()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(10));
        QuotaMonitor monitor = new(_source, _clock, _delay);

        await monitor.StartAsync();

        try
        {
            int readsAfterStartup = _source.ReadCount;

            await AdvanceToAsync(Start + TimeSpan.FromSeconds(60));

            Assert.Equal(readsAfterStartup + 1, _source.ReadCount);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task CountdownTicksEverySecond()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(10, ResetsAt.ToUnixTimeMilliseconds()));
        QuotaMonitor monitor = new(_source, _clock, _delay);
        List<QuotaSnapshot> changes = new();
        monitor.SnapshotChanged += (_, s) => changes.Add(s);

        await monitor.StartAsync();

        try
        {
            await AdvanceToAsync(Start + TimeSpan.FromSeconds(2));

            Assert.True(changes.Count >= 3, $"Expected at least 3 snapshots, got {changes.Count}");
            Assert.All(changes, s => Assert.Equal(Start, s.SyncedAt));
            Assert.NotNull(changes[0].Countdown);
            Assert.True(changes[^1].Countdown < changes[0].Countdown);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task StaleMarkedExactlyAtOneHundredTwentySeconds()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(10));
        QuotaMonitor monitor = new(
            _source,
            _clock,
            _delay,
            pollInterval: TimeSpan.FromMinutes(5));

        await monitor.StartAsync();

        try
        {
            await AdvanceToAsync(Start + TimeSpan.FromSeconds(119));
            Assert.True(monitor.CurrentSnapshot!.IsFresh);

            await AdvanceToAsync(Start + TimeSpan.FromSeconds(120));
            Assert.False(monitor.CurrentSnapshot.IsFresh);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task ReconnectBackoffAfterSourceFailure()
    {
        _source.EnqueueResult(new QuotaSourceResult(false, null, "boom"));
        _source.EnqueueSuccess(FiveHourSnapshot(10));
        QuotaMonitor monitor = new(_source, _clock, _delay);

        await monitor.StartAsync();

        try
        {
            Assert.Equal(MonitoringConnectionState.Error, monitor.CurrentSnapshot!.ConnectionState);
            Assert.False(monitor.CurrentSnapshot.IsFresh);

            int readsAfterStartup = _source.ReadCount;

            await AdvanceToAsync(Start + TimeSpan.FromSeconds(1));
            Assert.Equal(readsAfterStartup + 1, _source.ReadCount);
            Assert.Equal(MonitoringConnectionState.Connected, monitor.CurrentSnapshot.ConnectionState);

            _source.EnqueueResult(new QuotaSourceResult(false, null, "boom"));
            await AdvanceToAsync(Start + TimeSpan.FromSeconds(61));
            Assert.Equal(MonitoringConnectionState.Error, monitor.CurrentSnapshot.ConnectionState);

            readsAfterStartup = _source.ReadCount;
            await AdvanceToAsync(Start + TimeSpan.FromSeconds(63));
            Assert.Equal(readsAfterStartup + 1, _source.ReadCount);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task ConnectionFailureRemainsStaleDuringLocalCountdownTicks()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(10, ResetsAt.ToUnixTimeSeconds()));
        _source.EnqueueResult(new QuotaSourceResult(false, null, "transport unavailable"));
        QuotaMonitor monitor = new(_source, _clock, _delay);

        await monitor.StartAsync();

        try
        {
            await AdvanceToAsync(Start + TimeSpan.FromSeconds(60));
            Assert.Equal(MonitoringConnectionState.Error, monitor.CurrentSnapshot!.ConnectionState);
            Assert.False(monitor.CurrentSnapshot.IsFresh);

            MethodInfo tick = typeof(QuotaMonitor).GetMethod(
                "RepublishCountdown",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            tick.Invoke(monitor, null);

            Assert.Equal(MonitoringConnectionState.Error, monitor.CurrentSnapshot.ConnectionState);
            Assert.False(monitor.CurrentSnapshot.IsFresh);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task DoesNotUseModelBoundary()
    {
        FakeModelBoundary boundary = new();
        _source.EnqueueSuccess(FiveHourSnapshot(10));
        QuotaMonitor monitor = new(_source, _clock, _delay);

        await monitor.StartAsync();

        try
        {
            Assert.Equal(0, boundary.CallCount);

            ConstructorInfo[] constructors = typeof(QuotaMonitor).GetConstructors();
            Assert.All(constructors, c =>
                Assert.DoesNotContain(c.GetParameters(), p => p.ParameterType == typeof(IModelBoundary)));

            FieldInfo[] fields = typeof(QuotaMonitor).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.All(fields, f => Assert.NotEqual(typeof(IModelBoundary), f.FieldType));
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task AccountTransitionInvalidatesOldSnapshot()
    {
        _source.EnqueueSuccess(new RawRateLimitSnapshot(
            "limit-5h",
            "5 hour credits",
            "PlanA",
            new RawRateLimitWindow(10, null, 300L)));
        QuotaMonitor monitor = new(_source, _clock, _delay);

        await monitor.StartAsync();

        try
        {
            Assert.Equal("PlanA", monitor.CurrentSnapshot!.ScopeLabel);

            _clock.Advance(TimeSpan.FromSeconds(10));
            _source.EnqueueSuccess(new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "PlanB",
                new RawRateLimitWindow(20, null, 300L)));
            _source.RaiseUpdated();

            await WaitUntilAsync(() => monitor.CurrentSnapshot.ScopeLabel == "PlanB");

            Assert.Equal("PlanB", monitor.CurrentSnapshot.ScopeLabel);
            Assert.Equal(20, monitor.CurrentSnapshot.FiveHour.UsedPercent);
            Assert.True(monitor.CurrentSnapshot.IsFresh);
            Assert.Equal(_clock.UtcNow, monitor.CurrentSnapshot.SyncedAt);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task RefreshNowReadsImmediatelyAndPreservesPollCadence()
    {
        _source.EnqueueSuccess(FiveHourSnapshot(10));
        QuotaMonitor monitor = new(_source, _clock, _delay);

        await monitor.StartAsync();

        try
        {
            int readsAfterStartup = _source.ReadCount;

            _source.EnqueueSuccess(FiveHourSnapshot(20));
            await AdvanceToAsync(Start + TimeSpan.FromSeconds(20));

            Task refreshTask = monitor.RefreshNowAsync();
            await WaitUntilAsync(() => _source.ReadCount == readsAfterStartup + 1);
            await refreshTask;

            Assert.Equal(readsAfterStartup + 1, _source.ReadCount);
            Assert.Equal(20, monitor.CurrentSnapshot!.FiveHour.UsedPercent);
            Assert.Equal(_clock.UtcNow, monitor.CurrentSnapshot.SyncedAt);

            _source.EnqueueSuccess(FiveHourSnapshot(30));
            await AdvanceToAsync(Start + TimeSpan.FromSeconds(59));
            Assert.Equal(readsAfterStartup + 1, _source.ReadCount);

            await AdvanceToAsync(Start + TimeSpan.FromSeconds(60));
            await WaitUntilAsync(() => _source.ReadCount == readsAfterStartup + 2);

            Assert.Equal(30, monitor.CurrentSnapshot.FiveHour.UsedPercent);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    private async Task AdvanceToAsync(DateTimeOffset target)
    {
        while (_clock.UtcNow < target)
        {
            TimeSpan step = TimeSpan.FromSeconds(1);
            if (_clock.UtcNow + step > target)
            {
                step = target - _clock.UtcNow;
            }

            await _delay.AdvanceAsync(step);
            await WaitUntilAsync(() => _delay.NextDeadline > _clock.UtcNow || _delay.PendingCount == 0);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < YieldLimit && !condition(); i++)
        {
            await Task.Yield();
        }
    }

    private static RawRateLimitSnapshot FiveHourSnapshot(int usedPercent, long? resetsAt = null)
    {
        return new RawRateLimitSnapshot(
            "limit-5h",
            "5 hour credits",
            "Pro",
            new RawRateLimitWindow(usedPercent, resetsAt, 300L));
    }
}
