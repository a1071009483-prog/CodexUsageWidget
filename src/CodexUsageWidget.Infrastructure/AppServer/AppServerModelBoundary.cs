using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

/// <summary>
/// The single auditable model boundary for the activation coordinator. All generation
/// traffic to the Codex app server flows through this class so that the Core layer can
/// enforce at-most-once semantics without directly depending on JSON-RPC details.
/// </summary>
public sealed class AppServerModelBoundary : IModelBoundary
{
    private const string ApprovalPolicyNever = "never";
    private const string SandboxReadOnly = "read-only";
    private const string LowestReasoningEffort = "low";

    private readonly CodexAppServerGateway _gateway;

    public AppServerModelBoundary(CodexAppServerGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    /// <inheritdoc />
    public async Task<ModelGenerationResult> StartGenerationAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        CancellationToken linkedToken = linkedSource.Token;

        try
        {
            ThreadStartResponse thread = await _gateway.StartThreadAsync(
                new ThreadStartOptions
                {
                    Model = request.ModelId,
                    WorkingDirectory = request.WorkingDirectory,
                    Ephemeral = true,
                    ApprovalPolicy = ApprovalPolicyNever,
                    Sandbox = SandboxReadOnly,
                    AllowProviderModelFallback = false,
                    DynamicTools = Array.Empty<object>(),
                    Environments = Array.Empty<object>(),
                },
                linkedToken).ConfigureAwait(false);

            TurnStartResponse turn = await _gateway.StartTurnAsync(
                new TurnStartOptions
                {
                    ThreadId = thread.Thread.Id,
                    Input = new[]
                    {
                        new TextUserInput("text", request.Prompt),
                    },
                    ApprovalPolicy = ApprovalPolicyNever,
                    Effort = LowestReasoningEffort,
                    Summary = "none",
                },
                linkedToken).ConfigureAwait(false);

            return new ModelGenerationResult(
                WasAccepted: true,
                GenerationStarted: true,
                ThreadId: thread.Thread.Id,
                TurnId: turn.Turn.Id);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            return new ModelGenerationResult(
                WasAccepted: false,
                GenerationStarted: false,
                FailureCategory: "turn-timeout");
        }
        catch (AppServerProtocolException exception)
        {
            return new ModelGenerationResult(
                WasAccepted: false,
                GenerationStarted: false,
                FailureCategory: MapProtocolFailure(exception));
        }
        catch (Exception exception)
        {
            return new ModelGenerationResult(
                WasAccepted: false,
                GenerationStarted: false,
                FailureCategory: RedactErrorCategory(exception));
        }
    }

    /// <inheritdoc />
    public Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken) =>
        _gateway.InterruptTurnAsync(threadId, turnId, cancellationToken);

    /// <inheritdoc />
    public Task DeleteThreadAsync(
        string threadId,
        CancellationToken cancellationToken) =>
        _gateway.DeleteThreadAsync(threadId, cancellationToken);

    private static string MapProtocolFailure(AppServerProtocolException exception)
    {
        // A missing method is the cleanest signal that the selected model is not
        // reachable through this app-server surface. Remote errors are kept as a
        // generic protocol failure so the coordinator does not silently fall back
        // for unrelated server-side faults.
        return exception.Kind == AppServerProtocolErrorKind.MethodNotFound
            ? "model-unavailable"
            : $"protocol-{exception.Kind}";
    }

    private static string RedactErrorCategory(Exception exception) =>
        exception switch
        {
            OperationCanceledException => "cancelled",
            _ => exception.GetType().Name,
        };
}
