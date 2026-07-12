using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using CodexUsageWidget.AcceptanceTests.Testing;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Monitoring;
using CodexUsageWidget.Core.Quota;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using CodexUsageWidget.Infrastructure.Persistence;
using CodexUsageWidget.Infrastructure.Security;
using CodexUsageWidget.Infrastructure.Time;
using Xunit;

namespace CodexUsageWidget.AcceptanceTests.Activation;

/// <summary>
/// Real-account activation acceptance test for OpenSpec 7.6.
///
/// This test is skipped by default. To run it:
///
/// 1. Be on Windows, signed in with <c>codex login</c> using a ChatGPT-backed account.
/// 2. Confirm the current five-hour bucket reports <c>usedPercent = 0</c> and the timer
///    has not been started by any other Codex task.
/// 3. Set <c>CODEX_ACTIVATION_TEST_APPROVED=true</c> and
///    <c>CODEX_ACCEPTANCE_DATA_PATH</c> to a scratch directory.
/// 4. Run this test.
/// </summary>
public sealed class RealActivationAcceptanceTest
{
    private const string ApprovalVariable = "CODEX_ACTIVATION_TEST_APPROVED";
    private const string DataPathVariable = "CODEX_ACCEPTANCE_DATA_PATH";
    private const string ActivationPrompt = "Respond with exactly the word 'OK' and do not use any tools.";

    [EnvironmentFact(ApprovalVariable, DataPathVariable)]
    public async Task RealAccountActivationStartsExactlyOneFiveHourWindow()
    {
        Assert.True(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "OpenSpec 7.6 requires a Windows environment with DPAPI and the Codex CLI.");

        string? approval = Environment.GetEnvironmentVariable(ApprovalVariable);
        if (!string.Equals(approval, "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail($"Set {ApprovalVariable}=true to approve this one-time real activation test.");
        }

        string codexPath = ResolveCodexPath();
        string dataDirectory = CreateDataDirectory();
        string activationWorkingDirectory = Path.Combine(dataDirectory, "activation-work");
        Directory.CreateDirectory(activationWorkingDirectory);

        var settings = AppServerSupervisorSettings.Default;
        AppServerSupervisor supervisor = new(
            new SystemProcessHost(),
            new ProcessStartRequest(codexPath, ["app-server"], dataDirectory),
            new ClientInformation("codex-usage-widget-acceptance", "1.0.0", "Codex Usage Widget Acceptance"),
            TimeSpan.FromSeconds(5),
            new TaskDelay(),
            settings,
            healthyDelay: new TaskDelay(),
            graceDelay: new TaskDelay());

        AppServerQuotaSource source = new(supervisor);
        QuotaMonitor monitor = new(
            source,
            new SystemClock(),
            new TaskDelay(),
            pollInterval: TimeSpan.FromSeconds(60),
            staleThreshold: TimeSpan.FromSeconds(120));

        ProtectedSaltStore? saltStore = null;

        try
        {
            using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            await supervisor.StartAsync(testCts.Token);
            await monitor.StartAsync(testCts.Token);

            AppServerGenerationSession generation = await WaitForGenerationAsync(supervisor, TimeSpan.FromSeconds(60), testCts.Token);
            AccountIdentity identity = await ResolveAccountIdentityAsync(generation.Session.Gateway, testCts.Token);

            QuotaSnapshot snapshot = await WaitForFreshUnusedFiveHourSnapshotAsync(
                monitor,
                TimeSpan.FromSeconds(60),
                testCts.Token);

            if (snapshot.FiveHour.ResetsAt is not null)
            {
                Assert.Fail(
                    "The five-hour bucket already has a future resetsAt, which means the timer " +
                    "appears to be active. Wait for a fully unused window (no resetsAt) before " +
                    "running this test.");
            }

            UsageStateDatabase database = new(dataDirectory);
            DpapiProtectedData protectedData = new();
            saltStore = new ProtectedSaltStore(dataDirectory, protectedData);
            AccountNamespaceHasher namespaceHasher = new(saltStore);
            ActivationLockStore lockStore = new(database);
            SqliteAuditStore auditStore = new(database);
            SqliteCleanupWorkStore cleanupStore = new(database);
            AppServerModelBoundary modelBoundary = new(generation.Session.Gateway);
            PinnedModelCatalog modelCatalog = new(generation.Session.Gateway);
            CaptureNotifier notifier = new();

            ActivationCoordinator coordinator = new(
                lockStore,
                modelCatalog,
                modelBoundary,
                source,
                auditStore,
                cleanupStore,
                namespaceHasher,
                notifier,
                new SystemClock(),
                new TaskDelay(),
                new ActivationCoordinatorOptions
                {
                    IsAutomationEnabled = true,
                    ConfirmationDebounce = TimeSpan.FromSeconds(2),
                    TurnTimeout = TimeSpan.FromSeconds(20),
                    VerificationTimeout = TimeSpan.FromSeconds(60),
                    VerificationPollInterval = TimeSpan.FromSeconds(5),
                    WorkingDirectory = activationWorkingDirectory,
                });

            ActivationResult result = await coordinator.TryActivateAsync(
                identity,
                snapshot,
                new ActivationRequest(true),
                testCts.Token);

            Assert.Equal(ActivationOutcome.Succeeded, result.Outcome);
            Assert.NotNull(result.AttemptId);

            // Wait for the monitor to reflect the post-activation future reset.
            QuotaSnapshot postSnapshot = await WaitForPostActivationSnapshotAsync(
                monitor,
                snapshot.FiveHour.ResetsAt,
                TimeSpan.FromSeconds(60),
                testCts.Token);

            Assert.True(
                postSnapshot.FiveHour.ResetsAt > DateTimeOffset.UtcNow,
                "The post-activation five-hour reset should be in the future.");
            Assert.NotEqual(snapshot.FiveHour.ResetsAt, postSnapshot.FiveHour.ResetsAt);

            // Audit evidence: exactly one row crossed the generation boundary and recorded success.
            IReadOnlyList<AuditEntry> audits = await ToListAsync(
                auditStore.ReadAllAsync(testCts.Token),
                testCts.Token);
            Assert.Contains(audits, a => a.AttemptId == result.AttemptId && a.TurnCrossedBoundary);
            AuditEntry terminalAudit = audits.First(a => a.AttemptId == result.AttemptId && !string.IsNullOrEmpty(a.Outcome));
            Assert.Equal("succeeded", terminalAudit.Outcome);
            Assert.NotNull(terminalAudit.ModelId);
            Assert.NotNull(terminalAudit.PostQuota);
            Assert.Null(terminalAudit.ErrorCategory);

            // Redaction: audit records must not contain the raw email, the prompt text, or tokens.
            string redactedJson = JsonSerializer.Serialize(audits);
            Assert.DoesNotContain(identity.Email, redactedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@", redactedJson);
            Assert.DoesNotContain(ActivationPrompt, redactedJson);
            Assert.All(audits, a => Assert.NotEqual(identity.Email, a.NamespaceHash));

            // Lock-store evidence: the attempt should be terminal with completed cleanup.
            string namespaceHash = await namespaceHasher.GetNamespaceHashAsync(identity, testCts.Token);
            string windowKey = ComputeLocalWindowKey(snapshot.SyncedAt);
            ActivationAttempt? storedAttempt = await lockStore.GetActiveAsync(
                namespaceHash,
                "global",
                windowKey,
                testCts.Token);
            Assert.NotNull(storedAttempt);
            Assert.Equal("succeeded", storedAttempt.TerminalOutcome);
            Assert.Equal("completed", storedAttempt.CleanupState);

            // No deferred cleanup work should remain after a successful deletion.
            IReadOnlyList<CleanupWorkItem> pendingCleanup = await ToListAsync(
                cleanupStore.ReadPendingAsync(testCts.Token),
                testCts.Token);
            Assert.Empty(pendingCleanup);

            // A second activation attempt for the same account while the five-hour timer is
            // active must not issue another turn/start.
            ActivationResult secondResult = await coordinator.TryActivateAsync(
                identity,
                postSnapshot,
                new ActivationRequest(true),
                testCts.Token);

            Assert.Equal(ActivationOutcome.NotEligible, secondResult.Outcome);
            Assert.Contains("verified-future-reset", secondResult.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { source.Dispose(); } catch { /* best effort */ }
            try { await monitor.StopAsync(); } catch { /* best effort */ }
            try { await monitor.DisposeAsync(); } catch { /* best effort */ }
            try { await supervisor.StopAsync(CancellationToken.None); } catch { /* best effort */ }
            try { await supervisor.DisposeAsync(); } catch { /* best effort */ }
            try { saltStore?.Dispose(); } catch { /* best effort */ }

            try
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; the App Server child may still hold files briefly.
            }
        }
    }

    private static string ResolveCodexPath()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("CODEX_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        CodexExecutableResolution resolution = CodexExecutableLocator.CreateSystem().Locate();
        if (!resolution.Found)
        {
            Assert.Fail($"Codex executable not found: {resolution.Diagnostic}");
        }

        return resolution.Command!;
    }

    private static string CreateDataDirectory()
    {
        string? basePath = Environment.GetEnvironmentVariable(DataPathVariable);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException($"{DataPathVariable} is not set.");
        }

        string path = Path.Combine(
            basePath,
            $"activation-{DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ComputeLocalWindowKey(DateTimeOffset observedAt)
    {
        long epochSeconds = (observedAt.ToUnixTimeSeconds() / (5 * 3600)) * (5 * 3600);
        return DateTimeOffset.FromUnixTimeSeconds(epochSeconds).ToString("O", CultureInfo.InvariantCulture);
    }

    private static async Task<AppServerGenerationSession> WaitForGenerationAsync(
        AppServerSupervisor supervisor,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            AppServerGenerationSession? generation = supervisor.CurrentGeneration;
            if (generation is not null)
            {
                return generation;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The App Server session was not published in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    private static async Task<AccountIdentity> ResolveAccountIdentityAsync(
        CodexAppServerGateway gateway,
        CancellationToken cancellationToken)
    {
        AccountReadResponse account = await gateway.ReadAccountAsync(refreshToken: false, cancellationToken)
            .ConfigureAwait(false);

        AccountAuthenticationEvaluator evaluator = new();
        AuthenticationAssessment assessment = evaluator.Evaluate(account);

        if (assessment.State != AuthenticationState.Supported)
        {
            Assert.Fail($"Unsupported authentication state: {assessment.State}. {assessment.Diagnostic}");
        }

        if (string.IsNullOrWhiteSpace(assessment.IdentityMaterial))
        {
            Assert.Fail("The App Server account did not return an email.");
        }

        return new AccountIdentity(
            assessment.IdentityMaterial,
            assessment.PlanType,
            assessment.WorkspaceIdentity);
    }

    private static async Task<QuotaSnapshot> WaitForFreshUnusedFiveHourSnapshotAsync(
        QuotaMonitor monitor,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            QuotaSnapshot? snapshot = monitor.CurrentSnapshot;
            if (snapshot is not null
                && snapshot.IsFresh
                && snapshot.ConnectionState == MonitoringConnectionState.Connected
                && snapshot.FiveHour.IsAvailable
                && snapshot.FiveHour.UsedPercent == 0)
            {
                return snapshot;
            }

            if (snapshot is not null && snapshot.FiveHour.UsedPercent != 0)
            {
                Assert.Fail(
                    $"The five-hour bucket is not fully unused (usedPercent = {snapshot.FiveHour.UsedPercent}). " +
                    "Wait for a fresh 100% remaining window before running this test.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                string state = snapshot is null
                    ? "no snapshot"
                    : $"{snapshot.ConnectionState}, fresh={snapshot.IsFresh}, used={snapshot.FiveHour.UsedPercent}";
                throw new TimeoutException($"Did not receive a fresh unused five-hour snapshot in time. State: {state}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    private static async Task<QuotaSnapshot> WaitForPostActivationSnapshotAsync(
        QuotaMonitor monitor,
        DateTimeOffset? previousResetsAt,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            QuotaSnapshot? snapshot = monitor.CurrentSnapshot;
            if (snapshot is not null
                && snapshot.IsFresh
                && snapshot.FiveHour.ResetsAt is { } resetsAt
                && resetsAt > DateTimeOffset.UtcNow
                && (!previousResetsAt.HasValue || resetsAt != previousResetsAt.Value))
            {
                return snapshot;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The post-activation snapshot did not show a changed future reset in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<T>> ToListAsync<T>(
        IAsyncEnumerable<T> enumerable,
        CancellationToken cancellationToken)
    {
        var list = new List<T>();
        await foreach (T item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            list.Add(item);
        }

        return list;
    }

    /// <summary>
    /// Pins model discovery to the App Server generation that was current when the test
    /// began, so the coordinator cannot accidentally list models from a restarted
    /// generation while sending generation requests through the original gateway.
    /// </summary>
    private sealed class PinnedModelCatalog : IModelCatalog
    {
        private readonly CodexAppServerGateway _gateway;

        public PinnedModelCatalog(CodexAppServerGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public async Task<IReadOnlyList<ModelCandidate>> ListModelsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ModelDescriptor> models = await _gateway.ListAllModelsAsync(
                    includeHidden: false,
                    cancellationToken)
                .ConfigureAwait(false);

            return models.Select(ToCandidate).ToList();
        }

        private static ModelCandidate ToCandidate(ModelDescriptor descriptor)
        {
            IReadOnlyList<string> efforts = descriptor.SupportedReasoningEfforts is null
                ? Array.Empty<string>()
                : descriptor.SupportedReasoningEfforts.Select(e => e.ReasoningEffort).ToList();

            return new ModelCandidate(
                descriptor.Id,
                descriptor.Model,
                descriptor.DisplayName,
                descriptor.IsDefault,
                efforts);
        }
    }

    private sealed class CaptureNotifier : IUserNotifier
    {
        public List<UserNotificationRequest> Requests { get; } = new();

        public Task<UserNotificationResult> NotifyAsync(
            UserNotificationRequest request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            Requests.Add(request);
            return Task.FromResult(new UserNotificationResult(true));
        }
    }
}
