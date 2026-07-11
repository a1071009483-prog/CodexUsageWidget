namespace CodexUsageWidget.Infrastructure.Persistence;

/// <summary>
/// Immutable description of a single versioned database migration.
/// </summary>
public sealed record Migration(int Version, string Name, string UpSql);
