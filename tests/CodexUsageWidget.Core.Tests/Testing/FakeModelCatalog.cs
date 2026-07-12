using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class FakeModelCatalog : IModelCatalog
{
    public IReadOnlyList<ModelCandidate> Models { get; set; } = Array.Empty<ModelCandidate>();

    public Exception? ExceptionToThrow { get; set; }

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<ModelCandidate>> ListModelsAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(Models);
    }
}
