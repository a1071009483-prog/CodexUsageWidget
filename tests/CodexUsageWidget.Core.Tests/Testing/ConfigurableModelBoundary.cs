using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class ConfigurableModelBoundary : IModelBoundary
{
    private readonly List<StartCall> _startCalls = new();
    private readonly List<InterruptCall> _interruptCalls = new();
    private readonly List<DeleteCall> _deleteCalls = new();

    public IReadOnlyList<StartCall> StartCalls => _startCalls;

    public IReadOnlyList<InterruptCall> InterruptCalls => _interruptCalls;

    public IReadOnlyList<DeleteCall> DeleteCalls => _deleteCalls;

    public Func<ModelGenerationRequest, CancellationToken, ModelGenerationResult>? OnStart { get; set; }

    public Action<string, string>? OnInterrupt { get; set; }

    public Func<string, CancellationToken, Task>? OnDelete { get; set; }

    public Task<ModelGenerationResult> StartGenerationAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken)
    {
        _startCalls.Add(new StartCall(request, cancellationToken));
        ModelGenerationResult result = OnStart?.Invoke(request, cancellationToken)
            ?? new ModelGenerationResult(false, false, FailureCategory: "unconfigured");
        return Task.FromResult(result);
    }

    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken)
    {
        _interruptCalls.Add(new InterruptCall(threadId, turnId));
        OnInterrupt?.Invoke(threadId, turnId);
        return Task.CompletedTask;
    }

    public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        _deleteCalls.Add(new DeleteCall(threadId));
        if (OnDelete is not null)
        {
            return OnDelete(threadId, cancellationToken);
        }

        return Task.CompletedTask;
    }

    internal sealed record StartCall(ModelGenerationRequest Request, CancellationToken CancellationToken);

    internal sealed record InterruptCall(string ThreadId, string TurnId);

    internal sealed record DeleteCall(string ThreadId);
}
