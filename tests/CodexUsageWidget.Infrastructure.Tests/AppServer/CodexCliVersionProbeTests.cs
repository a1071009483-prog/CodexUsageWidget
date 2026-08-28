using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class CodexCliVersionProbeTests
{
    [Fact]
    public async Task GetVersionAsyncParsesCodexCliVersion()
    {
        var host = new TestProcessHost(
            stdout: "codex-cli 0.148.0-alpha.9\n",
            stderr: "",
            exitCode: 0);
        var probe = new CodexCliVersionProbe(host);

        CodexCliVersionResult result = await probe.GetVersionAsync("codex.exe", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("0.148.0-alpha.9", result.Version);
        Assert.Equal(["--version"], host.LastRequest!.Arguments);
    }

    [Fact]
    public async Task GetVersionAsyncFallsBackToStandardError()
    {
        var host = new TestProcessHost(
            stdout: "",
            stderr: "codex-cli 0.148.0\n",
            exitCode: 0);
        var probe = new CodexCliVersionProbe(host);

        CodexCliVersionResult result = await probe.GetVersionAsync("codex.exe", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("0.148.0", result.Version);
    }

    [Fact]
    public async Task GetVersionAsyncReportsNonZeroExit()
    {
        var host = new TestProcessHost(stdout: "", stderr: "boom", exitCode: 1);
        var probe = new CodexCliVersionProbe(host);

        CodexCliVersionResult result = await probe.GetVersionAsync("codex.exe", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Version);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
    }

    [Fact]
    public async Task GetVersionAsyncReportsUnrecognizedOutput()
    {
        var host = new TestProcessHost(stdout: "hello world\n", stderr: "", exitCode: 0);
        var probe = new CodexCliVersionProbe(host);

        CodexCliVersionResult result = await probe.GetVersionAsync("codex.exe", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task GetVersionAsyncReportsStartFailure()
    {
        var host = new TestProcessHost(stdout: "", stderr: "", exitCode: 0)
        {
            StartException = new InvalidOperationException("not found"),
        };
        var probe = new CodexCliVersionProbe(host);

        CodexCliVersionResult result = await probe.GetVersionAsync("codex.exe", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task GetVersionAsyncPropagatesCancellation()
    {
        var host = new TestProcessHost(stdout: "", stderr: "", exitCode: 0);
        var probe = new CodexCliVersionProbe(host);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.GetVersionAsync("codex.exe", cts.Token));
    }

    private sealed class TestProcessHost : IProcessHost
    {
        private readonly string _stdout;
        private readonly string _stderr;
        private readonly int _exitCode;

        public TestProcessHost(string stdout, string stderr, int exitCode)
        {
            _stdout = stdout;
            _stderr = stderr;
            _exitCode = exitCode;
        }

        public ProcessStartRequest? LastRequest { get; private set; }

        public Exception? StartException { get; set; }

        public Task<IHostedProcess> StartAsync(
            ProcessStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StartException is not null)
            {
                throw StartException;
            }

            LastRequest = request;
            IHostedProcess process = new FakeHostedProcess(_stdout, _stderr, _exitCode);
            return Task.FromResult(process);
        }
    }

    private sealed class FakeHostedProcess : IHostedProcess
    {
        private readonly int _exitCode;

        public FakeHostedProcess(string stdout, string stderr, int exitCode)
        {
            StandardOutput = new StringReader(stdout);
            StandardError = new StringReader(stderr);
            _exitCode = exitCode;
        }

        public TextWriter StandardInput { get; } = new StringWriter();

        public TextReader StandardOutput { get; }

        public TextReader StandardError { get; }

        public Task<ProcessExitResult> WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessExitResult(_exitCode));

        public Task<ProcessExitResult> TerminateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessExitResult(_exitCode, WasTerminated: true));

        public ValueTask DisposeAsync()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
            StandardInput.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
