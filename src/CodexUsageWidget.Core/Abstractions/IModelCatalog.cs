namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// A read-only lightweight model catalog used by the activation selector.
/// Implementations list available models without performing model consumption.
/// </summary>
public interface IModelCatalog
{
    /// <summary>
    /// Lists the models available for activation.
    /// </summary>
    Task<IReadOnlyList<ModelCandidate>> ListModelsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A lightweight, non-sensitive model candidate exposed by the catalog.
/// </summary>
/// <param name="Id">The descriptor identifier.</param>
/// <param name="Model">The canonical model name used for generation requests.</param>
/// <param name="DisplayName">A human-readable display name.</param>
/// <param name="IsDefault">Whether the server marks this candidate as the default model.</param>
/// <param name="SupportedReasoningEfforts">The reasoning effort identifiers supported by the model, ordered lowest-to-highest when known.</param>
public sealed record ModelCandidate(
    string Id,
    string Model,
    string DisplayName,
    bool IsDefault,
    IReadOnlyList<string> SupportedReasoningEfforts);
