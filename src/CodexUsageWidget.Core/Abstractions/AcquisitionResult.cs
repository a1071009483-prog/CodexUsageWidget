namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Result of attempting to acquire the durable activation lock for a scoped
/// five-hour window. When <see cref="Acquired"/> is <c>true</c>, the caller has
/// established the at-most-once guard and may proceed toward generation. When
/// <c>false</c>, an existing lock already covers the same
/// (<c>namespace_hash</c>, <c>workspace_scope</c>, <c>window_key</c>) triple and
/// <see cref="Existing"/> carries the persisted attempt that blocks this one.
/// </summary>
/// <param name="Acquired">
/// <c>true</c> if this attempt was newly persisted as the authoritative lock;
/// <c>false</c> if a duplicate key already holds the lock.
/// </param>
/// <param name="Existing">
/// The existing lock when <see cref="Acquired"/> is <c>false</c>; otherwise <c>null</c>.
/// </param>
public sealed record AcquisitionResult(
    bool Acquired,
    ActivationAttempt? Existing);
