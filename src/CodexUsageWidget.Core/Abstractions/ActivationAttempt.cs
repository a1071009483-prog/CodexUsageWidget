namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Immutable snapshot of a single activation attempt persisted in the durable
/// write-ahead lock store. This record stores ONLY non-sensitive metadata: a
/// stable account namespace hash (never the raw email), workspace scope, window
/// identity, quota field snapshots, model ID, and outcome categories. It excludes
/// tokens, cookies, raw credentials, prompt content, and response bodies.
///
/// All time fields are UTC ISO-8601 strings (e.g. "2026-07-12T07:46:00Z") to keep
/// the durable representation timezone-stable across process restarts. The
/// <see cref="TurnStarted"/> flag is persisted as a SQLite INTEGER (0/1) and read
/// back as a bool.
/// </summary>
/// <param name="AttemptId">Stable unique identifier for this attempt.</param>
/// <param name="NamespaceHash">Opaque hash of account identity; never the raw email.</param>
/// <param name="WorkspaceScope">Workspace scope string; never null.</param>
/// <param name="WindowKey">
/// Five-hour window identity. Uses the authoritative server reset epoch when one
/// is available ("authoritative" kind), otherwise a durable local eligibility
/// epoch ("local" kind).
/// </param>
/// <param name="WindowKind">Either "authoritative" or "local".</param>
/// <param name="SuppressionDeadline">UTC ISO-8601 string; the attempt is guarded until this deadline.</param>
/// <param name="ObservedAt">UTC ISO-8601 string of the eligibility observation.</param>
/// <param name="AttemptAt">UTC ISO-8601 string of the lock insertion time.</param>
/// <param name="PreUsedPercent">Pre-activation usedPercent snapshot.</param>
/// <param name="PreResetsAt">Pre-activation resetsAt, or null if unavailable.</param>
/// <param name="ModelId">Selected model ID, or null if not yet resolved.</param>
/// <param name="TurnStarted">
/// Whether a generation turn was accepted/started. Once true, no further
/// generation may be issued for this scoped window.
/// </param>
/// <param name="TerminalOutcome">
/// Terminal outcome category ("succeeded", "unknown", "failed", "externally-satisfied"),
/// or null while the attempt is still pending.
/// </param>
/// <param name="PostUsedPercent">Post-activation usedPercent, or null if not yet recorded.</param>
/// <param name="PostResetsAt">Post-activation resetsAt, or null if not yet recorded.</param>
/// <param name="CleanupState">
/// Cleanup state: "none" (default), "completed", "deferred", etc.
/// </param>
public sealed record ActivationAttempt(
    string AttemptId,
    string NamespaceHash,
    string WorkspaceScope,
    string WindowKey,
    string WindowKind,
    string SuppressionDeadline,
    string ObservedAt,
    string AttemptAt,
    int PreUsedPercent,
    string? PreResetsAt,
    string? ModelId,
    bool TurnStarted,
    string? TerminalOutcome,
    int? PostUsedPercent,
    string? PostResetsAt,
    string CleanupState);
