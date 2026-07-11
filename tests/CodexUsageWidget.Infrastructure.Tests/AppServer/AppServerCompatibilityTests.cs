using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

/// <summary>
/// Startup capability fail-closed tests for <see cref="AppServerSupervisor"/>: a
/// non-generating capability preflight that finds a missing required method must keep the
/// supervisor out of the transient restart loop, publish no session, and surface an
/// explicit incompatible outcome that is terminal and distinct from transient connection
/// faults. The preflight never probes a live server and never invokes generation methods.
/// </summary>
public sealed class AppServerCompatibilityTests
{
    private static readonly string InitializeResultJson =
        "{\"codexHome\":\"C:\\\\Codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\",\"userAgent\":\"fake\"}";

    [Theory]
    [InlineData("thread/start")]
    [InlineData("turn/start")]
    [InlineData("turn/interrupt")]
    [InlineData("thread/delete")]
    [InlineData("account/rateLimits/read")]
    [InlineData("account/read")]
    [InlineData("model/list")]
    [InlineData("initialize")]
    public async Task MissingRequiredMethodFailsClosedAndIsNotRetriedAsTransient(string missingMethod)
    {
        // The preflight never launches a process; it only evaluates the locally advertised
        // method inventory. A missing required method is a terminal incompatible outcome.
        var diagnostics = new AppServerCapabilityDiagnostics();
        IEnumerable<string> advertised = AppServerCapabilityDiagnostics.RequiredMethods
            .Where(method => method != missingMethod);
        AppServerCapabilityResult preflightResult = diagnostics.Evaluate(advertised);
        Assert.False(preflightResult.IsCompatible);
        Assert.Contains(missingMethod, preflightResult.MissingMethods);

        var host = new CountingProcessHost();
        var backoffDelay = new RecordingDelay();
        var preflightCalled = 0;
        var supervisor = new AppServerSupervisor(
            host,
            new ProcessStartRequest("codex", ["--app-server"]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget"),
            TimeSpan.FromSeconds(30),
            backoffDelay,
            AppServerSupervisorSettings.Default,
            healthyDelay: new NeverElapsingDelay(),
            graceDelay: new NeverElapsingDelay(),
            capabilityPreflight: _ =>
            {
                Interlocked.Increment(ref preflightCalled);
                return Task.FromResult(preflightResult);
            },
            log: null);

        var published = 0;
        var incompatible = new TaskCompletionSource<AppServerIncompatibleEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.SessionPublished += (_, _) => Interlocked.Increment(ref published);
        supervisor.IncompatibleDetected += (_, args) => incompatible.TrySetResult(args);

        // No process is launched: the host is never called, so cancellation is irrelevant.
        Task startTask = supervisor.StartAsync(CancellationToken.None);

        AppServerIncompatibleEventArgs incompatibleArgs = await incompatible.Task
            .WaitAsync(TimeSpan.FromSeconds(5));

        // StartAsync completes (no restart loop is entered).
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref preflightCalled));
        Assert.Equal(0, host.StartCallCount);
        Assert.Equal(0, Volatile.Read(ref published));
        Assert.Equal(AppServerCapabilityState.Incompatible, supervisor.Compatibility);
        Assert.NotNull(supervisor.CapabilityResult);
        Assert.False(supervisor.CapabilityResult!.IsCompatible);
        Assert.Contains(missingMethod, incompatibleArgs.MissingMethods);
        // No backoff delay was ever scheduled (the restart loop never ran).
        Assert.Empty(backoffDelay.Delays);
        Assert.Equal(0, backoffDelay.MaxActive);

        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CompatibleCapabilityAllowsTheRestartLoopToRun()
    {
        var diagnostics = new AppServerCapabilityDiagnostics();
        AppServerCapabilityResult preflightResult = diagnostics.Evaluate(
            AppServerCapabilityDiagnostics.RequiredMethods);
        Assert.True(preflightResult.IsCompatible);

        var gen1 = new FakeHostedProcess(exitOnEof: true);
        var host = new FakeSequenceProcessHost(new[] { gen1 });
        var backoffDelay = new RecordingDelay();

        var supervisor = new AppServerSupervisor(
            host,
            new ProcessStartRequest("codex", ["--app-server"]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget"),
            TimeSpan.FromSeconds(30),
            backoffDelay,
            AppServerSupervisorSettings.Default,
            healthyDelay: new NeverElapsingDelay(),
            graceDelay: new NeverElapsingDelay(),
            capabilityPreflight: _ => Task.FromResult(preflightResult),
            log: null);

        var publishes = Channel.CreateUnbounded<AppServerGenerationSession>();
        supervisor.SessionPublished += (_, args) => publishes.Writer.TryWrite(args.Generation);

        using var cts = new CancellationTokenSource();
        Task startTask = supervisor.StartAsync(cts.Token);

        // The restart loop runs: gen1 is launched and handshakes, a session is published.
        long initId = await ReadInitializeAsync(gen1);
        gen1.Output.WriteLine(Success(initId, InitializeResultJson));
        await ReadInitializedNotificationAsync(gen1);
        AppServerGenerationSession session = await publishes.Reader.ReadAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(AppServerCapabilityState.Compatible, supervisor.Compatibility);
        Assert.NotNull(supervisor.CapabilityResult);
        Assert.True(supervisor.CapabilityResult!.IsCompatible);
        Assert.Equal(1, host.StartCallCount);
        Assert.False(session.Session.Completion.IsCompleted);

        cts.Cancel();
        await startTask.WaitAsync(TimeSpan.FromSeconds(10));

        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelDuringPreflightStopsStartWithoutEnteringLoopOrRaisingIncompatible()
    {
        // The preflight never completes on its own; it observes cancellation. Cancelling the
        // start token must stop StartAsync without raising IncompatibleDetected, publishing a
        // session, or launching a process.
        var diagnostics = new AppServerCapabilityDiagnostics();
        var host = new CountingProcessHost();
        var preflightEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var preflightCalled = 0;

        var supervisor = new AppServerSupervisor(
            host,
            new ProcessStartRequest("codex", ["--app-server"]),
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget"),
            TimeSpan.FromSeconds(30),
            new RecordingDelay(),
            AppServerSupervisorSettings.Default,
            healthyDelay: new NeverElapsingDelay(),
            graceDelay: new NeverElapsingDelay(),
            capabilityPreflight: async cancellationToken =>
            {
                Interlocked.Increment(ref preflightCalled);
                preflightEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
                return diagnostics.Evaluate(AppServerCapabilityDiagnostics.RequiredMethods);
            },
            log: null);

        var incompatibleRaised = 0;
        supervisor.IncompatibleDetected += (_, _) => Interlocked.Increment(ref incompatibleRaised);

        using var cts = new CancellationTokenSource();
        Task startTask = supervisor.StartAsync(cts.Token);

        await preflightEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref preflightCalled));

        cts.Cancel();

        // StartAsync completes (the cancellation was observed inside the preflight; no loop,
        // no incompatible outcome, no exception propagated).
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref preflightCalled));
        Assert.Equal(0, host.StartCallCount);
        Assert.Equal(0, Volatile.Read(ref incompatibleRaised));
        Assert.Equal(AppServerCapabilityState.Unknown, supervisor.Compatibility);

        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task<long> ReadInitializeAsync(FakeHostedProcess process)
    {
        string line = await process.Input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement request = JsonDocument.Parse(line).RootElement.Clone();
        Assert.Equal("initialize", request.GetProperty("method").GetString());
        return request.GetProperty("id").GetInt64();
    }

    private static async Task ReadInitializedNotificationAsync(FakeHostedProcess process)
    {
        string line = await process.Input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement notification = JsonDocument.Parse(line).RootElement.Clone();
        Assert.Equal("initialized", notification.GetProperty("method").GetString());
        Assert.False(notification.TryGetProperty("id", out _));
    }

    private static string Success(long id, string resultJson) => JsonSerializer.Serialize(new
    {
        id,
        result = JsonDocument.Parse(resultJson).RootElement.Clone(),
    });

    // --- Minimal test doubles (kept local to avoid touching existing test files) ---

    private sealed class CountingProcessHost : IProcessHost
    {
        private int _startCallCount;
        public int StartCallCount => Volatile.Read(ref _startCallCount);

        public Task<IHostedProcess> StartAsync(ProcessStartRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            Interlocked.Increment(ref _startCallCount);
            throw new InvalidOperationException(
                "The supervisor must not launch a process when the capability preflight is incompatible.");
        }
    }

    private sealed class FakeSequenceProcessHost : IProcessHost
    {
        private readonly Queue<FakeHostedProcess> _processes;

        public FakeSequenceProcessHost(IEnumerable<FakeHostedProcess> processes) =>
            _processes = new Queue<FakeHostedProcess>(processes);

        public int StartCallCount { get; private set; }

        public Task<IHostedProcess> StartAsync(ProcessStartRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            StartCallCount++;
            return Task.FromResult<IHostedProcess>(_processes.Dequeue());
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
            _exitCompletion.TrySetResult(new ProcessExitResult(1, true));
            CompleteStreams();
            return _exitCompletion.Task;
        }

        public ValueTask DisposeAsync()
        {
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

        public int MaxActive { get; private set; }

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _delays.Add(delay);
            }

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class NeverElapsingDelay : IDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
