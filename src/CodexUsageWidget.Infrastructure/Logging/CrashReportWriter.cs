using System.Globalization;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Security;

namespace CodexUsageWidget.Infrastructure.Logging;

/// <summary>
/// Writes JSON crash reports under the current user's local application data directory.
/// Reports contain only the exception type, a redacted message, and an optional stack
/// trace; they never include tokens, cookies, raw credentials, or workspace content.
/// </summary>
public sealed class CrashReportWriter : ICrashReportWriter
{
    private readonly IAppFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly string _directory;
    private readonly string _applicationName;

    public CrashReportWriter(
        IAppFileSystem fileSystem,
        IClock clock,
        string directory,
        string applicationName)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _applicationName = applicationName ?? throw new ArgumentNullException(nameof(applicationName));
    }

    /// <inheritdoc/>
    public async Task<string?> WriteAsync(
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        cancellationToken.ThrowIfCancellationRequested();

        string timestamp = _clock.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string fileName = $"{_applicationName}-crash-{timestamp}-{exception.GetType().Name}.json";
        string path = Path.Combine(_directory, fileName);

        string report = JsonSerializer.Serialize(new
        {
            timestampUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            application = _applicationName,
            exceptionType = exception.GetType().FullName,
            message = SensitiveDataRedactor.Redact(exception.Message) ?? string.Empty,
            stackTrace = exception.StackTrace,
        });

        AppFileWriteResult result = await _fileSystem.WriteTextAsync(
                new AppFileWriteRequest(path, report),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded ? path : null;
    }
}
