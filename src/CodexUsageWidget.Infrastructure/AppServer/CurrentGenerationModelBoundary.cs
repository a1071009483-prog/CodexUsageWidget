using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.AppServer;

/// <summary>
/// A generation-aware <see cref="IModelBoundary"/> that delegates every activation call
/// to the current <see cref="AppServerSupervisor"/> generation. This lets the
/// <see cref="ActivationCoordinator"/> keep a stable boundary reference while the App
/// Server connection is restarted underneath it.
/// </summary>
public sealed class CurrentGenerationModelBoundary : IModelBoundary
{
    private readonly AppServerSupervisor _supervisor;

    public CurrentGenerationModelBoundary(AppServerSupervisor supervisor)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    }

    /// <inheritdoc />
    public async Task<ModelGenerationResult> StartGenerationAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken)
    {
        AppServerModelBoundary boundary = ResolveBoundary();
        return await boundary.StartGenerationAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken)
    {
        AppServerModelBoundary boundary = ResolveBoundary();
        await boundary.InterruptTurnAsync(threadId, turnId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteThreadAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        AppServerModelBoundary boundary = ResolveBoundary();
        await boundary.DeleteThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    private AppServerModelBoundary ResolveBoundary()
    {
        AppServerGenerationSession? generation = _supervisor.CurrentGeneration;
        if (generation is null)
        {
            throw new InvalidOperationException("App Server session is not available.");
        }

        return new AppServerModelBoundary(generation.Session.Gateway);
    }
}
