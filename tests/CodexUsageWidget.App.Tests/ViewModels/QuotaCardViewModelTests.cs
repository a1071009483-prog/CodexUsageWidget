using CodexUsageWidget.App.Tests.Testing;
using CodexUsageWidget.App.ViewModels;
using CodexUsageWidget.Core.Quota;
using Xunit;

namespace CodexUsageWidget.App.Tests.ViewModels;

public sealed class QuotaCardViewModelTests
{
    private static readonly DateTimeOffset SyncTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(QuotaBucket.FiveHour, "5小时")]
    [InlineData(QuotaBucket.Weekly, "本周")]
    public void BucketLabelsAreLocalized(QuotaBucket bucket, string expected)
    {
        QuotaCardViewModel vm = Create(bucket);

        Assert.Equal(expected, vm.BucketLabel);
    }

    [Theory]
    [InlineData(0, 100, QuotaCardColorState.Normal)]
    [InlineData(69, 31, QuotaCardColorState.Normal)]
    [InlineData(70, 30, QuotaCardColorState.Warning)]
    [InlineData(89, 11, QuotaCardColorState.Warning)]
    [InlineData(90, 10, QuotaCardColorState.Critical)]
    [InlineData(100, 0, QuotaCardColorState.Critical)]
    public void ColorStateFollowsRemainingThresholds(int used, int remaining, QuotaCardColorState expected)
    {
        QuotaCardViewModel vm = Create(QuotaBucket.FiveHour);
        vm.Update(Bucket(QuotaBucket.FiveHour, used, remaining, true), true, SyncTime, TimeSpan.FromHours(5));

        Assert.Equal(remaining, vm.RemainingPercent);
        Assert.Equal(expected, vm.ColorState);
    }

    [Theory]
    [InlineData(0, 0, false, "已过期")]
    [InlineData(10, 90, true, "已同步")]
    [InlineData(10, 90, false, "已过期")]
    [InlineData(0, 100, true, "100%·计时已启动")]
    public void StatusTextForFiveHourBucket(int used, int remaining, bool fresh, string expected)
    {
        QuotaCardViewModel vm = Create(QuotaBucket.FiveHour);
        vm.Update(Bucket(QuotaBucket.FiveHour, used, remaining, true), fresh, SyncTime, TimeSpan.FromHours(5));

        Assert.Equal(expected, vm.StatusText);
    }

    [Fact]
    public void WeeklyBucketNeverShowsActiveRounded100Label()
    {
        QuotaCardViewModel vm = Create(QuotaBucket.Weekly);
        vm.Update(Bucket(QuotaBucket.Weekly, 0, 100, true), true, SyncTime, null);

        Assert.Equal("已同步", vm.StatusText);
    }

    [Theory]
    [InlineData(0, 100, 0, "00:00:00")]
    [InlineData(0, 100, 3665, "01:01:05")]
    public void CountdownFormatting(int used, int remaining, int seconds, string expected)
    {
        QuotaCardViewModel vm = Create(QuotaBucket.FiveHour);
        vm.Update(
            Bucket(QuotaBucket.FiveHour, used, remaining, true),
            true,
            SyncTime,
            TimeSpan.FromSeconds(seconds));

        Assert.Equal(expected, vm.CountdownText);
    }

    [Fact]
    public void CountdownIsEmptyWhenUnavailable()
    {
        QuotaCardViewModel vm = Create(QuotaBucket.FiveHour);
        vm.Update(Bucket(QuotaBucket.FiveHour, 0, 100, true), true, SyncTime, null);

        Assert.Empty(vm.CountdownText);
    }

    [Fact]
    public void WeeklyBucketShowsCountdownWhenAvailable()
    {
        QuotaCardViewModel vm = Create(QuotaBucket.Weekly);
        DateTimeOffset resetsAt = SyncTime.AddDays(7);

        vm.Update(
            Bucket(QuotaBucket.Weekly, 10, 90, true, resetsAt),
            true,
            SyncTime,
            TimeSpan.FromDays(7).Subtract(TimeSpan.FromSeconds(5)));

        Assert.Equal("167:59:55", vm.CountdownText);
        Assert.Equal("已同步", vm.StatusText);
    }

    [Fact]
    public void UnavailableBucketShowsDashRemaining()
    {
        QuotaCardViewModel vm = Create(QuotaBucket.FiveHour);
        vm.Update(Bucket(QuotaBucket.FiveHour, 0, 0, false), true, SyncTime, null);

        Assert.Equal("--", vm.RemainingPercentText);
        Assert.Equal("不可用", vm.StatusText);
    }

    [Fact]
    public void LastSyncTimeTextFormatsLocalTime()
    {
        QuotaCardViewModel vm = Create(QuotaBucket.FiveHour);
        vm.Update(Bucket(QuotaBucket.FiveHour, 0, 100, true), true, SyncTime, TimeSpan.FromHours(5));

        Assert.Equal(SyncTime.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture), vm.LastSyncTimeText);
    }

    [Fact]
    public void NeverSuccessfullySynchronizedCardShowsNoSyncTime()
    {
        QuotaCardViewModel vm = Create(QuotaBucket.FiveHour);

        vm.Update(Bucket(QuotaBucket.FiveHour, 0, 0, false), false, null, null);

        Assert.Equal("--", vm.LastSyncTimeText);
    }

    private static QuotaCardViewModel Create(QuotaBucket bucket) => new(bucket, new SynchronousDispatcher());

    private static QuotaBucketSnapshot Bucket(
        QuotaBucket bucket,
        int used,
        int remaining,
        bool available,
        DateTimeOffset? resetsAt = null) => new(bucket, used, remaining, resetsAt, bucket == QuotaBucket.FiveHour ? 300L : 10080L, available);
}
