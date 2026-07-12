namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Writes redacted crash reports to local storage. Implementations must not retain
/// tokens, cookies, raw credentials, prompt/response bodies, or unredacted workspace content.
/// </summary>
public interface ICrashReportWriter
{
    /// <summary>
    /// Writes a redacted report for an unhandled exception and returns the path to the report.
    /// </summary>
    Task<string?> WriteAsync(
        Exception exception,
        CancellationToken cancellationToken = default);
}
