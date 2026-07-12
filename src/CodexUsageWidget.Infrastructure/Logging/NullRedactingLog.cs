using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Logging;

/// <summary>
/// No-op redacting log used when logging is not required.
/// </summary>
public sealed class NullRedactingLog : IRedactingLog
{
    public ValueTask WriteAsync(
        StructuredLogEvent logEvent,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
