namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Supplies UTC time to code that must remain deterministic in tests.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
