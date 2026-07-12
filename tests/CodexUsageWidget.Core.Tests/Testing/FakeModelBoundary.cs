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
}
