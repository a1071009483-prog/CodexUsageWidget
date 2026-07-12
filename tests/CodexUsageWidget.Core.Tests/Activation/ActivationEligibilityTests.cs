using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Quota;
using Xunit;

namespace CodexUsageWidget.Core.Tests.Activation;

public sealed class ActivationEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FreshExactZeroWithAutomationIsEligible()
    {
        QuotaSnapshot snapshot = Snapshot(0, fresh: true);

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: true, null, Now);

        Assert.True(result.IsEligible);
        Assert.Equal(0, result.PreActivationValues.UsedPercent);
    }

    [Fact]
    public void WeeklyStateDoesNotGateActivation()
    {
        QuotaSnapshot snapshot = Snapshot(
            0,
            fresh: true,
            weeklyUsed: 100,
            weeklyAvailable: false);

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: true, null, Now);

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void StaleDataIsNotEligible()
    {
        QuotaSnapshot snapshot = Snapshot(0, fresh: false);

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: true, null, Now);

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void NonZeroUsageIsNotEligible()
    {
        QuotaSnapshot snapshot = Snapshot(1, fresh: true);

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: true, null, Now);

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void AutomationDisabledIsNotEligible()
    {
        QuotaSnapshot snapshot = Snapshot(0, fresh: true);

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: false, null, Now);

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void ActiveSuppressionLockIsNotEligible()
    {
        QuotaSnapshot snapshot = Snapshot(0, fresh: true);
        ActivationAttempt activeLock = ActiveAttempt(Now.AddHours(1));

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: true, activeLock, Now);

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void ExpiredSuppressionLockIsEligible()
    {
        QuotaSnapshot snapshot = Snapshot(0, fresh: true);
        ActivationAttempt activeLock = ActiveAttempt(Now.AddHours(-1));

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: true, activeLock, Now);

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void VerifiedFutureResetIsNotEligible()
    {
        QuotaSnapshot snapshot = Snapshot(0, fresh: true, resetsAt: Now.AddHours(2));

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: true, null, Now);

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void UnavailableBucketIsNotEligible()
    {
        QuotaSnapshot snapshot = Snapshot(0, fresh: true, fiveHourAvailable: false);

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: true, null, Now);

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void MissingResetTimeIsEligible()
    {
        QuotaSnapshot snapshot = Snapshot(0, fresh: true, resetsAt: null);

        ActivationEligibilityResult result = ActivationEligibility.Evaluate(snapshot, automationEnabled: true, null, Now);

        Assert.True(result.IsEligible);
    }

    private static QuotaSnapshot Snapshot(
        int fiveHourUsed,
        bool fresh,
        bool fiveHourAvailable = true,
        DateTimeOffset? resetsAt = null,
        int weeklyUsed = 0,
        bool weeklyAvailable = true)
    {
        return new QuotaSnapshot(
            ScopeLabel: "test",
            new QuotaBucketSnapshot(
                QuotaBucket.FiveHour,
                fiveHourUsed,
                100 - fiveHourUsed,
                resetsAt,
                WindowDurationMinutes: 300,
                fiveHourAvailable),
            new QuotaBucketSnapshot(
                QuotaBucket.Weekly,
                weeklyUsed,
                100 - weeklyUsed,
                ResetsAt: null,
                WindowDurationMinutes: 10080,
                weeklyAvailable),
            SyncedAt: Now,
            IsFresh: fresh,
            MonitoringConnectionState.Connected,
            Countdown: null);
    }

    private static ActivationAttempt ActiveAttempt(DateTimeOffset suppressionDeadline) =>
        new(
            AttemptId: "attempt-1",
            NamespaceHash: "hash",
            WorkspaceScope: "global",
            WindowKey: "window",
            WindowKind: "local",
            SuppressionDeadline: suppressionDeadline.ToUniversalTime().ToString("O"),
            ObservedAt: Now.ToUniversalTime().ToString("O"),
            AttemptAt: Now.ToUniversalTime().ToString("O"),
            PreUsedPercent: 0,
            PreResetsAt: null,
            ModelId: null,
            TurnStarted: false,
            TerminalOutcome: null,
            PostUsedPercent: null,
            PostResetsAt: null,
            CleanupState: "none");
}
