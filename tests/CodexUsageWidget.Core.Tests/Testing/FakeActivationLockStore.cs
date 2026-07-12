using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class FakeActivationLockStore : IActivationLockStore
{
    private readonly Dictionary<string, ActivationAttempt> _byKey = new();
    private readonly Dictionary<string, ActivationAttempt> _byAttempt = new();

    public Exception? ExceptionToThrow { get; set; }

    public IReadOnlyDictionary<string, ActivationAttempt> Attempts => _byAttempt;

    private static string Key(string ns, string scope, string window) => $"{ns}|{scope}|{window}";

    public Task<AcquisitionResult> TryAcquireAsync(
        ActivationAttempt attempt,
        CancellationToken cancellationToken)
    {
        ThrowIfRequested();
        string key = Key(attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey);
        if (_byKey.TryGetValue(key, out ActivationAttempt? existing))
        {
            return Task.FromResult(new AcquisitionResult(false, existing));
        }

        _byKey[key] = attempt;
        _byAttempt[attempt.AttemptId] = attempt;
        return Task.FromResult(new AcquisitionResult(true, null));
    }

    public Task<ActivationAttempt?> GetActiveAsync(
        string namespaceHash,
        string workspaceScope,
        string windowKey,
        CancellationToken cancellationToken)
    {
        ThrowIfRequested();
        _byKey.TryGetValue(Key(namespaceHash, workspaceScope, windowKey), out ActivationAttempt? attempt);
        return Task.FromResult(attempt);
    }

    public Task MarkTurnStartedAsync(string attemptId, CancellationToken cancellationToken)
    {
        ThrowIfRequested();
        if (_byAttempt.TryGetValue(attemptId, out ActivationAttempt? attempt))
        {
            _byAttempt[attemptId] = attempt with { TurnStarted = true };
            _byKey[Key(attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey)] = _byAttempt[attemptId];
        }

        return Task.CompletedTask;
    }

    public Task MarkTerminalAsync(
        string attemptId,
        string terminalOutcome,
        int? postUsedPercent,
        string? postResetsAt,
        string cleanupState,
        CancellationToken cancellationToken)
    {
        ThrowIfRequested();
        if (_byAttempt.TryGetValue(attemptId, out ActivationAttempt? attempt))
        {
            ActivationAttempt updated = attempt with
            {
                TerminalOutcome = terminalOutcome,
                PostUsedPercent = postUsedPercent,
                PostResetsAt = postResetsAt,
                CleanupState = cleanupState,
            };
            _byAttempt[attemptId] = updated;
            _byKey[Key(updated.NamespaceHash, updated.WorkspaceScope, updated.WindowKey)] = updated;
        }

        return Task.CompletedTask;
    }

    public Task ExtendSuppressionDeadlineAsync(
        string attemptId,
        string newSuppressionDeadline,
        CancellationToken cancellationToken)
    {
        ThrowIfRequested();
        if (_byAttempt.TryGetValue(attemptId, out ActivationAttempt? attempt))
        {
            ActivationAttempt updated = attempt with { SuppressionDeadline = newSuppressionDeadline };
            _byAttempt[attemptId] = updated;
            _byKey[Key(updated.NamespaceHash, updated.WorkspaceScope, updated.WindowKey)] = updated;
        }

        return Task.CompletedTask;
    }

    private void ThrowIfRequested()
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }
}
