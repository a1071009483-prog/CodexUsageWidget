using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class FakeModelCatalog : IModelCatalog
{
    public IReadOnlyList<ModelCandidate> Models { get; set; } = Array.Empty<ModelCandidate>();

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<ModelCandidate>> ListModelsAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(Models);
    }
}
