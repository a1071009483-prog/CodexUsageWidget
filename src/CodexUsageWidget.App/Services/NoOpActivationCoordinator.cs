using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.App.Services;

/// <summary>
/// No-op activation coordinator used for the design-time smoke mode.
/// It never performs any model consumption and reports that activation is not eligible.
/// </summary>
public sealed class NoOpActivationCoordinator : IActivationCoordinator
{
    public Task<ActivationResult> TryActivateAsync(
        AccountIdentity identity,
        QuotaSnapshot snapshot,
        ActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ActivationResult.NotEligible("design-mode"));
    }
}
