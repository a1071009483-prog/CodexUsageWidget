using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class AppServerProcessTests
{
    private static readonly string InitializeResultJson =
        "{\"codexHome\":\"C:\\\\Codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\",\"userAgent\":\"fake\"}";

    [Fact]
    public async Task StartReturnsOnlyAfterInitializeThenInitialized()
    {
        var fakeProcess = new FakeHostedProcess(exitOnEof: true);
        var fakeHost = new FakeProcessHost(fakeProcess);
        var captureLog = new CaptureLog();
        var startRequest = new ProcessStartRequest(
            "codex",
            ["--app-server"],
            "/working/dir",
            new Dictionary<string, string?> { ["ENV"] = "value" });
        var clientInfo = new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget");

        var appServerProcess = new AppServerProcess(
            fakeHost,
            startRequest,
            clientInfo,
            TimeSpan.FromSeconds(30),
            delay: FakeDelay.NeverElapsing,
            log: captureLog);

        Task<AppServerSession> startTask = appServerProcess.StartAsync(CancellationToken.None);

        // The exact ProcessStartRequest reached the host.
        Assert.Same(startRequest, fakeHost.LastRequest);

        // First outbound frame is a numeric-id initialize request.
        string initializeLine = await fakeProcess.Input.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement initializeRequest = Parse(initializeLine);
        Assert.Equal("initialize", initializeRequest.GetProperty("method").GetString());
        Assert.Equal(
            JsonValueKind.Number,
            initializeRequest.GetProperty("id").ValueKind);
        JsonElement clientInfoElement =
            initializeRequest.GetProperty("params").GetProperty("clientInfo");
        Assert.Equal("codex-usage-widget", clientInfoElement.GetProperty("name").GetString());
        Assert.Equal("1.0.0", clientInfoElement.GetProperty("version").GetString());
        Assert.Equal("Codex Usage Widget", clientInfoElement.GetProperty("title").GetString());

        // StartAsync has not completed: the initialize response has not arrived.
        Assert.False(startTask.IsCompleted);

        // Return the initialize result.
        long initializeId = initializeRequest.GetProperty("id").GetInt64();
        fakeProcess.Output.WriteLine(Success(initializeId, InitializeResultJson));

        // Next outbound frame is the parameterless initialized notification (no id).
        string initializedLine = await fakeProcess.Input.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement initializedNotification = Parse(initializedLine);
        Assert.Equal("initialized", initializedNotification.GetProperty("method").GetString());
        Assert.False(initializedNotification.TryGetProperty("id", out _));

        // Only after initialized does StartAsync return a gateway.
        AppServerSession session = await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(session.Gateway);
        Assert.False(session.Completion.IsCompleted);

        await appServerProcess.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(true)]   // closing stdin causes a clean zero exit; TerminateAsync never called
    [InlineData(false)] // child ignores EOF; grace delay elapses; TerminateAsync called once
    public async Task StopUsesEofForGracefulExitAndTerminatesOnlyAfterTheGraceBound(
        bool exitOnEof)
    {
        var fakeProcess = new FakeHostedProcess(exitOnEof: exitOnEof);
        var fakeHost = new FakeProcessHost(fakeProcess);
        var fakeDelay = new FakeDelay();
        var captureLog = new CaptureLog();

        // Write some stderr lines that must never be persisted verbatim.
        fakeProcess.Error.WriteLine("sensitive-stderr-data");
        fakeProcess.Error.WriteLine("another-secret-line-with-token");

        var appServerProcess = new AppServerProcess(
            fakeHost,
            new ProcessStartRequest("codex", []),
            new ClientInformation("test", "1.0"),
            TimeSpan.FromSeconds(30),
            delay: fakeDelay,
            log: captureLog);

        AppServerSession session = await StartHandshakeAsync(appServerProcess, fakeProcess);
        Assert.False(session.Completion.IsCompleted);

        Task stopTask = appServerProcess.StopAsync(CancellationToken.None);

        // Wait until the shutdown has closed stdin (EOF sent).
        await fakeProcess.Input.Closed.WaitAsync(TimeSpan.FromSeconds(5));

        if (exitOnEof)
        {
            // The child exits on EOF before the grace delay elapses.
            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, fakeProcess.TerminateCount);
        }
        else
        {
            // The child ignores EOF; the grace delay must elapse before terminate.
            // The delay has not elapsed yet, so stop must still be running.
            Assert.False(stopTask.IsCompleted);
            fakeDelay.Elapse();
            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, fakeProcess.TerminateCount);
        }

        // Idempotent: repeated StopAsync / DisposeAsync do not close/terminate/dispose twice.
        await appServerProcess.StopAsync(CancellationToken.None);
        Assert.Equal(fakeProcess.TerminateCount, fakeProcess.TerminateCount);
        await appServerProcess.DisposeAsync();
        Assert.Equal(exitOnEof ? 0 : 1, fakeProcess.TerminateCount);
        Assert.Equal(1, fakeProcess.DisposeCount);

        // Stderr drain logged only structured safe fields — never raw content.
        foreach (StructuredLogEvent logEvent in captureLog.Events)
        {
            Assert.Equal("AppServerStderrLine", logEvent.EventName);
            foreach (string? propertyValue in logEvent.Properties.Values)
            {
                Assert.False(
                    propertyValue?.Contains("sensitive", StringComparison.Ordinal) == true,
                    "Raw stderr content must not be persisted in log properties.");
                Assert.False(
                    propertyValue?.Contains("secret", StringComparison.Ordinal) == true,
                    "Raw stderr content must not be persisted in log properties.");
                Assert.False(
                    propertyValue?.Contains("token", StringComparison.Ordinal) == true,
                    "Raw stderr content must not be persisted in log properties.");
            }
        }
    }

    [Fact]
    public async Task UnexpectedExitFaultsSessionAndPendingRequestsAsDisconnected()
    {
        // --- Part 1: unexpected stdout EOF with a pending request ---
        var fakeProcess = new FakeHostedProcess();
        var fakeHost = new FakeProcessHost(fakeProcess);
        var captureLog = new CaptureLog();

        var appServerProcess = new AppServerProcess(
            fakeHost,
            new ProcessStartRequest("codex", []),
            new ClientInformation("test", "1.0"),
            TimeSpan.FromSeconds(30),
            delay: FakeDelay.NeverElapsing,
            log: captureLog);

        AppServerSession session = await StartHandshakeAsync(appServerProcess, fakeProcess);

        // Start a request that will remain pending when the connection faults.
        Task<AccountReadResponse> pendingRequest =
            session.Gateway.ReadAccountAsync(false, CancellationToken.None);

        // Confirm the request was written to the child's stdin.
        _ = await fakeProcess.Input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // The child exits unexpectedly — stdout EOF + non-zero exit.
        fakeProcess.SimulateExit(1);

        // Pending request faults as Disconnected.
        AppServerProtocolException pendingException =
            await Assert.ThrowsAsync<AppServerProtocolException>(
                () => pendingRequest.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(AppServerProtocolErrorKind.Disconnected, pendingException.Kind);

        // Session Completion faults as Disconnected.
        AppServerProtocolException completionException =
            await Assert.ThrowsAsync<AppServerProtocolException>(
                () => session.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(AppServerProtocolErrorKind.Disconnected, completionException.Kind);

        // Subsequent send fails immediately — same terminal state.
        await Assert.ThrowsAnyAsync<Exception>(
            () => session.Gateway.ReadAccountAsync(false, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        // Explicit stop after the fault does not reinterpret as a clean session.
        await appServerProcess.StopAsync(CancellationToken.None);
        Assert.Equal(1, fakeProcess.DisposeCount);

        // --- Part 2 (accompanying assertion): no pending request — Completion must still fault ---
        var fakeProcess2 = new FakeHostedProcess();
        var fakeHost2 = new FakeProcessHost(fakeProcess2);
        var appServerProcess2 = new AppServerProcess(
            fakeHost2,
            new ProcessStartRequest("codex", []),
            new ClientInformation("test", "1.0"),
            TimeSpan.FromSeconds(30),
            delay: FakeDelay.NeverElapsing,
            log: captureLog);

        AppServerSession session2 = await StartHandshakeAsync(appServerProcess2, fakeProcess2);

        // No request is sent. Stdout EOF alone must still fault Completion.
        fakeProcess2.SimulateExit(0);

        AppServerProtocolException completionException2 =
            await Assert.ThrowsAsync<AppServerProtocolException>(
                () => session2.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(AppServerProtocolErrorKind.Disconnected, completionException2.Kind);

        await appServerProcess2.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InitializationFailureCleansUpTheChild()
    {
        var fakeProcess = new FakeHostedProcess(exitOnEof: true);
        var fakeHost = new FakeProcessHost(fakeProcess);
        var fakeDelay = new FakeDelay();

        var appServerProcess = new AppServerProcess(
            fakeHost,
            new ProcessStartRequest("codex", []),
            new ClientInformation("test", "1.0"),
            TimeSpan.FromSeconds(30),
            delay: fakeDelay,
            log: null);

        Task<AppServerSession> startTask = appServerProcess.StartAsync(CancellationToken.None);

        // Read the initialize request.
        string initializeLine = await fakeProcess.Input.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement initializeRequest = Parse(initializeLine);
        Assert.Equal("initialize", initializeRequest.GetProperty("method").GetString());

        // The child exits before responding — stdout EOF + non-zero exit.
        fakeProcess.SimulateExit(1);

        // StartAsync must not return a gateway; it throws.
        await Assert.ThrowsAsync<AppServerProtocolException>(
            () => startTask.WaitAsync(TimeSpan.FromSeconds(5)));

        // The child is cleaned up — disposed exactly once, no orphan.
        Assert.Equal(1, fakeProcess.DisposeCount);

        // Idempotent stop does not re-close/re-terminate.
        await appServerProcess.StopAsync(CancellationToken.None);
        Assert.Equal(1, fakeProcess.DisposeCount);
        await appServerProcess.DisposeAsync();
        Assert.Equal(1, fakeProcess.DisposeCount);
    }

    private static async Task<AppServerSession> StartHandshakeAsync(
        AppServerProcess appServerProcess,
        FakeHostedProcess fakeProcess)
    {
        Task<AppServerSession> startTask = appServerProcess.StartAsync(CancellationToken.None);

        string initializeLine = await fakeProcess.Input.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement initializeRequest = Parse(initializeLine);
        long initializeId = initializeRequest.GetProperty("id").GetInt64();
        fakeProcess.Output.WriteLine(Success(initializeId, InitializeResultJson));

        string initializedLine = await fakeProcess.Input.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        JsonElement initializedNotification = Parse(initializedLine);
        Assert.Equal("initialized", initializedNotification.GetProperty("method").GetString());
        Assert.False(initializedNotification.TryGetProperty("id", out _));

        return await startTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Success(long id, string resultJson) => JsonSerializer.Serialize(new
    {
        id,
        result = Parse(resultJson),
    });

    // --- Test doubles ---

    private sealed class FakeProcessHost : IProcessHost
    {
        private readonly FakeHostedProcess _process;

        public FakeProcessHost(FakeHostedProcess process) => _process = process;

        public ProcessStartRequest? LastRequest { get; private set; }

        public Task<IHostedProcess> StartAsync(
            ProcessStartRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult<IHostedProcess>(_process);
        }
    }

    private sealed class FakeHostedProcess : IHostedProcess
    {
        private readonly FakeStandardInput _standardInput;
        private readonly ChannelLineReader _standardOutput = new();
        private readonly ChannelLineReader _standardError = new();
        private readonly TaskCompletionSource<ProcessExitResult> _exitCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _exitOnEof;
        private int _terminateCount;
        private int _disposeCount;

        public FakeHostedProcess(bool exitOnEof = false)
        {
            _exitOnEof = exitOnEof;
            _standardInput = new FakeStandardInput();
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

        public int TerminateCount => Volatile.Read(ref _terminateCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void SimulateExit(int exitCode)
        {
            CompleteStreams();
            _exitCompletion.TrySetResult(new ProcessExitResult(exitCode, false));
        }

        public void CompleteStdout() => _standardOutput.Complete();

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

            cancellationToken.ThrowIfCancellationRequested();
            return _exitCompletion.Task.Result;
        }

        public Task<ProcessExitResult> TerminateAsync(CancellationToken cancellationToken)
        {
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

    private sealed class FakeDelay : IDelay
    {
        private readonly TaskCompletionSource _tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public static FakeDelay NeverElapsing { get; } = new();

        public Task Elapsed => _tcs.Task;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            _tcs.Task.WaitAsync(cancellationToken);

        public void Elapse() => _tcs.TrySetResult();
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
            lock (_lock)
            {
                _events.Add(logEvent);
            }

            return ValueTask.CompletedTask;
        }
    }
}
