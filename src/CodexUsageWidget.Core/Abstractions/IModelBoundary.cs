namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Defines the single auditable boundary through which model generation may begin,
/// and the lifecycle helpers required to interrupt and clean up a temporary turn.
/// </summary>
public interface IModelBoundary
{
    /// <summary>
    /// Starts a temporary thread and a single lightweight generation turn.
    /// </summary>
    Task<ModelGenerationResult> StartGenerationAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Interrupts a turn that has exceeded its completion window.
    /// </summary>
    Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the temporary thread created for activation.
    /// </summary>
    Task DeleteThreadAsync(
        string threadId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Request to start a single lightweight generation turn.
/// </summary>
/// <param name="AttemptId">The durable activation attempt identifier.</param>
/// <param name="ModelId">The canonical model identifier selected for the turn.</param>
/// <param name="Prompt">The prompt text. Implementations must treat this as sensitive and never persist it.</param>
/// <param name="WorkingDirectory">The working directory for the temporary thread.</param>
/// <param name="Timeout">The maximum time the caller will wait for the turn to complete.</param>
public sealed record ModelGenerationRequest(
    string AttemptId,
    string ModelId,
    string Prompt,
    string WorkingDirectory,
    TimeSpan Timeout);

/// <summary>
/// Result of a single <see cref="IModelBoundary.StartGenerationAsync"/> call.
/// </summary>
/// <param name="WasAccepted">Whether the model accepted the generation request.</param>
/// <param name="GenerationStarted">Whether a turn was actually started.</param>
/// <param name="ThreadId">The temporary thread identifier, when known.</param>
/// <param name="TurnId">The turn identifier, when known.</param>
/// <param name="FailureCategory">A redacted failure category when the request was not accepted.</param>
public sealed record ModelGenerationResult(
    bool WasAccepted,
    bool GenerationStarted,
    string? ThreadId = null,
    string? TurnId = null,
    string? FailureCategory = null);
