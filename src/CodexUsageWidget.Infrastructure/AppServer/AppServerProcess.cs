using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

public sealed class AppServerProcess : IAsyncDisposable
{
    private readonly IProcessHost _processHost;
    private readonly ProcessStartRequest _startRequest;
    private readonly ClientInformation _clientInformation;
    private readonly TimeSpan _gracefulStopDelay;
    private readonly IDelay _delay;
    private readonly IRedactingLog? _log;

    private IHostedProcess? _hostedProcess;
    private JsonRpcConnection? _connection;
    private CodexAppServerGateway? _gateway;
    private Task? _stderrDrainTask;

    private int _started;
    private int _stopped;

    public AppServerProcess(
        IProcessHost processHost,
        ProcessStartRequest startRequest,
        ClientInformation clientInformation,
        TimeSpan gracefulStopDelay,
        IDelay? delay = null,
        IRedactingLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(processHost);
        ArgumentNullException.ThrowIfNull(startRequest);
        ArgumentNullException.ThrowIfNull(clientInformation);
        _processHost = processHost;
        _startRequest = startRequest;
        _clientInformation = clientInformation;
        _gracefulStopDelay = gracefulStopDelay;
        _delay = delay ?? SystemDelay.Instance;
        _log = log;
    }

    public async Task<AppServerSession> StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException(
                "The App Server process has already been started.");
        }

        try
        {
            _hostedProcess = await _processHost.StartAsync(_startRequest, cancellationToken)
                .ConfigureAwait(false);

            _stderrDrainTask = DrainStderrAsync(_hostedProcess.StandardError);

            _connection = new JsonRpcConnection(
                _hostedProcess.StandardOutput,
                _hostedProcess.StandardInput,
                _log);

            await _connection.StartAsync(cancellationToken).ConfigureAwait(false);

            _gateway = new CodexAppServerGateway(_connection);

            await _gateway.InitializeAsync(_clientInformation, cancellationToken)
                .ConfigureAwait(false);

            return new AppServerSession(_gateway, _connection.Completion);
        }
        catch
        {
            Interlocked.Exchange(ref _stopped, 1);
            await ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return Task.CompletedTask;
        }

        return ShutdownAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        await ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        IHostedProcess? hostedProcess = _hostedProcess;
        if (hostedProcess is null)
        {
            // StartAsync was never called or failed before the child was launched.
            return;
        }

        // 1. Close standard input to send EOF — gives the child a chance to exit cleanly.
        try
        {
            hostedProcess.StandardInput.Close();
        }
        catch
        {
            // Best-effort close; the child may have already closed its end.
        }

        // 2. Race natural exit against the configured grace delay.
        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<ProcessExitResult> exitTask = hostedProcess.WaitForExitAsync(raceCts.Token);
        Task delayTask = _delay.DelayAsync(_gracefulStopDelay, raceCts.Token);

        await Task.WhenAny(exitTask, delayTask).ConfigureAwait(false);

        if (!exitTask.IsCompletedSuccessfully)
        {
            // The process did not exit naturally (grace elapsed or caller cancelled).
            // Terminate with a non-cancellable token so the child is never orphaned.
            try
            {
                await hostedProcess.TerminateAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best-effort termination; still proceed to dispose.
            }
        }

        // Cancel whichever racing task is still running.
        raceCts.Cancel();
        try { await exitTask.ConfigureAwait(false); } catch { }
        try { await delayTask.ConfigureAwait(false); } catch { }

        // 3. Await the stderr drain so it finishes before disposing the process.
        if (_stderrDrainTask is not null)
        {
            try { await _stderrDrainTask.ConfigureAwait(false); } catch { }
        }

        // 4. Dispose the connection (faults any remaining pending requests as Disconnected).
        if (_connection is not null)
        {
            try { await _connection.DisposeAsync().ConfigureAwait(false); } catch { }
        }

        // 5. Dispose the process.
        try { await hostedProcess.DisposeAsync().ConfigureAwait(false); } catch { }
    }

    private async Task DrainStderrAsync(TextReader stderr)
    {
        try
        {
            while (true)
            {
                string? line = await stderr.ReadLineAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (_log is not null)
                {
                    await _log.WriteAsync(
                        new StructuredLogEvent(
                            RedactingLogLevel.Debug,
                            "AppServerStderrLine",
                            new Dictionary<string, string?>
                            {
                                ["Length"] = line.Length.ToString(CultureInfo.InvariantCulture),
                            }),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // The stderr drain must never fault the session.
        }
    }

    private sealed class SystemDelay : IDelay
    {
        public static readonly SystemDelay Instance = new();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(delay, cancellationToken);
    }
}

public sealed record AppServerSession(
    CodexAppServerGateway Gateway,
    Task Completion);
