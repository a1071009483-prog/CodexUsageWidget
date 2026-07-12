using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Tests.Testing;

internal sealed class ManualClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);
}
