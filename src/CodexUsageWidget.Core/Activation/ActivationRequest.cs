namespace CodexUsageWidget.Core.Activation;

/// <summary>
/// Parameters supplied by the caller for a single activation attempt.
/// </summary>
/// <param name="IsAutomationEnabled">Whether automatic activation is currently enabled by the user.</param>
/// <param name="IsUserInitiated">Whether the user authorized this one guarded evaluation.</param>
public sealed record ActivationRequest(
    bool IsAutomationEnabled,
    bool IsUserInitiated = false);
