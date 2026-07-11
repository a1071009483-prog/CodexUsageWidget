namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Reads and writes application-owned text without exposing file-system types to Core.
/// </summary>
public interface IAppFileSystem
{
    Task<AppFileReadResult> ReadTextAsync(
        AppFileReadRequest request,
        CancellationToken cancellationToken);

    Task<AppFileWriteResult> WriteTextAsync(
        AppFileWriteRequest request,
        CancellationToken cancellationToken);
}

public sealed record AppFileReadRequest(string Path);

public sealed record AppFileReadResult(
    bool Succeeded,
    string? Content = null,
    string? FailureReason = null);

public sealed record AppFileWriteRequest(string Path, string Content);

public sealed record AppFileWriteResult(bool Succeeded, string? FailureReason = null);
