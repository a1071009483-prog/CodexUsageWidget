namespace CodexUsageWidget.Core.Activation;

/// <summary>
/// Tunable settings for <see cref="ActivationCoordinator"/>.
/// </summary>
public sealed record ActivationCoordinatorOptions
{
    /// <summary>
    /// Whether automatic activation is enabled. Defaults to false.
    /// </summary>
    public bool IsAutomationEnabled { get; init; }

    /// <summary>
    /// Debounce between the two consecutive quota confirmations. Defaults to zero.
    /// </summary>
    public TimeSpan ConfirmationDebounce { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Maximum time to wait for a started turn to complete before interrupting. Defaults to 10 seconds.
    /// </summary>
    public TimeSpan TurnTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum time to spend polling for a verified reset after the turn. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan VerificationTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Interval between post-activation quota verification polls. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan VerificationPollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Working directory passed to the model boundary. Defaults to empty.
    /// </summary>
    public string WorkingDirectory { get; init; } = string.Empty;
}
