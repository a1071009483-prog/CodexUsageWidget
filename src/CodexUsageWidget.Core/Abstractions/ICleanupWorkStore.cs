namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Lifecycle state of a deferred cleanup work item. Cleanup is delete-only and
/// MUST NOT lead back to model generation.
/// </summary>
public enum CleanupWorkState
{
    /// <summary>
    /// The cleanup item is waiting to be processed.
    /// </summary>
    Pending,

    /// <summary>
    /// The cleanup item completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The cleanup item failed and will remain available for later retry.
    /// </summary>
    Failed,
}

/// <summary>
/// A single deferred cleanup work item, typically the deletion of a temporary
/// activation thread. Contains only non-sensitive identifiers needed to perform
/// the delete operation.
/// </summary>
/// <param name="CleanupId">Unique identifier for this cleanup item.</param>
/// <param name="AttemptId">Reference to the related activation attempt.</param>
/// <param name="ThreadId">Identifier of the temporary thread to delete.</param>
/// <param name="EnqueuedAt">UTC ISO-8601 string when the item was enqueued.</param>
/// <param name="State">Current lifecycle state of the cleanup item.</param>
public sealed record CleanupWorkItem(
    string CleanupId,
    string AttemptId,
    string ThreadId,
    string EnqueuedAt,
    CleanupWorkState State);

/// <summary>
/// Durable queue for deferred cleanup work. Implementations MUST NOT create
/// model turns or retry activation; they only record, retrieve, and update
/// delete-only cleanup items.
/// </summary>
public interface ICleanupWorkStore
{
    /// <summary>
    /// Records a cleanup item for the given attempt and thread. The call is
    /// idempotent for the same <paramref name="attemptId"/> and
    /// <paramref name="threadId"/> pair: duplicate enqueues do not create
    /// additional rows.
    /// </summary>
    Task EnqueueAsync(
        string attemptId,
        string threadId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically retrieves the oldest pending cleanup item and marks it as
    /// being processed, or returns <c>null</c> when no pending items exist.
    /// Implementations may leave the item in a pending state and rely on
    /// <see cref="MarkFailedAsync"/> to make it available again.
    /// </summary>
    Task<CleanupWorkItem?> TryTakePendingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marks the cleanup item as completed. Completed items are no longer
    /// returned by <see cref="TryTakePendingAsync"/>.
    /// </summary>
    Task MarkCompletedAsync(string cleanupId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the cleanup item as failed so it remains eligible for retry.
    /// </summary>
    Task MarkFailedAsync(string cleanupId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all pending cleanup items ordered by enqueue time ascending.
    /// </summary>
    IAsyncEnumerable<CleanupWorkItem> ReadPendingAsync(CancellationToken cancellationToken);
}
