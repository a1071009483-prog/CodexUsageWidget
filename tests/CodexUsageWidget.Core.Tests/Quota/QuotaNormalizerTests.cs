using System.Collections.Immutable;
using CodexUsageWidget.Core.Quota;
using Xunit;

namespace CodexUsageWidget.Core.Tests.Quota;

public sealed class QuotaNormalizerTests
{
    private static readonly DateTimeOffset SyncedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly QuotaBucketSnapshot UnavailableFiveHour = new(
        QuotaBucket.FiveHour, 0, 0, null, null, false);

    private static readonly QuotaBucketSnapshot UnavailableWeekly = new(
        QuotaBucket.Weekly, 0, 0, null, null, false);

    public static TheoryData<string, RawRateLimitSnapshot, QuotaBucketSnapshot, QuotaBucketSnapshot, string?> NormalizeCases()
    {
        var data = new TheoryData<string, RawRateLimitSnapshot, QuotaBucketSnapshot, QuotaBucketSnapshot, string?>();

        data.Add(
            "five-hour from primary 300-minute window",
            new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "Pro",
                new RawRateLimitWindow(50, 1_700_000_000_000L, 300L)),
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, 50, 50, DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000L), 300L, true),
            UnavailableWeekly,
            "Pro");

        data.Add(
            "weekly from 10080-minute duration in dictionary",
            new RawRateLimitSnapshot(
                "limit-week",
                "weekly credits",
                "Pro",
                null,
                new Dictionary<string, RawRateLimitWindow>
                {
                    ["weekly"] = new(30, 1_700_000_000L, 10080L),
                }.ToImmutableDictionary()),
            UnavailableFiveHour,
            new QuotaBucketSnapshot(QuotaBucket.Weekly, 30, 70, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L), 10080L, true),
            "Pro");

        data.Add(
            "weekly from label containing 'weekly' with non-standard duration",
            new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "Pro",
                new RawRateLimitWindow(0, null, 300L),
                new Dictionary<string, RawRateLimitWindow>
                {
                    ["weekly-credits"] = new(20, null, 60L),
                }.ToImmutableDictionary()),
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, 0, 100, null, 300L, true),
            new QuotaBucketSnapshot(QuotaBucket.Weekly, 20, 80, null, 60L, true),
            "Pro");

        data.Add(
            "weekly from duration near seven days",
            new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "Pro",
                new RawRateLimitWindow(10, null, 300L),
                new Dictionary<string, RawRateLimitWindow>
                {
                    ["rolling-week"] = new(15, null, 10050L),
                }.ToImmutableDictionary()),
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, 10, 90, null, 300L, true),
            new QuotaBucketSnapshot(QuotaBucket.Weekly, 15, 85, null, 10050L, true),
            "Pro");

        data.Add(
            "ambiguous weekly candidates make weekly unavailable",
            new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "Pro",
                new RawRateLimitWindow(10, null, 300L),
                new Dictionary<string, RawRateLimitWindow>
                {
                    ["weekly-a"] = new(15, null, 10080L),
                    ["weekly-b"] = new(20, null, 10080L),
                }.ToImmutableDictionary()),
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, 10, 90, null, 300L, true),
            UnavailableWeekly,
            "Pro");

        data.Add(
            "invalid buckets produce unavailable snapshots",
            new RawRateLimitSnapshot("limit-x", "unknown", "Free", null),
            UnavailableFiveHour,
            UnavailableWeekly,
            "Free");

        data.Add(
            "used percent above 100 clamps remaining to 0",
            new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "Pro",
                new RawRateLimitWindow(150, null, 300L)),
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, 150, 0, null, 300L, true),
            UnavailableWeekly,
            "Pro");

        data.Add(
            "used percent below 0 clamps remaining to 100",
            new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "Pro",
                new RawRateLimitWindow(-20, null, 300L)),
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, -20, 100, null, 300L, true),
            UnavailableWeekly,
            "Pro");

        data.Add(
            "scope label falls back to limit name when plan type missing",
            new RawRateLimitSnapshot(
                "limit-5h",
                "Team Plan",
                null,
                new RawRateLimitWindow(0, null, 300L)),
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, 0, 100, null, 300L, true),
            UnavailableWeekly,
            "Team Plan");

        data.Add(
            "sparse update with only five-hour present leaves weekly unavailable",
            new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "Pro",
                new RawRateLimitWindow(75, null, 300L)),
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, 75, 25, null, 300L, true),
            UnavailableWeekly,
            "Pro");

        data.Add(
            "resetsAt in seconds is converted correctly",
            new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "Pro",
                new RawRateLimitWindow(0, 1_700_000_000L, 300L)),
            new QuotaBucketSnapshot(
                QuotaBucket.FiveHour,
                0,
                100,
                DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L),
                300L,
                true),
            UnavailableWeekly,
            "Pro");

        data.Add(
            "resetsAt in milliseconds is converted correctly",
            new RawRateLimitSnapshot(
                "limit-5h",
                "5 hour credits",
                "Pro",
                new RawRateLimitWindow(0, 1_700_000_000_000L, 300L)),
            new QuotaBucketSnapshot(
                QuotaBucket.FiveHour,
                0,
                100,
                DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000L),
                300L,
                true),
            UnavailableWeekly,
            "Pro");

        return data;
    }

    [Theory]
    [MemberData(nameof(NormalizeCases))]
    public void NormalizeMapsRawSnapshotToQuotaSnapshot(
        string _,
        RawRateLimitSnapshot raw,
        QuotaBucketSnapshot expectedFiveHour,
        QuotaBucketSnapshot expectedWeekly,
        string? expectedScopeLabel)
    {
        QuotaSnapshot actual = QuotaNormalizer.Normalize(raw, SyncedAt);

        Assert.Equal(expectedScopeLabel, actual.ScopeLabel);
        Assert.Equal(expectedFiveHour, actual.FiveHour);
        Assert.Equal(expectedWeekly, actual.Weekly);
        Assert.Equal(SyncedAt, actual.SyncedAt);
        Assert.Equal(MonitoringConnectionState.Connected, actual.ConnectionState);
        Assert.True(actual.IsFresh);
    }

    [Fact]
    public void NormalizeHonorsSuppliedConnectionState()
    {
        RawRateLimitSnapshot raw = new(
            "limit-5h",
            "5 hour credits",
            "Pro",
            new RawRateLimitWindow(0, null, 300L));

        QuotaSnapshot actual = QuotaNormalizer.Normalize(raw, SyncedAt, MonitoringConnectionState.Error);

        Assert.Equal(MonitoringConnectionState.Error, actual.ConnectionState);
    }
}
