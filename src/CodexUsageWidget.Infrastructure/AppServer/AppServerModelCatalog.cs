using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

/// <summary>
/// Lists available models through the current <see cref="AppServerSupervisor"/>
/// generation. This is a read-only, non-generating catalog used by the activation
/// model selector.
/// </summary>
public sealed class AppServerModelCatalog : IModelCatalog
{
    private readonly AppServerSupervisor _supervisor;

    public AppServerModelCatalog(AppServerSupervisor supervisor)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ModelCandidate>> ListModelsAsync(CancellationToken cancellationToken)
    {
        AppServerGenerationSession? generation = _supervisor.CurrentGeneration;
        if (generation is null)
        {
            throw new InvalidOperationException("App Server session is not available.");
        }

        IReadOnlyList<ModelDescriptor> models = await generation.Session.Gateway
            .ListAllModelsAsync(includeHidden: false, cancellationToken)
            .ConfigureAwait(false);

        return models.Select(ToCandidate).ToList();
    }

    private static ModelCandidate ToCandidate(ModelDescriptor descriptor)
    {
        IReadOnlyList<string> efforts = descriptor.SupportedReasoningEfforts is null
            ? Array.Empty<string>()
            : descriptor.SupportedReasoningEfforts.Select(e => e.ReasoningEffort).ToList();

        return new ModelCandidate(
            descriptor.Id,
            descriptor.Model,
            descriptor.DisplayName,
            descriptor.IsDefault,
            efforts);
    }
}
