namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// A redacted, non-sensitive quota snapshot recorded as part of an audit entry.
/// Contains only the numeric values and UTC reset instant needed to verify the
/// activation outcome; it excludes prompts, responses, and raw account data.
/// </summary>
/// <param name="UsedPercent">The server's raw used percentage at the time of the snapshot.</param>
/// <param name="RemainingPercent">Clamped remaining percentage computed as <c>100 - usedPercent</c>.</param>
/// <param name="ResetsAt">UTC ISO-8601 reset instant, or <c>null</c> when unavailable.</param>
public sealed record AuditQuotaSnapshot(
    int UsedPercent,
    int RemainingPercent,
    string? ResetsAt);

/// <summary>
/// Immutable redacted audit record persisted for each activation attempt. The
/// store excludes tokens, cookies, raw credentials, prompt/response bodies,
/// raw email addresses, and unredacted workspace content.
/// </summary>
/// <param name="AuditId">Unique identifier for this audit row.</param>
/// <param name="NamespaceHash">Opaque account namespace hash; never the raw email.</param>
/// <param name="AttemptId">Reference to the activation attempt, or <c>null</c> for observations without a lock.</param>
/// <param name="ModelId">Selected model identifier, or <c>null</c> if not yet resolved.</param>
/// <param name="ObservedAt">UTC ISO-8601 string of the eligibility observation.</param>
/// <param name="PreQuota">Pre-activation quota snapshot, or <c>null</c>.</param>
/// <param name="PostQuota">Post-activation quota snapshot, or <c>null</c>.</param>
/// <param name="TurnCrossedBoundary">Whether a generation turn was accepted/started.</param>
/// <param name="Outcome">Terminal outcome category, or <c>null</c> while pending.</param>
/// <param name="ErrorCategory">Redacted error category, or <c>null</c> on success.</param>
/// <param name="RecordedAt">UTC ISO-8601 string when the audit row was recorded.</param>
public sealed record AuditEntry(
    string AuditId,
    string NamespaceHash,
    string? AttemptId,
    string? ModelId,
    string ObservedAt,
    AuditQuotaSnapshot? PreQuota,
    AuditQuotaSnapshot? PostQuota,
    bool TurnCrossedBoundary,
    string? Outcome,
    string? ErrorCategory,
    string RecordedAt);
