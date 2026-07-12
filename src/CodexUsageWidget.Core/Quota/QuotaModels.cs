namespace CodexUsageWidget.Core.Quota;

/// <summary>
/// Identifies a quota bucket recognized by the monitor.
/// </summary>
public enum QuotaBucket
{
    /// <summary>A rolling five-hour usage window.</summary>
    FiveHour,

    /// <summary>A rolling seven-day usage window.</summary>
    Weekly,
}

/// <summary>
/// Describes the current state of the monitor's connection to its quota source.
/// </summary>
public enum MonitoringConnectionState
{
    /// <summary>A successful read completed recently.</summary>
    Connected,

    /// <summary>A read attempt is in progress.</summary>
    Connecting,

    /// <summary>The source is unreachable or has not been started.</summary>
    Disconnected,

    /// <summary>The source requires authentication before reads can succeed.</summary>
    AuthenticatingRequired,

    /// <summary>The most recent read failed and the monitor is retrying.</summary>
    Error,
}

/// <summary>
/// Raw window data consumed by <see cref="QuotaNormalizer"/>. Core-owned and independent
/// of any infrastructure serialization format.
/// </summary>
/// <param name="UsedPercent">The percentage of the bucket already consumed.</param>
/// <param name="ResetsAt">The bucket reset time as a Unix timestamp, interpreted by <see cref="QuotaNormalizer"/>.</param>
/// <param name="WindowDurationMins">The bucket window duration in minutes, if known.</param>
public sealed record RawRateLimitWindow(
    int UsedPercent,
    long? ResetsAt = null,
    long? WindowDurationMins = null);

/// <summary>
/// Raw snapshot data consumed by <see cref="QuotaNormalizer"/>. Core-owned and independent
/// of any infrastructure serialization format.
/// </summary>
/// <param name="LimitId">The identifier of the applied limit.</param>
/// <param name="LimitName">A human-readable name for the applied limit.</param>
/// <param name="PlanType">The plan or workspace label used as the account scope.</param>
/// <param name="Primary">The primary rate-limit window.</param>
/// <param name="BucketsByLimitId">Optional secondary buckets keyed by limit identifier.</param>
public sealed record RawRateLimitSnapshot(
    string? LimitId = null,
    string? LimitName = null,
    string? PlanType = null,
    RawRateLimitWindow? Primary = null,
    IReadOnlyDictionary<string, RawRateLimitWindow>? BucketsByLimitId = null);

/// <summary>
/// The result of a single read from an <see cref="Abstractions.IQuotaSource"/>.
/// </summary>
/// <param name="IsSuccess">Whether the read produced usable data.</param>
/// <param name="Snapshot">The raw snapshot when <paramref name="IsSuccess"/> is <c>true</c>.</param>
/// <param name="ErrorMessage">A human-readable failure reason when <paramref name="IsSuccess"/> is <c>false</c>.</param>
public sealed record QuotaSourceResult(
    bool IsSuccess,
    RawRateLimitSnapshot? Snapshot = null,
    string? ErrorMessage = null);

/// <summary>
/// A normalized view of a single quota bucket.
/// </summary>
public sealed record QuotaBucketSnapshot(
    QuotaBucket Bucket,
    int UsedPercent,
    int RemainingPercent,
    DateTimeOffset? ResetsAt,
    long? WindowDurationMinutes,
    bool IsAvailable);

/// <summary>
/// The raw eligibility values required by downstream five-hour activation logic.
/// </summary>
/// <param name="IsFresh">Whether the snapshot is fresh enough to trigger activation.</param>
/// <param name="UsedPercent">The raw used percentage of the five-hour bucket.</param>
/// <param name="ResetsAt">The five-hour bucket reset time, if known.</param>
/// <param name="IsAvailable">Whether the five-hour bucket is available.</param>
public sealed record QuotaTriggerInput(
    bool IsFresh,
    int UsedPercent,
    DateTimeOffset? ResetsAt,
    bool IsAvailable);

/// <summary>
/// A normalized, immutable quota snapshot produced by the monitor.
/// </summary>
public sealed record QuotaSnapshot(
    string? ScopeLabel,
    QuotaBucketSnapshot FiveHour,
    QuotaBucketSnapshot Weekly,
    DateTimeOffset SyncedAt,
    bool IsFresh,
    MonitoringConnectionState ConnectionState,
    TimeSpan? Countdown)
{
    /// <summary>
    /// Gets the eligibility input for the five-hour bucket.
    /// </summary>
    public QuotaTriggerInput FiveHourTriggerInput => new(
        IsFresh,
        FiveHour.UsedPercent,
        FiveHour.ResetsAt,
        FiveHour.IsAvailable);
}
