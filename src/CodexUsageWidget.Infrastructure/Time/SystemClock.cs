using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Time;

/// <summary>
/// Production implementation of <see cref="IClock"/> that returns the current UTC time.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
