namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Writes structured events after removing sensitive keys and values.
/// </summary>
public interface IRedactingLog
{
    ValueTask WriteAsync(
        StructuredLogEvent logEvent,
        CancellationToken cancellationToken);
}

public enum RedactingLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}

public sealed record StructuredLogEvent(
    RedactingLogLevel Level,
    string EventName,
    IReadOnlyDictionary<string, string?> Properties);
