using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class ManualClock : IClock
{
    private DateTimeOffset _now;

    public ManualClock(DateTimeOffset start) => _now = start;

    public DateTimeOffset UtcNow => _now;

    public void Set(DateTimeOffset value) => _now = value;

    public void Advance(TimeSpan delta) => _now += delta;
}
