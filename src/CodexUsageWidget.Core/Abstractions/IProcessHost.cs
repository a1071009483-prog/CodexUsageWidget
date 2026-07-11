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
}

public sealed record ProcessStartRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null);

public sealed record ProcessExitResult(int ExitCode, bool WasTerminated = false);
