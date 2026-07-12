namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Durable scoped write-ahead deduplication store for activation attempts.
///
/// Every operation runs inside a SQLite transaction with <c>PRAGMA synchronous = FULL</c>
/// so that the at-most-once guarantee survives a process crash or power loss. This
/// interface performs NO model consumption (<c>thread/start</c>/<c>turn/start</c>) —
/// it is a pure storage lock. The coordinator acquires the lock before any
/// generation request and retains it through the suppression period.
/// </summary>
public interface IActivationLockStore
{
    /// <summary>
    /// Atomically persists <paramref name="attempt"/> as the write-ahead lock for
    /// its (<c>namespace_hash</c>, <c>workspace_scope</c>, <c>window_key</c>) triple.
    /// On success the lock is durably flushed. On a UNIQUE-constraint conflict the
    /// call returns <see cref="AcquisitionResult.Acquired"/>=<c>false</c> with the
    /// existing attempt in <see cref="AcquisitionResult.Existing"/> — it does NOT
    /// throw. Any other SQLite/IO error fails closed by propagating the exception.
    /// </summary>
    Task<AcquisitionResult> TryAcquireAsync(
        ActivationAttempt attempt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the active (most recently persisted) attempt for the given scoped key,
    /// or <c>null</c> when none exists. Used by crash-recovery to re-establish the
    /// at-most-once guard before evaluating new candidates.
    /// </summary>
    Task<ActivationAttempt?> GetActiveAsync(
        string namespaceHash,
        string workspaceScope,
        string windowKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets <c>turn_started</c> to <c>true</c> for the given attempt. Idempotent:
    /// setting it again on an already-started attempt is a no-op and produces no
    /// error. Durably flushed. Once this flag is set, no further generation may be
    /// issued for the scoped window.
    /// </summary>
    Task MarkTurnStartedAsync(
        string attemptId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the terminal outcome, post-activation quota snapshot, and cleanup
    /// state for the given attempt. Durably flushed. After a terminal outcome the
    /// lock is retained through its suppression period; the coordinator never
    /// retries generation for that scoped window.
    /// </summary>
    Task MarkTerminalAsync(
        string attemptId,
        string terminalOutcome,
        int? postUsedPercent,
        string? postResetsAt,
        string cleanupState,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extends the suppression deadline for the given attempt. Durably flushed.
    /// Used to align the local guard with a later verified server reset time.
    /// </summary>
    Task ExtendSuppressionDeadlineAsync(
        string attemptId,
        string newSuppressionDeadline,
        CancellationToken cancellationToken);
}
