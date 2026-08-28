using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.Core.Activation;

/// <summary>
/// Pure eligibility logic for automatic five-hour activation.
/// </summary>
public static class ActivationEligibility
{
    /// <summary>
    /// Evaluates whether the current snapshot and durable state permit activation.
    /// </summary>
    public static ActivationEligibilityResult Evaluate(
        QuotaSnapshot snapshot,
        bool automationEnabled,
        ActivationAttempt? activeAttempt,
        DateTimeOffset now,
        bool deferFutureResetVerification = false)
    {
        QuotaTriggerInput input = snapshot.FiveHourTriggerInput;

        if (!automationEnabled)
        {
            return Ineligible(input, "automation-disabled");
        }

        if (!input.IsFresh)
        {
            return Ineligible(input, "stale");
        }

        if (!input.IsAvailable)
        {
            return Ineligible(input, "bucket-unavailable");
        }

        if (input.UsedPercent != 0)
        {
            return Ineligible(input, "usage-nonzero");
        }

        if (!deferFutureResetVerification
            && input.ResetsAt is { } resetsAt
            && resetsAt > now)
        {
            return Ineligible(input, "verified-future-reset");
        }

        if (activeAttempt is not null
            && TryParseDeadline(activeAttempt.SuppressionDeadline) is { } deadline
            && deadline > now)
        {
            return Ineligible(input, "suppression-active");
        }

        return new ActivationEligibilityResult(true, "eligible", input);
    }

    private static ActivationEligibilityResult Ineligible(QuotaTriggerInput input, string reason) =>
        new(false, reason, input);

    private static DateTimeOffset? TryParseDeadline(string value)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset deadline))
        {
            return deadline;
        }

        return null;
    }
}
