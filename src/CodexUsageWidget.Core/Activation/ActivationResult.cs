namespace CodexUsageWidget.Core.Activation;

/// <summary>
/// Terminal outcome category returned by <see cref="ActivationCoordinator"/>.
/// </summary>
public enum ActivationOutcome
{
    Succeeded,
    NotEligible,
    Suppressed,
    ExternallySatisfied,
    NoModel,
    Failed,
    Ambiguous,
}

/// <summary>
/// The result of a single activation attempt.
/// </summary>
/// <param name="Outcome">The terminal outcome category.</param>
/// <param name="Reason">A safe, non-sensitive human-readable reason.</param>
/// <param name="ErrorCategory">A redacted error category when the outcome is a failure.</param>
/// <param name="AttemptId">The durable attempt identifier, when one was created.</param>
public sealed record ActivationResult(
    ActivationOutcome Outcome,
    string Reason,
    string? ErrorCategory = null,
    string? AttemptId = null)
{
    /// <summary>
    /// Whether the activation produced a verified reset.
    /// </summary>
    public bool IsSuccess => Outcome == ActivationOutcome.Succeeded;

    /// <summary>
    /// Builds a success result.
    /// </summary>
    public static ActivationResult Succeeded(string reason, string attemptId) =>
        new(ActivationOutcome.Succeeded, reason, AttemptId: attemptId);

    /// <summary>
    /// Builds a not-eligible result.
    /// </summary>
    public static ActivationResult NotEligible(string reason) =>
        new(ActivationOutcome.NotEligible, reason);

    /// <summary>
    /// Builds a suppressed result for an active durable lock.
    /// </summary>
    public static ActivationResult Suppressed(string? attemptId = null) =>
        new(ActivationOutcome.Suppressed, "Active suppression lock covers the current window.", AttemptId: attemptId);

    /// <summary>
    /// Builds a result indicating the quota condition was satisfied externally before generation.
    /// </summary>
    public static ActivationResult ExternallySatisfied(string attemptId) =>
        new(ActivationOutcome.ExternallySatisfied, "Quota no longer fresh-zero; condition satisfied externally.", AttemptId: attemptId);

    /// <summary>
    /// Builds a result indicating no acceptable model was available.
    /// </summary>
    public static ActivationResult NoModel(string attemptId) =>
        new(ActivationOutcome.NoModel, "No acceptable model available for activation.", AttemptId: attemptId);

    /// <summary>
    /// Builds a generic failure result.
    /// </summary>
    public static ActivationResult Failed(string reason, string? errorCategory = null, string? attemptId = null) =>
        new(ActivationOutcome.Failed, reason, errorCategory, attemptId);

    /// <summary>
    /// Builds a result for attempts that crossed the model boundary but could not be verified.
    /// </summary>
    public static ActivationResult Ambiguous(string attemptId) =>
        new(ActivationOutcome.Ambiguous, "Activation crossed boundary but verification did not confirm a reset.", AttemptId: attemptId);
}
