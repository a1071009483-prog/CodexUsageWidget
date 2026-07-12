namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Persists redacted activation audit metadata. Implementations store only
/// non-sensitive fields (timestamps, account namespace hashes, model IDs, quota
/// snapshots, outcome/error categories). They MUST NOT accept or persist tokens,
/// cookies, raw credentials, prompt/response bodies, raw email addresses, or
/// unredacted workspace content.
/// </summary>
public interface IAuditStore
{
    /// <summary>
    /// Writes or overwrites the audit row identified by
    /// <paramref name="entry.AuditId"/u003e. The row is durably flushed before the
    /// task completes.
    /// </summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the audit row with the given identifier, or <c>null</c> if none exists.
    /// </summary>
    Task<AuditEntry?> ReadAsync(string auditId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all audit rows ordered by <see cref="AuditEntry.RecordedAt"/>
    /// descending (newest first), suitable for the local audit view.
    /// </summary>
    IAsyncEnumerable<AuditEntry> ReadAllAsync(CancellationToken cancellationToken);
}
