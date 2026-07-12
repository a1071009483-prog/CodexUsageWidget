using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Quota;
using CodexUsageWidget.Core.Tests.Testing;
using Xunit;

namespace CodexUsageWidget.Core.Tests.Activation;

public sealed class ActivationCoordinatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
    private const string DefaultModel = "gpt-4o-mini";

    private readonly ManualClock _clock;
    private readonly ManualDelay _delay;
    private readonly FakeActivationLockStore _lockStore;
    private readonly FakeModelCatalog _modelCatalog;
    private readonly ConfigurableModelBoundary _modelBoundary;
    private readonly FakeQuotaSource _quotaSource;
    private readonly FakeAuditStore _auditStore;
    private readonly FakeCleanupWorkStore _cleanupStore;
    private readonly FakeNamespaceHasher _namespaceHasher;
    private readonly FakeUserNotifier _notifier;
    private readonly ActivationCoordinator _coordinator;

    public ActivationCoordinatorTests()
    {
        _clock = new ManualClock(Now);
        _delay = new ManualDelay(_clock);
        _lockStore = new FakeActivationLockStore();
        _modelCatalog = new FakeModelCatalog();
        _modelBoundary = new ConfigurableModelBoundary();
        _quotaSource = new FakeQuotaSource();
        _auditStore = new FakeAuditStore();
        _cleanupStore = new FakeCleanupWorkStore();
        _namespaceHasher = new FakeNamespaceHasher();
        _notifier = new FakeUserNotifier();

        _coordinator = new ActivationCoordinator(
            _lockStore,
            _modelCatalog,
            _modelBoundary,
            _quotaSource,
            _auditStore,
            _cleanupStore,
            _namespaceHasher,
            _notifier,
            _clock,
            _delay,
            new ActivationCoordinatorOptions
            {
                IsAutomationEnabled = true,
                ConfirmationDebounce = TimeSpan.FromSeconds(1),
                TurnTimeout = TimeSpan.FromSeconds(5),
                VerificationTimeout = TimeSpan.FromSeconds(10),
                VerificationPollInterval = TimeSpan.FromSeconds(1),
            });

        _modelCatalog.Models = new[]
        {
            new ModelCandidate("id-mini", DefaultModel, "Mini", false, ["minimal"]),
        };
    }

    [Fact]
    public async Task HappyPathActivatesAndVerifiesSuccess()
    {
        EnqueueConfirmationSnapshots(
            Raw(0, null),
            Raw(0, null));
        EnqueueVerificationSnapshot(Raw(0, Future(5 * 60 * 60 - 60)));

        _modelBoundary.OnStart = (_, _) =>
            new ModelGenerationResult(true, true, ThreadId: "thread-1", TurnId: "turn-1");

        Task<ActivationResult> task = _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(5));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        ActivationResult result = await task;

        Assert.True(result.IsSuccess);
        Assert.Single(_modelBoundary.StartCalls);
        Assert.Equal(DefaultModel, _modelBoundary.StartCalls[0].Request.ModelId);
        Assert.Single(_modelBoundary.DeleteCalls);
        Assert.Single(_notifier.Calls);
        Assert.Single(_auditStore.Entries);
        AuditEntry audit = _auditStore.Entries.Values.Single();
        Assert.Equal(DefaultModel, audit.ModelId);
        Assert.True(audit.TurnCrossedBoundary);
        Assert.Equal("succeeded", audit.Outcome);
    }

    [Fact]
    public async Task NotEligibleWhenSnapshotIsNotFreshZero()
    {
        ActivationResult result = await _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, false, true, null),
            new ActivationRequest(true));

        Assert.Equal(ActivationOutcome.NotEligible, result.Outcome);
        Assert.Empty(_modelBoundary.StartCalls);
        Assert.Empty(_notifier.Calls);
    }

    [Fact]
    public async Task SecondConfirmationFailsReturnsNotEligible()
    {
        _quotaSource.EnqueueSuccess(Raw(5, null));

        ActivationResult result = await _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ActivationOutcome.NotEligible, result.Outcome);
        Assert.Empty(_modelBoundary.StartCalls);
    }

    [Fact]
    public async Task ActiveSuppressionLockReturnsSuppressed()
    {
        EnqueueConfirmationSnapshots(Raw(0, null));
        await SeedActiveLockAsync(Now.AddHours(1));

        ActivationResult result = await _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ActivationOutcome.Suppressed, result.Outcome);
        Assert.Empty(_modelBoundary.StartCalls);
    }

    [Fact]
    public async Task LockConflictReturnsSuppressed()
    {
        EnqueueConfirmationSnapshots(Raw(0, null));
        await SeedActiveLockAsync(Now.AddHours(5));

        ActivationResult result = await _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ActivationOutcome.Suppressed, result.Outcome);
        Assert.Empty(_modelBoundary.StartCalls);
    }

    [Fact]
    public async Task FinalPreflightExternallySatisfied()
    {
        EnqueueConfirmationSnapshots(Raw(0, null));
        _quotaSource.EnqueueSuccess(Raw(7, null));

        ActivationResult result = await _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ActivationOutcome.ExternallySatisfied, result.Outcome);
        Assert.Empty(_modelBoundary.StartCalls);
        Assert.Single(_auditStore.Entries);
        Assert.Single(_notifier.Calls);
    }

    [Fact]
    public async Task NoModelReturnsFail()
    {
        EnqueueConfirmationSnapshots(Raw(0, null));
        _modelCatalog.Models = Array.Empty<ModelCandidate>();

        ActivationResult result = await _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ActivationOutcome.NoModel, result.Outcome);
        Assert.Empty(_modelBoundary.StartCalls);
    }

    [Fact]
    public async Task ModelUnavailableFallsBackAndSucceeds()
    {
        _modelCatalog.Models = new[]
        {
            new ModelCandidate("id-a", "gpt-4o-mini", "A", false, ["minimal"]),
            new ModelCandidate("id-b", "model-b", "B", true, ["minimal"]),
        };

        EnqueueConfirmationSnapshots(
            Raw(0, null),
            Raw(0, null));
        EnqueueVerificationSnapshot(Raw(0, Future(5 * 60 * 60 - 60)));

        int calls = 0;
        _modelBoundary.OnStart = (request, _) =>
        {
            calls++;
            if (request.ModelId == "gpt-4o-mini")
            {
                return new ModelGenerationResult(false, false, FailureCategory: "model-unavailable");
            }

            return new ModelGenerationResult(true, true, ThreadId: "thread-2", TurnId: "turn-2");
        };

        Task<ActivationResult> task = _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(5));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        ActivationResult result = await task;

        Assert.True(result.IsSuccess);
        Assert.Equal(2, calls);
        Assert.Equal("model-b", _modelBoundary.StartCalls[1].Request.ModelId);
    }

    [Fact]
    public async Task TurnTimeoutInterruptsAndReturnsAmbiguous()
    {
        EnqueueConfirmationSnapshots(
            Raw(0, null),
            Raw(0, null));
        _quotaSource.EnqueueSuccess(Raw(0, null));

        _modelBoundary.OnStart = (_, _) =>
            new ModelGenerationResult(true, true, ThreadId: "thread-3", TurnId: "turn-3");

        Task<ActivationResult> task = _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(5));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(10));

        ActivationResult result = await task;

        Assert.Equal(ActivationOutcome.Ambiguous, result.Outcome);
        Assert.Single(_modelBoundary.StartCalls);
        Assert.Single(_modelBoundary.InterruptCalls);
        Assert.Equal("turn-3", _modelBoundary.InterruptCalls[0].TurnId);
        Assert.Single(_notifier.Calls);
    }

    [Fact]
    public async Task DeleteFailureEnqueuesCleanup()
    {
        EnqueueConfirmationSnapshots(
            Raw(0, null),
            Raw(0, null));
        EnqueueVerificationSnapshot(Raw(0, Future(5 * 60 * 60 - 60)));

        _modelBoundary.OnStart = (_, _) =>
            new ModelGenerationResult(true, true, ThreadId: "thread-4", TurnId: "turn-4");
        _modelBoundary.OnDelete = (_, _) => throw new InvalidOperationException("delete failed");

        Task<ActivationResult> task = _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(5));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        ActivationResult result = await task;

        Assert.True(result.IsSuccess);
        Assert.Single(_cleanupStore.Pending);
        Assert.Equal("thread-4", _cleanupStore.Pending[0].ThreadId);
    }

    [Fact]
    public async Task LockStoreExceptionFailsClosedWithoutBoundaryCall()
    {
        EnqueueConfirmationSnapshots(Raw(0, null));
        _lockStore.ExceptionToThrow = new InvalidOperationException("store down");

        ActivationResult result = await _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        Assert.Equal(ActivationOutcome.Failed, result.Outcome);
        Assert.Empty(_modelBoundary.StartCalls);
    }

    [Fact]
    public async Task BoundaryExceptionFailsClosed()
    {
        EnqueueConfirmationSnapshots(
            Raw(0, null),
            Raw(0, null));
        _modelBoundary.OnStart = (_, _) => throw new InvalidOperationException("boom");

        Task<ActivationResult> task = _coordinator.TryActivateAsync(
            Identity(),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        ActivationResult result = await task;

        Assert.Equal(ActivationOutcome.Failed, result.Outcome);
        Assert.Single(_modelBoundary.StartCalls);
    }

    [Fact]
    public async Task AuditExcludesSensitiveData()
    {
        EnqueueConfirmationSnapshots(
            Raw(0, null),
            Raw(0, null));
        EnqueueVerificationSnapshot(Raw(0, Future(5 * 60 * 60 - 60)));

        _modelBoundary.OnStart = (_, _) =>
            new ModelGenerationResult(true, true, ThreadId: "thread-5", TurnId: "turn-5");

        Task<ActivationResult> task = _coordinator.TryActivateAsync(
            new AccountIdentity("secret@example.com"),
            Snapshot(0, true, true, null),
            new ActivationRequest(true));

        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(5));
        await _delay.AdvanceAsync(TimeSpan.FromSeconds(1));

        await task;

        AuditEntry audit = _auditStore.Entries.Values.Single();
        Assert.DoesNotContain("secret@example.com", audit.NamespaceHash);
        Assert.DoesNotContain("secret", audit.ToString());
    }

    private void EnqueueConfirmationSnapshots(params RawRateLimitSnapshot[] snapshots)
    {
        foreach (RawRateLimitSnapshot snapshot in snapshots)
        {
            _quotaSource.EnqueueSuccess(snapshot);
        }
    }

    private void EnqueueVerificationSnapshot(RawRateLimitSnapshot snapshot) =>
        _quotaSource.EnqueueSuccess(snapshot);

    private async Task SeedActiveLockAsync(DateTimeOffset suppressionDeadline)
    {
        await _lockStore.TryAcquireAsync(
            new ActivationAttempt(
                AttemptId: "existing",
                NamespaceHash: _namespaceHasher.Hash,
                WorkspaceScope: "global",
                WindowKey: LocalWindowKey(),
                WindowKind: "local",
                SuppressionDeadline: suppressionDeadline.ToString("O"),
                ObservedAt: Now.ToString("O"),
                AttemptAt: Now.ToString("O"),
                PreUsedPercent: 0,
                PreResetsAt: null,
                ModelId: null,
                TurnStarted: false,
                TerminalOutcome: null,
                PostUsedPercent: null,
                PostResetsAt: null,
                CleanupState: "none"),
            CancellationToken.None);
    }

    private static AccountIdentity Identity() =>
        new("user@example.com");

    private static string LocalWindowKey() =>
        new DateTimeOffset(
            (Now.ToUnixTimeSeconds() / (5 * 3600)) * (5 * 3600),
            TimeSpan.Zero).ToString("O");

    private static QuotaSnapshot Snapshot(
        int used,
        bool fresh,
        bool available,
        DateTimeOffset? resetsAt)
    {
        return new QuotaSnapshot(
            ScopeLabel: "test",
            new QuotaBucketSnapshot(
                QuotaBucket.FiveHour,
                used,
                100 - used,
                resetsAt,
                WindowDurationMinutes: 300,
                available),
            new QuotaBucketSnapshot(
                QuotaBucket.Weekly,
                0,
                100,
                ResetsAt: null,
                WindowDurationMinutes: 10080,
                true),
            SyncedAt: Now,
            IsFresh: fresh,
            MonitoringConnectionState.Connected,
            Countdown: null);
    }

    private static RawRateLimitSnapshot Raw(int used, long? resetsAt) =>
        new(
            LimitId: "codex",
            LimitName: "Codex",
            PlanType: "test",
            Primary: new RawRateLimitWindow(used, resetsAt, WindowDurationMins: 300));

    private static long Future(int seconds) =>
        Now.AddSeconds(seconds).ToUnixTimeSeconds();

    public void Dispose() => _delay.Dispose();
}
