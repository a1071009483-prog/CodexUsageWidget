using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class FakeModelBoundary : IModelBoundary
{
    public int CallCount { get; private set; }

    public Task<ModelGenerationResult> StartGenerationAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new ModelGenerationResult(false, false, FailureCategory: "Unexpected call"));
    }

    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
