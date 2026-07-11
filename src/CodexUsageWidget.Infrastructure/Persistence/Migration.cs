namespace CodexUsageWidget.Infrastructure.Persistence;

/// <summary>
/// Immutable description of a single versioned database migration.
/// </summary>
/// <remarks>
/// <para>
/// Contract: <see cref="UpSql"/> MUST be idempotent — re-applying it against a database
/// that already contains its effects must succeed without error.
/// </para>
/// <para>
/// This is required because <c>PRAGMA user_version</c> is not transactional in SQLite:
/// it is advanced AFTER the migration transaction commits. If the process crashes between
/// the commit and the version advance, a subsequent <see cref="DatabaseMigrator.MigrateAsync"/>
/// call observes a stale version and re-applies the same migration. Idempotent SQL (e.g.
/// <c>CREATE TABLE IF NOT EXISTS</c>) makes that recovery safe. Non-idempotent data
/// migrations must be written so re-application is a no-op or guards on existing state.
/// </para>
/// </remarks>
public sealed record Migration(int Version, string Name, string UpSql);
