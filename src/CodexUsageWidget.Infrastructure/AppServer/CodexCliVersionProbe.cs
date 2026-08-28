using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.AppServer;

/// <summary>
/// Result of probing the installed Codex CLI for its version.
/// </summary>
public sealed record CodexCliVersionResult(
    bool Succeeded,
    string? Version,
    string Diagnostic);

/// <summary>
/// Probes <c>codex --version</c> through the existing process abstraction so
/// startup diagnostics and acceptance evidence can record the CLI version.
/// </summary>
public sealed class CodexCliVersionProbe
{
    private readonly IProcessHost _processHost;

    public CodexCliVersionProbe(IProcessHost processHost)
    {
        _processHost = processHost ?? throw new ArgumentNullException(nameof(processHost));
    }

    public async Task<CodexCliVersionResult> GetVersionAsync(
        string command,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        try
        {
            await using IHostedProcess process = await _processHost.StartAsync(
                new ProcessStartRequest(command, ["--version"], null),
                cancellationToken).ConfigureAwait(false);

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            ProcessExitResult exit = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            if (exit.ExitCode != 0)
            {
                return new CodexCliVersionResult(false, null, "Codex CLI version command failed.");
            }

            string text = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            string? version = ParseVersion(text);
            return version is null
                ? new CodexCliVersionResult(false, null, "Codex CLI version output was not recognized.")
                : new CodexCliVersionResult(true, version, "Codex CLI version detected.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new CodexCliVersionResult(false, null, "Codex CLI version command could not be started.");
        }
    }

    internal static string? ParseVersion(string value)
    {
        string[] parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.FirstOrDefault(part => char.IsDigit(part.FirstOrDefault()));
    }
}
