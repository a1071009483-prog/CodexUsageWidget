using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class FakeActivationLockStore : IActivationLockStore
{
    private readonly Dictionary<string, ActivationAttempt> _byKey = new();
    private readonly Dictionary<string, ActivationAttempt> _byAttempt = new();
    private readonly object _sync = new();

    public Exception? ExceptionToThrow { get; set; }
    public Exception? ExceptionOnGetActive { get; set; }
    public Exception? ExceptionOnTryAcquire { get; set; }
    public Exception? ExceptionOnMarkTurnStarted { get; set; }
    public Exception? ExceptionOnMarkTerminal { get; set; }
    public Exception? ExceptionOnExtendSuppressionDeadline { get; set; }

    public IReadOnlyDictionary<string, ActivationAttempt> Attempts
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<string, ActivationAttempt>(_byAttempt);
            }
        }
    }

    private static string Key(string ns, string scope, string window) => $"{ns}|{scope}|{window}";

    public Task<AcquisitionResult> TryAcquireAsync(
        ActivationAttempt attempt,
        CancellationToken cancellationToken)
    {
        ThrowIfRequested(ExceptionOnTryAcquire);
        string key = Key(attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey);
        lock (_sync)
        {
            if (string.Equals(attempt.WindowKind, "local", StringComparison.Ordinal)
                && DateTimeOffset.TryParse(attempt.AttemptAt, out DateTimeOffset attemptAt))
            {
                ActivationAttempt? activeLocal = _byAttempt.Values.FirstOrDefault(existing =>
                    string.Equals(existing.NamespaceHash, attempt.NamespaceHash, StringComparison.Ordinal)
                    && string.Equals(existing.WorkspaceScope, attempt.WorkspaceScope, StringComparison.Ordinal)
                    && string.Equals(existing.WindowKind, "local", StringComparison.Ordinal)
                    && DateTimeOffset.TryParse(existing.SuppressionDeadline, out DateTimeOffset deadline)
                    && deadline > attemptAt);

                if (activeLocal is not null)
                {
                    return Task.FromResult(new AcquisitionResult(false, activeLocal));
                }
            }

            if (_byKey.TryGetValue(key, out ActivationAttempt? existing))
            {
                return Task.FromResult(new AcquisitionResult(false, existing));
            }

            _byKey[key] = attempt;
            _byAttempt[attempt.AttemptId] = attempt;
            return Task.FromResult(new AcquisitionResult(true, null));
        }
    }

    public Task<ActivationAttempt?> GetActiveAsync(
        string namespaceHash,
        string workspaceScope,
        string windowKey,
        CancellationToken cancellationToken)
    {
        ThrowIfRequested(ExceptionOnGetActive);
        lock (_sync)
        {
            _byKey.TryGetValue(Key(namespaceHash, workspaceScope, windowKey), out ActivationAttempt? attempt);
            return Task.FromResult(attempt);
        }
    }

    public Task MarkTurnStartedAsync(string attemptId, CancellationToken cancellationToken)
    {
        ThrowIfRequested(ExceptionOnMarkTurnStarted);
        lock (_sync)
        {
            if (_byAttempt.TryGetValue(attemptId, out ActivationAttempt? attempt))
            {
                ActivationAttempt updated = attempt with { TurnStarted = true };
                _byAttempt[attemptId] = updated;
                _byKey[Key(attempt.NamespaceHash, attempt.WorkspaceScope, attempt.WindowKey)] = updated;
            }
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
        ThrowIfRequested(ExceptionOnMarkTerminal);
        lock (_sync)
        {
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
        }

        return Task.CompletedTask;
    }

    public Task ExtendSuppressionDeadlineAsync(
        string attemptId,
        string newSuppressionDeadline,
        CancellationToken cancellationToken)
    {
        ThrowIfRequested(ExceptionOnExtendSuppressionDeadline);
        lock (_sync)
        {
            if (_byAttempt.TryGetValue(attemptId, out ActivationAttempt? attempt))
            {
                ActivationAttempt updated = attempt with { SuppressionDeadline = newSuppressionDeadline };
                _byAttempt[attemptId] = updated;
                _byKey[Key(updated.NamespaceHash, updated.WorkspaceScope, updated.WindowKey)] = updated;
            }
        }

        return Task.CompletedTask;
    }

    private void ThrowIfRequested(Exception? specific)
    {
        Exception? toThrow = specific ?? ExceptionToThrow;
        if (toThrow is not null)
        {
            throw toThrow;
        }
    }
}
