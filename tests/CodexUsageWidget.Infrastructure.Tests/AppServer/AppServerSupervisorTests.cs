using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

/// <summary>
/// Reconnect-ownership tests for <see cref="AppServerSupervisor"/>: successive process
/// generations with capped backoff, retired-generation filtering, and a healthy-reset
/// rule that is not satisfied by handshake alone. All scenarios use in-memory hosted
/// processes and fake delays — no real process launches and no model consumption.
/// </summary>
public sealed class AppServerSupervisorTests
{
    private static readonly string InitializeResultJson =
        "{\"codexHome\":\"C:\\\\Codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\",\"userAgent\":\"fake\"}";

    [Fact]
    public async Task UnexpectedFailuresCreateNewGenerationsWithCappedBackoff()
    {
        // Generations: gen1 fails before handshake, gen2 fails after handshake, gen3-7 fail
        // before handshake, gen8 stays healthy. Seven failures must produce the capped
        // backoff sequence 1,2,4,8,16,30,30 seconds before gen8 is published.
        var gens = new List<FakeHostedProcess>();
        for (int i = 0; i < 7; i++)
        {
            gens.Add(new FakeHostedProcess(exitOnEof: true));
        }

        // gen8 stays healthy; gen2 (index 1) fails after handshake.
        gens.Add(new FakeHostedProcess(exitOnEof: true));

        var host = new FakeSequenceProcessHost(gens);
        var backoffDelay = new RecordingDelay();
        var healthyDelay = new NeverElapsingDelay();
        var graceDelay = new NeverElapsingDelay();
        var captureLog = new CaptureLog();

        var settings = new AppServerSupervisorSettings(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60));

        var supervisor = new AppServerSupervisor(
            host,
            new ProcessStartRequest("codex", ["--app-server"]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget"),
            TimeSpan.FromSeconds(30),
            backoffDelay,
            settings,
            healthyDelay,
            graceDelay,
            captureLog);

        var publishes = Channel.CreateUnbounded<AppServerGenerationSession>();
        supervisor.SessionPublished += (_, args) => publishes.Writer.TryWrite(args.Generation);

        using var cts = new CancellationTokenSource();
        Task startTask = supervisor.StartAsync(cts.Token);

        // gen1: fail before handshake (process exits after the initialize request is sent).
        await ReadInitializeAsync(gens[0]);
        gens[0].SimulateExit(1);

        // gen2: complete the handshake so it publishes, then fail after handshake.
        long gen2InitId = await ReadInitializeAsync(gens[1]);
        gens[1].Output.WriteLine(Success(gen2InitId, InitializeResultJson));
        await ReadInitializedNotificationAsync(gens[1]);
        AppServerGenerationSession gen2Session = await ReadPublishAsync(publishes);
        Assert.False(gen2Session.Session.Completion.IsCompleted);
        gens[1].SimulateExit(1);

        // gen3-7: each fails before handshake.
        for (int i = 2; i <= 6; i++)
        {
            await ReadInitializeAsync(gens[i]);
            gens[i].SimulateExit(1);
        }

        // gen8: handshake + stable.
        long gen8InitId = await ReadInitializeAsync(gens[7]);
        gens[7].Output.WriteLine(Success(gen8InitId, InitializeResultJson));
        await ReadInitializedNotificationAsync(gens[7]);
        AppServerGenerationSession gen8Session = await ReadPublishAsync(publishes);
        Assert.Equal(8, gen8Session.GenerationId);
        Assert.False(gen8Session.Session.Completion.IsCompleted);

        // Cancellation is honored: cancelling the start token stops gen8 and completes StartAsync.
        cts.Cancel();
        await startTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(8, host.StartCallCount);
        Assert.Equal(1, host.MaxLive);
        Assert.Equal(1, backoffDelay.MaxActive);
        Assert.Equal(
            new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(16),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
            },
            backoffDelay.Delays);
        Assert.Equal(1, gens[7].DisposeCount);

        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RetiredGenerationsCannotPublishOrCompleteCurrentWork()
    {
        // gen1 publishes, then a malformed frame faults its connection. Late frames queued
        // on gen1's stdout (a response with an id reused by gen2, and a rate-limits
        // notification) must not complete gen2's pending request or reach the forwarded
        // notification event. Explicit StopAsync must suppress any further restart.
        var gen1 = new FakeHostedProcess(exitOnEof: true);
        var gen2 = new FakeHostedProcess(exitOnEof: true);
        var host = new FakeSequenceProcessHost(new[] { gen1, gen2 });
        var backoffDelay = new RecordingDelay();
        var healthyDelay = new NeverElapsingDelay();
        var graceDelay = new NeverElapsingDelay();
        var captureLog = new CaptureLog();

        var supervisor = new AppServerSupervisor(
            host,
            new ProcessStartRequest("codex", ["--app-server"]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget"),
            TimeSpan.FromSeconds(30),
            backoffDelay,
            AppServerSupervisorSettings.Default,
            healthyDelay,
            graceDelay,
            captureLog);

        var publishes = Channel.CreateUnbounded<AppServerGenerationSession>();
        supervisor.SessionPublished += (_, args) => publishes.Writer.TryWrite(args.Generation);

        var forwarded = new List<RateLimitSnapshot>();
        supervisor.RateLimitsUpdated += (_, args) =>
        {
            lock (forwarded) { forwarded.Add(args.RateLimits); }
        };

        using var cts = new CancellationTokenSource();
        Task startTask = supervisor.StartAsync(cts.Token);

        // gen1 handshake + publish.
        long gen1InitId = await ReadInitializeAsync(gen1);
        gen1.Output.WriteLine(Success(gen1InitId, InitializeResultJson));
        await ReadInitializedNotificationAsync(gen1);
        AppServerGenerationSession gen1Session = await ReadPublishAsync(publishes);
        Assert.Equal(1, gen1Session.GenerationId);

        // While gen1 is current, a rate-limits notification IS forwarded.
        gen1.Output.WriteLine(RateLimitsUpdateNotification(7));

        // gen1 has a pending rate-limits read (id 2: initialize used id 1).
        Task<RateLimitsReadResponse> gen1Pending =
            gen1Session.Session.Gateway.ReadRateLimitsAsync(CancellationToken.None);
        long gen1ReadId = await ReadRateLimitsReadRequestAsync(gen1, expectId: 2);

        // Retire gen1 via a structurally malformed frame. The connection pump reads the
        // malformed line, faults Completion, and exits — but stdout stays open so late
        // frames can still be queued. Write the late frames back-to-back with the
        // malformed line (no await between them) so the supervisor's async disposal of
        // gen1 cannot close stdout first. The pump has exited, so the late frames are
        // never processed; they are dropped when gen1 is disposed.
        gen1.Output.WriteLine(MalformedResponse(gen1ReadId));
        gen1.Output.WriteLine(RateLimitsResponse(gen1ReadId, 99));
        gen1.Output.WriteLine(RateLimitsUpdateNotification(99));

        // The old pending request fails (as a connection fault), not via a late completion.
        AppServerProtocolException gen1PendingException = await Assert.ThrowsAsync<
            AppServerProtocolException>(() => gen1Pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(AppServerProtocolErrorKind.MalformedMessage, gen1PendingException.Kind);

        // gen2 handshake + publish.
        long gen2InitId = await ReadInitializeAsync(gen2);
        gen2.Output.WriteLine(Success(gen2InitId, InitializeResultJson));
        await ReadInitializedNotificationAsync(gen2);
        AppServerGenerationSession gen2Session = await ReadPublishAsync(publishes);
        Assert.Equal(2, gen2Session.GenerationId);
        Assert.NotSame(gen1Session.Session.Gateway, gen2Session.Session.Gateway);

        // gen2 has a pending rate-limits read with the SAME numeric id (2) that gen1 used.
        Task<RateLimitsReadResponse> gen2Pending =
            gen2Session.Session.Gateway.ReadRateLimitsAsync(CancellationToken.None);
        long gen2ReadId = await ReadRateLimitsReadRequestAsync(gen2, expectId: 2);
        Assert.Equal(gen1ReadId, gen2ReadId);

        // Complete gen2's pending request from gen2's own stdout — never via gen1's late frame.
        gen2.Output.WriteLine(RateLimitsResponse(gen2ReadId, 42));
        RateLimitsReadResponse gen2Result = await gen2Pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, gen2Result.RateLimits.Primary!.UsedPercent);

        // A gen2 rate-limits notification IS forwarded (forwarding follows the current gen).
        gen2.Output.WriteLine(RateLimitsUpdateNotification(42));
        await WaitForForwardedAsync(forwarded, count: 2);

        RateLimitSnapshot[] snapshots;
        lock (forwarded) { snapshots = forwarded.ToArray(); }
        Assert.Equal(7, snapshots[0].Primary!.UsedPercent);
        Assert.Equal(42, snapshots[1].Primary!.UsedPercent);
        Assert.DoesNotContain(snapshots, s => s.Primary?.UsedPercent == 99);

        // Explicit StopAsync suppresses further restart (no gen3).
        await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await startTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, host.StartCallCount);
        Assert.Equal(1, host.MaxLive);
        Assert.Equal(1, backoffDelay.MaxActive);

        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BackoffResetsOnlyAfterAStableHealthyInterval()
    {
        // gen1 fails before handshake (backoff 1s). gen2 handshakes but faults before the
        // healthy interval elapses (backoff grows to 2s — NOT reset on handshake alone).
        // gen3 handshakes, survives the healthy interval (reset), then faults (backoff 1s
        // again). gen4 stays healthy and is stopped.
        var gen1 = new FakeHostedProcess(exitOnEof: true);
        var gen2 = new FakeHostedProcess(exitOnEof: true);
        var gen3 = new FakeHostedProcess(exitOnEof: true);
        var gen4 = new FakeHostedProcess(exitOnEof: true);
        var host = new FakeSequenceProcessHost(new[] { gen1, gen2, gen3, gen4 });

        var backoffDelay = new RecordingDelay();
        var healthyDelay = new ControllableDelay();
        var graceDelay = new NeverElapsingDelay();

        var settings = new AppServerSupervisorSettings(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(7));

        var supervisor = new AppServerSupervisor(
            host,
            new ProcessStartRequest("codex", ["--app-server"]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget"),
            TimeSpan.FromSeconds(30),
            backoffDelay,
            settings,
            healthyDelay,
            graceDelay,
            log: null);

        var publishes = Channel.CreateUnbounded<AppServerGenerationSession>();
        supervisor.SessionPublished += (_, args) => publishes.Writer.TryWrite(args.Generation);

        var confirmedHealthy = Channel.CreateUnbounded<AppServerGenerationSession>();
        supervisor.GenerationConfirmedHealthy += (_, args) => confirmedHealthy.Writer.TryWrite(args.Generation);

        using var cts = new CancellationTokenSource();
        Task startTask = supervisor.StartAsync(cts.Token);

        // gen1: fail before handshake -> backoff 1s (step 1).
        await ReadInitializeAsync(gen1);
        gen1.SimulateExit(1);

        // gen2: handshake + publish, but fault BEFORE the healthy interval elapses.
        long gen2InitId = await ReadInitializeAsync(gen2);
        gen2.Output.WriteLine(Success(gen2InitId, InitializeResultJson));
        await ReadInitializedNotificationAsync(gen2);
        AppServerGenerationSession gen2Session = await ReadPublishAsync(publishes);
        Assert.Equal(2, gen2Session.GenerationId);
        gen2.SimulateExit(0); // faults immediately, before healthy interval -> backoff grows.

        // gen3: handshake + publish, survive the healthy interval, then fault.
        long gen3InitId = await ReadInitializeAsync(gen3);
        gen3.Output.WriteLine(Success(gen3InitId, InitializeResultJson));
        await ReadInitializedNotificationAsync(gen3);
        AppServerGenerationSession gen3Session = await ReadPublishAsync(publishes);
        Assert.Equal(3, gen3Session.GenerationId);

        // Elapse gen3's healthy interval -> the supervisor confirms it healthy and resets backoff.
        healthyDelay.ElapseNext();
        AppServerGenerationSession gen3Confirmed = await ReadConfirmedAsync(confirmedHealthy);
        Assert.Equal(3, gen3Confirmed.GenerationId);
        gen3.SimulateExit(0); // faults AFTER becoming healthy -> backoff resets to 1s.

        // gen4: handshake + publish + survive healthy, then stop.
        long gen4InitId = await ReadInitializeAsync(gen4);
        gen4.Output.WriteLine(Success(gen4InitId, InitializeResultJson));
        await ReadInitializedNotificationAsync(gen4);
        AppServerGenerationSession gen4Session = await ReadPublishAsync(publishes);
        Assert.Equal(4, gen4Session.GenerationId);

        await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await startTask.WaitAsync(TimeSpan.FromSeconds(10));

        // gen1 -> 1s (not healthy). gen2 -> 2s (handshake but not healthy, NOT reset).
        // gen3 -> 1s (became healthy, then faulted -> reset).
        Assert.Equal(
            new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(1),
            },
            backoffDelay.Delays);
        Assert.Equal(4, host.StartCallCount);
        Assert.Equal(1, host.MaxLive);
        Assert.Equal(1, backoffDelay.MaxActive);

        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task<long> ReadInitializeAsync(FakeHostedProcess process)
    {
        string line = await process.Input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement request = Parse(line);
        Assert.Equal("initialize", request.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Number, request.GetProperty("id").ValueKind);
        return request.GetProperty("id").GetInt64();
    }

    private static async Task ReadInitializedNotificationAsync(FakeHostedProcess process)
    {
        string line = await process.Input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement notification = Parse(line);
        Assert.Equal("initialized", notification.GetProperty("method").GetString());
        Assert.False(notification.TryGetProperty("id", out _));
    }

    private static async Task<long> ReadRateLimitsReadRequestAsync(
        FakeHostedProcess process,
        long expectId)
    {
        string line = await process.Input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement request = Parse(line);
        Assert.Equal("account/rateLimits/read", request.GetProperty("method").GetString());
        long id = request.GetProperty("id").GetInt64();
        Assert.Equal(expectId, id);
        return id;
    }

    private static async Task<AppServerGenerationSession> ReadPublishAsync(
        Channel<AppServerGenerationSession> publishes) =>
        await publishes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    private static async Task<AppServerGenerationSession> ReadConfirmedAsync(
        Channel<AppServerGenerationSession> confirmed) =>
        await confirmed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    private static async Task WaitForForwardedAsync(List<RateLimitSnapshot> forwarded, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            lock (forwarded)
            {
                if (forwarded.Count >= count)
                {
                    return;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Expected {count} forwarded notifications, got {forwarded.Count}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Success(long id, string resultJson) => JsonSerializer.Serialize(new
    {
        id,
        result = Parse(resultJson),
    });

    private static string RateLimitsResponse(long id, int usedPercent) => JsonSerializer.Serialize(new
    {
        id,
        result = new
        {
            rateLimits = new { primary = new { usedPercent } },
        },
    });

    private static string RateLimitsUpdateNotification(int usedPercent) => JsonSerializer.Serialize(new
    {
        method = "account/rateLimits/updated",
        @params = new { rateLimits = new { primary = new { usedPercent } } },
    });

    private static string MalformedResponse(long id) =>
        $"{{\"id\":{id},\"result\":{{}},\"error\":{{\"code\":-1,\"message\":\"m\"}}}}";

    // --- Test doubles ---

    private sealed class FakeSequenceProcessHost : IProcessHost
    {
        private readonly Queue<FakeHostedProcess> _processes;
        private int _live;
        private int _maxLive;
        private int _startCallCount;

        public FakeSequenceProcessHost(IEnumerable<FakeHostedProcess> processes) =>
            _processes = new Queue<FakeHostedProcess>(processes);

        public int StartCallCount => Volatile.Read(ref _startCallCount);
        public int MaxLive => Volatile.Read(ref _maxLive);

        public Task<IHostedProcess> StartAsync(
            ProcessStartRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            Interlocked.Increment(ref _startCallCount);
            FakeHostedProcess process = _processes.Dequeue();
            Interlocked.Increment(ref _live);
            UpdateMax(ref _maxLive, Volatile.Read(ref _live));
            process.Exited.ContinueWith(
                _ => Interlocked.Decrement(ref _live),
                TaskScheduler.Default);
            return Task.FromResult<IHostedProcess>(process);
        }

        private static void UpdateMax(ref int target, int value)
        {
            int max = Volatile.Read(ref target);
            while (value > max)
            {
                int previous = Interlocked.CompareExchange(ref target, value, max);
                if (previous == max)
                {
                    return;
                }

                max = previous;
            }
        }
    }

    private sealed class FakeHostedProcess : IHostedProcess
    {
        private readonly FakeStandardInput _standardInput = new();
        private readonly ChannelLineReader _standardOutput = new();
        private readonly ChannelLineReader _standardError = new();
        private readonly TaskCompletionSource<ProcessExitResult> _exitCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _exitOnEof;
        private int _terminateCount;
        private int _disposeCount;

        public FakeHostedProcess(bool exitOnEof = true)
        {
            _exitOnEof = exitOnEof;
            _standardInput.Closed.ContinueWith(
                _ =>
                {
                    if (_exitOnEof)
                    {
                        CompleteStreams();
                        _exitCompletion.TrySetResult(new ProcessExitResult(0, false));
                    }
                },
                TaskScheduler.Default);
        }

        public TextWriter StandardInput => _standardInput;
        public TextReader StandardOutput => _standardOutput;
        public TextReader StandardError => _standardError;

        public FakeStandardInput Input => _standardInput;
        public ChannelLineReader Output => _standardOutput;
        public ChannelLineReader Error => _standardError;

        public Task Exited => _exitCompletion.Task;
        public int TerminateCount => Volatile.Read(ref _terminateCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void SimulateExit(int exitCode)
        {
            CompleteStreams();
            _exitCompletion.TrySetResult(new ProcessExitResult(exitCode, false));
        }

        private void CompleteStreams()
        {
            _standardOutput.Complete();
            _standardError.Complete();
        }

        public Task<ProcessExitResult> WaitForExitAsync(CancellationToken cancellationToken)
        {
            if (_exitCompletion.Task.IsCompleted)
            {
                return _exitCompletion.Task;
            }

            return WaitForExitCoreAsync(cancellationToken);
        }

        private async Task<ProcessExitResult> WaitForExitCoreAsync(CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
            Task completed = await Task.WhenAny(_exitCompletion.Task, cancellationTask);
            cts.Cancel();
            try { await cancellationTask.ConfigureAwait(false); } catch { }
            if (completed == cancellationTask)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return _exitCompletion.Task.Result;
        }

        public Task<ProcessExitResult> TerminateAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (_exitCompletion.TrySetResult(new ProcessExitResult(1, true)))
            {
                Interlocked.Increment(ref _terminateCount);
            }

            CompleteStreams();
            return _exitCompletion.Task;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeCount, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            CompleteStreams();
            _exitCompletion.TrySetResult(new ProcessExitResult(0, false));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeStandardInput : TextWriter
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource _closed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override Encoding Encoding => Encoding.UTF8;

        public Task Closed => _closed.Task;

        public override Task WriteLineAsync(string? value)
        {
            if (!_lines.Writer.TryWrite(value ?? string.Empty))
            {
                throw new InvalidOperationException("The input channel is closed.");
            }

            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return WriteLineAsync(buffer.ToString());
        }

        public override Task FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task<string> ReadLineAsync(CancellationToken cancellationToken = default) =>
            await _lines.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        protected override void Dispose(bool disposing)
        {
            _lines.Writer.TryComplete();
            _closed.TrySetResult();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            _lines.Writer.TryComplete();
            _closed.TrySetResult();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class RecordingDelay : IDelay
    {
        private readonly List<TimeSpan> _delays = new();
        private readonly object _lock = new();
        private int _active;
        private int _maxActive;

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_lock)
                {
                    return _delays.ToArray();
                }
            }
        }

        public int MaxActive => Volatile.Read(ref _maxActive);

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _delays.Add(delay);
            }

            int active = Interlocked.Increment(ref _active);
            UpdateMax(ref _maxActive, active);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private static void UpdateMax(ref int target, int value)
        {
            int max = Volatile.Read(ref target);
            while (value > max)
            {
                int previous = Interlocked.CompareExchange(ref target, value, max);
                if (previous == max)
                {
                    return;
                }

                max = previous;
            }
        }
    }

    private sealed class NeverElapsingDelay : IDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    /// <summary>
    /// A delay whose waits complete only when <see cref="ElapseNext"/> is called, in FIFO
    /// order. Cancellation removes a wait from the completion queue. Used for the healthy
    /// interval so the test can deterministically control when a generation is confirmed.
    /// </summary>
    private sealed class ControllableDelay : IDelay
    {
        private readonly object _lock = new();
        private readonly Queue<TaskCompletionSource> _pending = new();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            _ = delay;
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _pending.Enqueue(tcs);
            }

            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return tcs.Task;
        }

        public void ElapseNext()
        {
            TaskCompletionSource? target = null;
            lock (_lock)
            {
                while (_pending.Count > 0)
                {
                    TaskCompletionSource next = _pending.Dequeue();
                    if (!next.Task.IsCompleted)
                    {
                        target = next;
                        break;
                    }
                }
            }

            target?.TrySetResult();
        }
    }

    private sealed class CaptureLog : IRedactingLog
    {
        private readonly List<StructuredLogEvent> _events = new();
        private readonly object _lock = new();

        public IReadOnlyList<StructuredLogEvent> Events
        {
            get
            {
                lock (_lock)
                {
                    return _events.ToArray();
                }
            }
        }

        public ValueTask WriteAsync(
            StructuredLogEvent logEvent,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            lock (_lock)
            {
                _events.Add(logEvent);
            }

            return ValueTask.CompletedTask;
        }
    }
}
