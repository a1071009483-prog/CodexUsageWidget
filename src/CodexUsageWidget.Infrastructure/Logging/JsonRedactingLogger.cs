using System.Globalization;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Security;

namespace CodexUsageWidget.Infrastructure.Logging;

public sealed class JsonRedactingLogger : IRedactingLog, IDisposable
{
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
            if (SensitiveDataRedactor.SensitiveKeyFragments.Any(
                    fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            safe[key] = SensitiveDataRedactor.Redact(value);
        }

        return safe;
    }

    public void Dispose() => _writeGate.Dispose();
}
