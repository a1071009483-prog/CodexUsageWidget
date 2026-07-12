using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.Core.Activation;

/// <summary>
/// The result of evaluating whether the current quota snapshot permits activation.
/// </summary>
/// <param name="IsEligible">Whether activation is permitted.</param>
/// <param name="Reason">A non-sensitive reason string.</param>
/// <param name="PreActivationValues">The pre-activation quota values used for the decision.</param>
public sealed record ActivationEligibilityResult(
    bool IsEligible,
    string Reason,
    QuotaTriggerInput PreActivationValues);
