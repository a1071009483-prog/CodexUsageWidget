using System.Globalization;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.Core.Activation;

/// <summary>
/// Guarded five-hour activation state machine. Ensures at most one accepted generation
/// per scoped window through durable write-ahead locking, two consecutive confirmations,
/// a final read-only preflight, curated model selection, and read-only verification.
/// </summary>
public sealed class ActivationCoordinator
{
    private const string PromptText = "Respond with exactly the word 'OK' and do not use any tools.";

    private const string OutcomeSucceeded = "succeeded";
    private const string OutcomeFailed = "failed";
    private const string OutcomeExternallySatisfied = "externally-satisfied";
    private const string OutcomeNoModel = "no-model";
    private const string OutcomeAmbiguous = "ambiguous";

    private const string FailureCategoryModelUnavailable = "model-unavailable";

    private static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);

    private readonly IActivationLockStore _lockStore;
    private readonly IModelCatalog _modelCatalog;
    private readonly IModelBoundary _modelBoundary;
    private readonly IQuotaSource _quotaSource;
    private readonly IAuditStore _auditStore;
    private readonly ICleanupWorkStore _cleanupStore;
    private readonly IAccountNamespaceHasher _namespaceHasher;
    private readonly IUserNotifier _notifier;
    private readonly IClock _clock;
    private readonly IDelay _delay;
    private readonly ActivationCoordinatorOptions _options;

    public ActivationCoordinator(
        IActivationLockStore lockStore,
        IModelCatalog modelCatalog,
        IModelBoundary modelBoundary,
        IQuotaSource quotaSource,
        IAuditStore auditStore,
        ICleanupWorkStore cleanupStore,
        IAccountNamespaceHasher namespaceHasher,
        IUserNotifier notifier,
        IClock clock,
        IDelay delay,
        ActivationCoordinatorOptions options)
    {
        _lockStore = lockStore ?? throw new ArgumentNullException(nameof(lockStore));
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _modelBoundary = modelBoundary ?? throw new ArgumentNullException(nameof(modelBoundary));
        _quotaSource = quotaSource ?? throw new ArgumentNullException(nameof(quotaSource));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _cleanupStore = cleanupStore ?? throw new ArgumentNullException(nameof(cleanupStore));
        _namespaceHasher = namespaceHasher ?? throw new ArgumentNullException(nameof(namespaceHasher));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Attempts to activate a fresh five-hour window with at-most-once semantics.
    /// </summary>
    public async Task<ActivationResult> TryActivateAsync(
        AccountIdentity identity,
        QuotaSnapshot snapshot,
        ActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine("[Coordinator] TryActivateAsync entered");

        if (!_options.IsAutomationEnabled || !request.IsAutomationEnabled)
        {
            return ActivationResult.NotEligible("automation-disabled");
        }

        string? attemptId = null;

        try
        {
            // First confirmation: the snapshot supplied by the caller.
            ActivationEligibilityResult eligibility = ActivationEligibility.Evaluate(
                snapshot,
                automationEnabled: true,
                activeAttempt: null,
                _clock.UtcNow);

            if (!eligibility.IsEligible)
            {
                return ActivationResult.NotEligible(eligibility.Reason);
            }

            string namespaceHash = await _namespaceHasher.GetNamespaceHashAsync(
                identity,
                cancellationToken).ConfigureAwait(false);

            // Second confirmation after debounce.
            if (_options.ConfirmationDebounce > TimeSpan.Zero)
            {
                Console.WriteLine($"[Coordinator] awaiting debounce delay {_options.ConfirmationDebounce}");
                await _delay.DelayAsync(_options.ConfirmationDebounce, cancellationToken).ConfigureAwait(false);
                Console.WriteLine("[Coordinator] debounce delay completed");
            }

            QuotaSnapshot? confirmed = await FetchSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (confirmed is null)
            {
                return ActivationResult.NotEligible("refetch-failed");
            }

            eligibility = ActivationEligibility.Evaluate(
                confirmed,
                automationEnabled: true,
                activeAttempt: null,
                _clock.UtcNow);

            if (!eligibility.IsEligible)
            {
                return ActivationResult.NotEligible($"refetch-{eligibility.Reason}");
            }

            // Durable guard: an active lock for the same window suppresses this attempt.
            string workspaceScope = NormalizeWorkspaceScope(identity.WorkspaceScope);
            string windowKey = ComputeWindowKey(snapshot.SyncedAt, confirmed.FiveHour.ResetsAt, out string windowKind);

            ActivationAttempt? activeAttempt = await _lockStore.GetActiveAsync(
                namespaceHash,
                workspaceScope,
                windowKey,
                cancellationToken).ConfigureAwait(false);

            if (activeAttempt is not null
                && TryParseDeadline(activeAttempt.SuppressionDeadline) is { } activeDeadline
                && activeDeadline > _clock.UtcNow)
            {
                return ActivationResult.Suppressed(activeAttempt.AttemptId);
            }

            // Write-ahead lock acquisition.
            DateTimeOffset now = _clock.UtcNow;
            attemptId = Guid.NewGuid().ToString();
            string suppressionDeadline = FormatIso(now + FiveHours);

            var attempt = new ActivationAttempt(
                attemptId,
                namespaceHash,
                workspaceScope,
                windowKey,
                windowKind,
                suppressionDeadline,
                FormatIso(confirmed.SyncedAt),
                FormatIso(now),
                confirmed.FiveHour.UsedPercent,
                confirmed.FiveHour.ResetsAt is { } preReset ? FormatIso(preReset) : null,
                null,
                false,
                null,
                null,
                null,
                "none");

            AcquisitionResult acquisition;
            try
            {
                acquisition = await _lockStore.TryAcquireAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                string caughtErrorCategory = RedactErrorCategory(exception);
                await WriteAuditAsync(
                    attemptId,
                    namespaceHash,
                    modelId: null,
                    preSnapshot: confirmed,
                    postSnapshot: null,
                    turnCrossedBoundary: false,
                    OutcomeFailed,
                    caughtErrorCategory,
                    cancellationToken).ConfigureAwait(false);
                await NotifyOnceAsync(OutcomeFailed, attemptId, cancellationToken).ConfigureAwait(false);
                return ActivationResult.Failed("lock-acquisition-failure", caughtErrorCategory, attemptId);
            }

            if (!acquisition.Acquired)
            {
                return ActivationResult.Suppressed(acquisition.Existing?.AttemptId);
            }

            // Model selection happens before the final preflight so that an empty catalog
            // fails fast without consuming a quota read.
            IReadOnlyList<ModelCandidate> catalog = await _modelCatalog.ListModelsAsync(cancellationToken)
                .ConfigureAwait(false);

            ModelSelectionResult? selection = LightweightModelSelector.Select(catalog);
            if (selection is null)
            {
                await MarkTerminalAsync(
                    attemptId,
                    OutcomeNoModel,
                    confirmed,
                    "none",
                    cancellationToken).ConfigureAwait(false);
                await WriteAuditAsync(
                    attemptId,
                    namespaceHash,
                    modelId: null,
                    preSnapshot: confirmed,
                    postSnapshot: null,
                    turnCrossedBoundary: false,
                    OutcomeNoModel,
                    errorCategory: null,
                    cancellationToken).ConfigureAwait(false);
                await NotifyOnceAsync(OutcomeNoModel, attemptId, cancellationToken).ConfigureAwait(false);
                return ActivationResult.NoModel(attemptId);
            }

            // Final preflight immediately before any model consumption.
            QuotaSnapshot? finalPreflight = await FetchSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (finalPreflight is null
                || !ActivationEligibility.Evaluate(
                    finalPreflight,
                    automationEnabled: true,
                    activeAttempt: null,
                    _clock.UtcNow).IsEligible)
            {
                QuotaSnapshot postSnapshot = finalPreflight ?? confirmed;
                await MarkTerminalAsync(
                    attemptId,
                    OutcomeExternallySatisfied,
                    postSnapshot,
                    "none",
                    cancellationToken).ConfigureAwait(false);
                await WriteAuditAsync(
                    attemptId,
                    namespaceHash,
                    modelId: null,
                    preSnapshot: confirmed,
                    postSnapshot: finalPreflight,
                    turnCrossedBoundary: false,
                    OutcomeExternallySatisfied,
                    errorCategory: null,
                    cancellationToken).ConfigureAwait(false);
                await NotifyOnceAsync(OutcomeExternallySatisfied, attemptId, cancellationToken).ConfigureAwait(false);
                return ActivationResult.ExternallySatisfied(attemptId);
            }

            // Attempt generation with optional fallback for explicit pre-generation unavailability.
            HashSet<string> attemptedModels = new(StringComparer.OrdinalIgnoreCase);
            ModelGenerationResult? generationResult = null;

            while (selection is not null)
            {
                if (!attemptedModels.Add(selection.Selected.Model))
                {
                    break;
                }

                var generationRequest = new ModelGenerationRequest(
                    attemptId,
                    selection.Selected.Model,
                    PromptText,
                    _options.WorkingDirectory,
                    _options.TurnTimeout);

                try
                {
                    generationResult = await _modelBoundary.StartGenerationAsync(
                        generationRequest,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    string caughtErrorCategory = RedactErrorCategory(exception);
                    await MarkTerminalAsync(
                        attemptId,
                        OutcomeFailed,
                        finalPreflight,
                        "none",
                        cancellationToken,
                        errorCategory: caughtErrorCategory).ConfigureAwait(false);
                    await WriteAuditAsync(
                        attemptId,
                        namespaceHash,
                        selection.Selected.Model,
                        preSnapshot: confirmed,
                        postSnapshot: null,
                        turnCrossedBoundary: false,
                        OutcomeFailed,
                        caughtErrorCategory,
                        cancellationToken).ConfigureAwait(false);
                    await NotifyOnceAsync(OutcomeFailed, attemptId, cancellationToken).ConfigureAwait(false);
                    return ActivationResult.Failed("generation-boundary-failure", caughtErrorCategory, attemptId);
                }

                if (generationResult.GenerationStarted)
                {
                    break;
                }

                if (!string.Equals(
                        generationResult.FailureCategory,
                        FailureCategoryModelUnavailable,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await MarkTerminalAsync(
                        attemptId,
                        OutcomeFailed,
                        finalPreflight,
                        "none",
                        cancellationToken,
                        errorCategory: generationResult.FailureCategory).ConfigureAwait(false);
                    await WriteAuditAsync(
                        attemptId,
                        namespaceHash,
                        selection.Selected.Model,
                        preSnapshot: confirmed,
                        postSnapshot: null,
                        turnCrossedBoundary: false,
                        OutcomeFailed,
                        generationResult.FailureCategory,
                        cancellationToken).ConfigureAwait(false);
                    await NotifyOnceAsync(OutcomeFailed, attemptId, cancellationToken).ConfigureAwait(false);
                    return ActivationResult.Failed("model-rejected", generationResult.FailureCategory, attemptId);
                }

                catalog = await _modelCatalog.ListModelsAsync(cancellationToken).ConfigureAwait(false);
                IReadOnlyList<ModelCandidate> remaining = catalog
                    .Where(c => !attemptedModels.Contains(c.Model))
                    .ToList();

                selection = LightweightModelSelector.Select(remaining);
            }

            if (selection is null || generationResult is null)
            {
                await MarkTerminalAsync(
                    attemptId,
                    OutcomeNoModel,
                    finalPreflight,
                    "none",
                    cancellationToken).ConfigureAwait(false);
                await WriteAuditAsync(
                    attemptId,
                    namespaceHash,
                    modelId: null,
                    preSnapshot: confirmed,
                    postSnapshot: null,
                    turnCrossedBoundary: false,
                    OutcomeNoModel,
                    errorCategory: null,
                    cancellationToken).ConfigureAwait(false);
                await NotifyOnceAsync(OutcomeNoModel, attemptId, cancellationToken).ConfigureAwait(false);
                return ActivationResult.NoModel(attemptId);
            }

            // The generation boundary has been crossed; retries are no longer allowed.
            try
            {
                await _lockStore.MarkTurnStartedAsync(attemptId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                string caughtErrorCategory = RedactErrorCategory(exception);
                await MarkTerminalAsync(
                    attemptId,
                    OutcomeFailed,
                    finalPreflight,
                    "none",
                    cancellationToken,
                    errorCategory: caughtErrorCategory).ConfigureAwait(false);
                await WriteAuditAsync(
                    attemptId,
                    namespaceHash,
                    selection.Selected.Model,
                    preSnapshot: confirmed,
                    postSnapshot: null,
                    turnCrossedBoundary: true,
                    OutcomeFailed,
                    caughtErrorCategory,
                    cancellationToken).ConfigureAwait(false);
                await NotifyOnceAsync(OutcomeFailed, attemptId, cancellationToken).ConfigureAwait(false);
                return ActivationResult.Failed("turn-started-mark-failure", caughtErrorCategory, attemptId);
            }

            // Wait for the turn to settle, then interrupt once on timeout.
            bool interrupted = false;
            try
            {
                await _delay.DelayAsync(_options.TurnTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // An external timeout fired; still proceed to interrupt/verify.
            }

            if (!string.IsNullOrEmpty(generationResult.ThreadId)
                && !string.IsNullOrEmpty(generationResult.TurnId))
            {
                try
                {
                    await _modelBoundary.InterruptTurnAsync(
                        generationResult.ThreadId,
                        generationResult.TurnId,
                        cancellationToken).ConfigureAwait(false);
                    interrupted = true;
                }
                catch
                {
                    // Interrupt is best-effort; verification decides the outcome.
                }
            }

            // Read-only verification: look for a changed future five-hour reset.
            QuotaSnapshot? verifiedSnapshot = null;
            DateTimeOffset verificationDeadline = _clock.UtcNow + _options.VerificationTimeout;

            while (_clock.UtcNow < verificationDeadline)
            {
                QuotaSnapshot? post = await FetchSnapshotAsync(cancellationToken).ConfigureAwait(false);
                if (post is not null
                    && IsVerifiedReset(confirmed.FiveHour.ResetsAt, post.FiveHour.ResetsAt, _clock.UtcNow))
                {
                    verifiedSnapshot = post;
                    break;
                }

                TimeSpan remaining = verificationDeadline - _clock.UtcNow;
                TimeSpan pollDelay = remaining < _options.VerificationPollInterval
                    ? remaining
                    : _options.VerificationPollInterval;

                if (pollDelay <= TimeSpan.Zero)
                {
                    break;
                }

                try
                {
                    await _delay.DelayAsync(pollDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Continue to the final verification check.
                }
            }

            string outcome = verifiedSnapshot is not null ? OutcomeSucceeded : OutcomeAmbiguous;
            string? errorCategory = verifiedSnapshot is not null
                ? null
                : interrupted ? "interrupted" : "verification-timeout";
            string cleanupState = "none";

            if (verifiedSnapshot is not null
                && !string.IsNullOrEmpty(generationResult.ThreadId))
            {
                try
                {
                    await _modelBoundary.DeleteThreadAsync(generationResult.ThreadId, cancellationToken)
                        .ConfigureAwait(false);
                    cleanupState = "completed";
                }
                catch
                {
                    try
                    {
                        await _cleanupStore.EnqueueAsync(attemptId, generationResult.ThreadId, cancellationToken)
                            .ConfigureAwait(false);
                        cleanupState = "deferred";
                    }
                    catch
                    {
                        cleanupState = "deferred-failed";
                    }
                }

                // Extend suppression to cover the verified reset.
                if (verifiedSnapshot.FiveHour.ResetsAt is { } verifiedReset
                    && verifiedReset > now
                    && TryParseDeadline(attempt.SuppressionDeadline) is { } currentDeadline)
                {
                    DateTimeOffset extendedDeadline = verifiedReset + FiveHours;
                    if (extendedDeadline > currentDeadline)
                    {
                        try
                        {
                            await _lockStore.ExtendSuppressionDeadlineAsync(
                                attemptId,
                                FormatIso(extendedDeadline),
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            // The guard is already established; extension is best-effort.
                        }
                    }
                }
            }

            await MarkTerminalAsync(
                attemptId,
                outcome,
                verifiedSnapshot ?? finalPreflight,
                cleanupState,
                cancellationToken,
                errorCategory: errorCategory).ConfigureAwait(false);

            await WriteAuditAsync(
                attemptId,
                namespaceHash,
                selection.Selected.Model,
                preSnapshot: confirmed,
                postSnapshot: verifiedSnapshot,
                turnCrossedBoundary: true,
                outcome,
                errorCategory,
                cancellationToken).ConfigureAwait(false);

            await NotifyOnceAsync(outcome, attemptId, cancellationToken).ConfigureAwait(false);

            return verifiedSnapshot is not null
                ? ActivationResult.Succeeded("verified-future-reset", attemptId)
                : ActivationResult.Ambiguous(attemptId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ActivationResult.Failed(
                "activation-unexpected-failure",
                RedactErrorCategory(exception),
                attemptId);
        }
    }

    private async Task<QuotaSnapshot?> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        QuotaSourceResult result = await _quotaSource.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Snapshot is null)
        {
            return null;
        }

        DateTimeOffset syncedAt = _clock.UtcNow;
        return QuotaNormalizer.Normalize(result.Snapshot, syncedAt, MonitoringConnectionState.Connected);
    }

    private static string ComputeWindowKey(
        DateTimeOffset observedAt,
        DateTimeOffset? resetsAt,
        out string windowKind)
    {
        if (resetsAt.HasValue)
        {
            windowKind = "authoritative";
            return FormatIso(resetsAt.Value);
        }

        windowKind = "local";
        long epochSeconds = (observedAt.ToUnixTimeSeconds() / (5 * 3600)) * (5 * 3600);
        return FormatIso(DateTimeOffset.FromUnixTimeSeconds(epochSeconds));
    }

    private static string NormalizeWorkspaceScope(string? workspaceScope)
    {
        if (string.IsNullOrWhiteSpace(workspaceScope))
        {
            return "global";
        }

        return workspaceScope.Trim();
    }

    private static DateTimeOffset? TryParseDeadline(string value)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset deadline))
        {
            return deadline;
        }

        return null;
    }

    private static bool IsVerifiedReset(DateTimeOffset? preResetsAt, DateTimeOffset? postResetsAt, DateTimeOffset now)
    {
        if (postResetsAt is not { } post)
        {
            return false;
        }

        if (preResetsAt is { } pre && post == pre)
        {
            return false;
        }

        TimeSpan ahead = post - now;
        return ahead > TimeSpan.Zero && ahead <= FiveHours;
    }

    private async Task MarkTerminalAsync(
        string attemptId,
        string outcome,
        QuotaSnapshot postSnapshot,
        string cleanupState,
        CancellationToken cancellationToken,
        string? errorCategory = null)
    {
        try
        {
            await _lockStore.MarkTerminalAsync(
                attemptId,
                outcome,
                postSnapshot.FiveHour.UsedPercent,
                postSnapshot.FiveHour.ResetsAt is { } r ? FormatIso(r) : null,
                cleanupState,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Terminal marking is best-effort after the guard is already established.
        }
    }

    private async Task WriteAuditAsync(
        string auditId,
        string namespaceHash,
        string? modelId,
        QuotaSnapshot preSnapshot,
        QuotaSnapshot? postSnapshot,
        bool turnCrossedBoundary,
        string outcome,
        string? errorCategory,
        CancellationToken cancellationToken)
    {
        var entry = new AuditEntry(
            auditId,
            namespaceHash,
            auditId,
            modelId,
            FormatIso(preSnapshot.SyncedAt),
            ToAuditQuotaSnapshot(preSnapshot),
            postSnapshot is not null ? ToAuditQuotaSnapshot(postSnapshot) : null,
            turnCrossedBoundary,
            outcome,
            errorCategory,
            FormatIso(_clock.UtcNow));

        try
        {
            await _auditStore.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Audit failures must not break the terminal outcome.
        }
    }

    private static AuditQuotaSnapshot ToAuditQuotaSnapshot(QuotaSnapshot snapshot) =>
        new(
            snapshot.FiveHour.UsedPercent,
            snapshot.FiveHour.RemainingPercent,
            snapshot.FiveHour.ResetsAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    private async Task NotifyOnceAsync(
        string outcome,
        string attemptId,
        CancellationToken cancellationToken)
    {
        (string title, string message, bool success) = outcome switch
        {
            OutcomeSucceeded => ("Activation succeeded", "The five-hour quota window was activated successfully.", true),
            OutcomeExternallySatisfied => ("Activation skipped", "The quota window started externally; no automatic generation was sent.", false),
            OutcomeNoModel => ("Activation skipped", "No acceptable model was available for activation.", false),
            OutcomeAmbiguous => ("Activation ambiguous", "Activation crossed the boundary but could not be verified.", false),
            _ => ("Activation failed", "Activation failed; see the audit log for details.", false),
        };

        try
        {
            await _notifier.NotifyAsync(
                new UserNotificationRequest(title, message, attemptId),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Notification failures must not break the terminal outcome.
        }
    }

    private static string RedactErrorCategory(Exception exception) =>
        exception switch
        {
            OperationCanceledException => "cancelled",
            _ => exception.GetType().Name,
        };

    private static string FormatIso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
