namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Defines the single auditable call through which model generation may begin.
/// </summary>
public interface IModelBoundary
{
    Task<ModelGenerationResult> StartGenerationAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken);
}

public sealed record ModelGenerationRequest(
    string AttemptId,
    string ModelId,
    string Prompt,
    string WorkingDirectory,
    TimeSpan Timeout);

public sealed record ModelGenerationResult(
    bool WasAccepted,
    bool GenerationStarted,
    string? ThreadId = null,
    string? TurnId = null,
    string? FailureCategory = null);
