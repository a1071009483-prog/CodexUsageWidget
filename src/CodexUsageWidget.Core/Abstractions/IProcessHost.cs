namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Starts an external process without exposing platform process types to Core.
/// </summary>
public interface IProcessHost
{
    Task<IHostedProcess> StartAsync(
        ProcessStartRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Represents the redirected streams and lifetime of a hosted process.
/// </summary>
public interface IHostedProcess : IAsyncDisposable
{
    TextWriter StandardInput { get; }

    TextReader StandardOutput { get; }

    TextReader StandardError { get; }

    Task<ProcessExitResult> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently terminates the hosted process tree. If the process is still
    /// running, the entire child process tree is killed and the actual exit is
    /// awaited, returning <see cref="ProcessExitResult.WasTerminated"/> = true.
    /// If the process has already exited, the observed natural exit result is
    /// returned. Repeated calls return the same completed lifetime result.
    /// </summary>
    Task<ProcessExitResult> TerminateAsync(CancellationToken cancellationToken);
}

public sealed record ProcessStartRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null);

public sealed record ProcessExitResult(int ExitCode, bool WasTerminated = false);
