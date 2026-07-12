namespace CodexUsageWidget.Core.Quota;

/// <summary>
/// Pure, deterministic logic that converts a raw rate-limit snapshot into a normalized
/// <see cref="QuotaSnapshot"/>.
/// </summary>
public static class QuotaNormalizer
{
    private const long FiveHourDurationMinutes = 300L;
    private const long WeeklyDurationMinutes = 10080L;
    private const long WeeklyDurationToleranceMinutes = 60L;

    /// <summary>
    /// Values below this threshold are treated as seconds since the Unix epoch; values at
    /// or above it are treated as milliseconds since the Unix epoch. The threshold keeps
    /// real-world second timestamps (around 1.7 billion) distinct from millisecond
    /// timestamps (around 1.7 trillion).
    /// </summary>
    private const long UnixSecondsMillisecondsThreshold = 1_000_000_000_000L;

    /// <summary>
    /// Normalizes the raw snapshot using the provided sync time and a connected state.
    /// </summary>
    /// <param name="snapshot">The raw snapshot to normalize.</param>
    /// <param name="syncedAt">The UTC time at which the snapshot was received.</param>
    /// <returns>A normalized quota snapshot.</returns>
    public static QuotaSnapshot Normalize(RawRateLimitSnapshot snapshot, DateTimeOffset syncedAt)
        => Normalize(snapshot, syncedAt, MonitoringConnectionState.Connected);

    /// <summary>
    /// Normalizes the raw snapshot using the provided sync time and connection state.
    /// </summary>
    /// <param name="snapshot">The raw snapshot to normalize.</param>
    /// <param name="syncedAt">The UTC time at which the snapshot was received.</param>
    /// <param name="connectionState">The connection state to record in the snapshot.</param>
    /// <returns>A normalized quota snapshot.</returns>
    public static QuotaSnapshot Normalize(
        RawRateLimitSnapshot snapshot,
        DateTimeOffset syncedAt,
        MonitoringConnectionState connectionState)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        IReadOnlyList<LabeledWindow> windows = CollectWindows(snapshot);

        QuotaBucketSnapshot fiveHour = ResolveFiveHour(windows);
        QuotaBucketSnapshot weekly = ResolveWeekly(windows);

        return new QuotaSnapshot(
            snapshot.PlanType ?? snapshot.LimitName,
            fiveHour,
            weekly,
            syncedAt,
            true,
            connectionState,
            null,
            null);
    }

    private static List<LabeledWindow> CollectWindows(RawRateLimitSnapshot snapshot)
    {
        List<LabeledWindow> windows = new();

        if (snapshot.Primary is not null)
        {
            windows.Add(new LabeledWindow(snapshot.LimitName, snapshot.Primary));
        }

        if (snapshot.BucketsByLimitId is not null)
        {
            foreach ((string? key, RawRateLimitWindow? window) in snapshot.BucketsByLimitId)
            {
                if (window is not null)
                {
                    windows.Add(new LabeledWindow(key, window));
                }
            }
        }

        return windows;
    }

    private static QuotaBucketSnapshot ResolveFiveHour(IReadOnlyList<LabeledWindow> windows)
    {
        LabeledWindow? candidate = null;

        foreach (LabeledWindow window in windows)
        {
            if (window.Window.WindowDurationMins == FiveHourDurationMinutes)
            {
                candidate = window;
                break;
            }
        }

        return candidate is not null
            ? ToBucketSnapshot(QuotaBucket.FiveHour, candidate.Value)
            : new QuotaBucketSnapshot(QuotaBucket.FiveHour, 0, 0, null, null, false);
    }

    private static QuotaBucketSnapshot ResolveWeekly(IReadOnlyList<LabeledWindow> windows)
    {
        List<LabeledWindow> candidates = new();

        foreach (LabeledWindow window in windows)
        {
            if (IsWeeklyCandidate(window))
            {
                candidates.Add(window);
            }
        }

        return candidates.Count == 1
            ? ToBucketSnapshot(QuotaBucket.Weekly, candidates[0])
            : new QuotaBucketSnapshot(QuotaBucket.Weekly, 0, 0, null, null, false);
    }

    private static bool IsWeeklyCandidate(LabeledWindow window)
    {
        long? duration = window.Window.WindowDurationMins;

        if (duration == WeeklyDurationMinutes)
        {
            return true;
        }

        if (duration is not null &&
            Math.Abs(duration.Value - WeeklyDurationMinutes) <= WeeklyDurationToleranceMinutes)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(window.Label))
        {
            string label = window.Label;
            if (label.Contains("weekly", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("week", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static QuotaBucketSnapshot ToBucketSnapshot(QuotaBucket bucket, LabeledWindow labeledWindow)
    {
        RawRateLimitWindow window = labeledWindow.Window;
        int remaining = 100 - window.UsedPercent;
        remaining = Math.Clamp(remaining, 0, 100);

        return new QuotaBucketSnapshot(
            bucket,
            window.UsedPercent,
            remaining,
            ConvertResetsAt(window.ResetsAt),
            window.WindowDurationMins,
            true);
    }

    private static DateTimeOffset? ConvertResetsAt(long? resetsAt)
    {
        if (resetsAt is null)
        {
            return null;
        }

        long value = resetsAt.Value;

        if (value < UnixSecondsMillisecondsThreshold)
        {
            return DateTimeOffset.FromUnixTimeSeconds(value);
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(value);
    }

    private readonly record struct LabeledWindow(string? Label, RawRateLimitWindow Window);
}
