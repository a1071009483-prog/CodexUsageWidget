using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class FakeActivationCoordinator : IActivationCoordinator
{
    public List<ActivationCall> Calls { get; } = new();

    public Task<ActivationResult> TryActivateAsync(
        AccountIdentity identity,
        QuotaSnapshot snapshot,
        ActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new ActivationCall(identity, snapshot, request));
        return Task.FromResult(ActivationResult.NotEligible("test"));
    }
}

internal sealed record ActivationCall(
    AccountIdentity Identity,
    QuotaSnapshot Snapshot,
    ActivationRequest Request);
