using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.Core.Activation;

/// <summary>
/// Framework-agnostic seam for the guarded five-hour activation state machine.
/// </summary>
public interface IActivationCoordinator
{
    /// <summary>
    /// Attempts to activate a fresh five-hour window with at-most-once semantics.
    /// </summary>
    Task<ActivationResult> TryActivateAsync(
        AccountIdentity identity,
        QuotaSnapshot snapshot,
        ActivationRequest request,
        CancellationToken cancellationToken = default);
}
