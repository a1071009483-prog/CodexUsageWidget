using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class FakeActivationCoordinator : IActivationCoordinator
{
    public List<ActivationCall> Calls { get; } = new();

    public Func<ActivationCall, CancellationToken, Task<ActivationResult>>? OnTryActivate { get; set; }

    public Task<ActivationResult> TryActivateAsync(
        AccountIdentity identity,
        QuotaSnapshot snapshot,
        ActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        var call = new ActivationCall(identity, snapshot, request);
        Calls.Add(call);
        return OnTryActivate?.Invoke(call, cancellationToken)
            ?? Task.FromResult(ActivationResult.NotEligible("test"));
    }
}

internal sealed record ActivationCall(
    AccountIdentity Identity,
    QuotaSnapshot Snapshot,
    ActivationRequest Request);
