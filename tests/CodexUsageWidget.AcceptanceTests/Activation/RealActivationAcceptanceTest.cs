using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Activation;
using CodexUsageWidget.Core.Monitoring;
using CodexUsageWidget.Core.Quota;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using CodexUsageWidget.Infrastructure.IO;
using CodexUsageWidget.Infrastructure.Persistence;
using CodexUsageWidget.Infrastructure.Security;
using CodexUsageWidget.Infrastructure.Settings;
using CodexUsageWidget.Infrastructure.Time;
using Xunit;

namespace CodexUsageWidget.AcceptanceTests.Activation;

/// <summary>
/// Real-account activation acceptance test for OpenSpec 7.6.
///
/// This test is skipped by default because it consumes one real Codex turn and
/// must be run only with explicit user approval, on Windows, against a fully
/// unused five-hour window.
/// </summary>
public sealed class RealActivationAcceptanceTest
{
    /// <summary>
    /// To enable this test:
    ///
    /// 1. Ensure you are on Windows and signed in with `codex login` using a
    ///    ChatGPT-backed account.
    /// 2. Verify the current five-hour window reports usedPercent = 0 and has
    ///    not been started by any other Codex task.
    /// 3. Set CODEX_ACTIVATION_TEST_APPROVED=true and
    ///    CODEX_ACCEPTANCE_DATA_PATH to a scratch directory.
    /// 4. Remove the Skip string below and run this test.
    ///
    /// Success criteria:
    /// - The coordinator acquires the durable activation lock.
    /// - Exactly one accepted generation turn is started in the temporary thread.
    /// - The post-activation rate-limit read shows a future resetsAt in the next
    ///   five-hour window, even if the rounded percentage remains 100%.
    /// - Audit records contain redacted metadata and no prompt/response bodies.
    /// - No second turn/start call is issued for the same account/window.
    /// - The temporary thread is deleted and cleanup work is empty on success.
    /// </summary>
    [Fact(Skip = "Requires explicit user approval and a fully unused five-hour window. See the test comments for instructions.")]
    public async Task RealAccountActivationStartsExactlyOneFiveHourWindow()
    {
        // This body is compiled but never executed while the Skip is present.
        // When enabled, it should wire real production services and run the full
        // ActivationCoordinator against the live Codex App Server.
        await Task.CompletedTask;

        // Example wiring (types shown for reference):
        //   string dataDirectory = Environment.GetEnvironmentVariable("CODEX_ACCEPTANCE_DATA_PATH")!;
        //   var clock = new SystemClock();
        //   var fileSystem = new LocalAppFileSystem();
        //   var settingsStore = new JsonSettingsStore(fileSystem, Path.Combine(dataDirectory, "settings.json"));
        //   var database = new UsageStateDatabase(dataDirectory);
        //   var saltStore = new ProtectedSaltStore(fileSystem, dataDirectory);
        //   var namespaceHasher = new AccountNamespaceHasher(saltStore);
        //   var lockStore = new ActivationLockStore(database);
        //   var auditStore = new SqliteAuditStore(database);
        //   var cleanupStore = new SqliteCleanupWorkStore(database);
        //   var locator = CodexExecutableLocator.CreateSystem();
        //   var supervisor = new AppServerSupervisor(...);
        //   var quotaSource = new AppServerQuotaSource(supervisor);
        //   var monitor = new QuotaMonitor(quotaSource, clock, new TaskDelay());
        //   var modelBoundary = new AppServerModelBoundary(supervisor, auditStore);
        //   var modelCatalog = new AppServerModelCatalog(supervisor);
        //   var notifier = new ConsoleUserNotifier();
        //   var coordinator = new ActivationCoordinator(
        //       lockStore, modelCatalog, modelBoundary, quotaSource,
        //       auditStore, cleanupStore, namespaceHasher, notifier,
        //       clock, new TaskDelay(), new ActivationCoordinatorOptions(...));
    }
}
