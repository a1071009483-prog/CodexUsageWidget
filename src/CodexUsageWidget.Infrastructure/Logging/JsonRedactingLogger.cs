using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Logging;

public sealed class JsonRedactingLogger : IRedactingLog, IDisposable
{
    private const string RedactedValue = "[REDACTED]";

    private static readonly string[] SensitiveKeyFragments =
    [
        "token",
        "secret",
        "credential",
        "cookie",
        "password",
        "authorization",
        "email",
        "workspace_path",
        "prompt",
        "response",
    ];

    private static readonly Regex BearerOrKeyPattern = new(
        @"(?i)(?:\bbearer\s+\S+|\bsk-[a-z0-9_-]{6,})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AbsolutePathPattern = new(
        @"(?i)(?:\b[A-Z]:\\|(?:^|\s)/(?:users|home|var|tmp|etc)/)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly TextWriter _output;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonRedactingLogger(TextWriter output, IClock clock)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async ValueTask WriteAsync(
        StructuredLogEvent logEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, string?> safeProperties = Redact(logEvent.Properties);

        string line = JsonSerializer.Serialize(new
        {
            timestampUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            level = logEvent.Level.ToString(),
            eventName = logEvent.EventName,
            properties = safeProperties,
        });

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteLineAsync(line).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static Dictionary<string, string?> Redact(
        IReadOnlyDictionary<string, string?> properties)
    {
        var safe = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach ((string key, string? value) in properties)
        {
            if (SensitiveKeyFragments.Any(
                    fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            safe[key] = IsSensitiveValue(value) ? RedactedValue : value;
        }

        return safe;
    }

    private static bool IsSensitiveValue(string? value) =>
        value is not null
        && (BearerOrKeyPattern.IsMatch(value)
            || EmailPattern.IsMatch(value)
            || AbsolutePathPattern.IsMatch(value));

    public void Dispose() => _writeGate.Dispose();
}
