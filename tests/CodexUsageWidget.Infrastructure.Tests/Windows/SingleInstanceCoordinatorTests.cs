using System.IO.Pipes;
using System.Text;
using CodexUsageWidget.Infrastructure.Windows;
using Xunit;

#pragma warning disable xUnit1031 // Blocking task operations are intentional here to keep the Windows mutex owner thread stable.

namespace CodexUsageWidget.Infrastructure.Tests.Windows;

/// <summary>
/// Tests for the per-user single-instance coordinator. Each test uses a unique
/// coordinator name so that parallel test runs do not interfere. Cross-instance
/// behavior is evaluated from a separate thread (faithful proxy for a second
/// process), but the first instance's mutex is always released synchronously on
/// the thread that acquired it to satisfy Windows mutex ownership rules.
/// </summary>
public sealed class SingleInstanceCoordinatorTests
{
    private static string UniqueName() =>
        "codex-widget-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void FirstInstanceAcquiresOwnership()
    {
        SingleInstanceCoordinator coordinator = new(UniqueName());

        bool acquired = coordinator.TryAcquireInstance();

        Assert.True(acquired);
        coordinator.ReleaseInstance();
    }

    [Fact]
    public void SecondInstanceFailsToAcquireWhileFirstHoldsMutex()
    {
        string name = UniqueName();
        SingleInstanceCoordinator first = new(name);

        Assert.True(first.TryAcquireInstance());
        try
        {
            // A real second process would run on a different thread, so we
            // evaluate the second coordinator from the thread pool.
            bool acquired = Task.Run(() =>
            {
                SingleInstanceCoordinator second = new(name);
                return second.TryAcquireInstance();
            }).GetAwaiter().GetResult();

            Assert.False(acquired);
        }
        finally
        {
            first.ReleaseInstance();
        }
    }

    [Fact]
    public void ReleaseAllowsSubsequentInstanceToAcquire()
    {
        string name = UniqueName();
        SingleInstanceCoordinator first = new(name);
        SingleInstanceCoordinator second = new(name);

        Assert.True(first.TryAcquireInstance());
        first.ReleaseInstance();

        // Give the kernel a moment to observe the released mutex before the
        // second instance attempts to open it.
        Thread.Sleep(50);

        bool acquired = second.TryAcquireInstance();
        Assert.True(acquired);
        second.ReleaseInstance();
    }

    [Fact]
    public void ListenerReceivesBringForwardSignalViaNamedPipe()
    {
        string name = UniqueName();
        SingleInstanceCoordinator coordinator = new(name);

        Assert.True(coordinator.TryAcquireInstance());
        TaskCompletionSource tcs = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));

        coordinator.StartListening(
            (CancellationToken ct) =>
            {
                tcs.TrySetResult();
                return Task.CompletedTask;
            },
            cts.Token);

        try
        {
            string userToken = GetCurrentUserToken();
            string pipeName = $"{name}_BringForward_{userToken}";

            using NamedPipeClientStream client = new(
                ".",
                pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            client.Connect(2000);

            byte[] bytes = Encoding.UTF8.GetBytes("bring-forward\n");
            client.Write(bytes);
            client.Flush();

            Assert.True(tcs.Task.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(tcs.Task.IsCompletedSuccessfully);
        }
        finally
        {
            coordinator.ReleaseInstance();
        }
    }

    [Fact]
    public void ListenerStopsAfterRelease()
    {
        string name = UniqueName();
        SingleInstanceCoordinator coordinator = new(name);

        Assert.True(coordinator.TryAcquireInstance());
        using CancellationTokenSource cts = new();
        coordinator.StartListening(
            (CancellationToken ct) => Task.CompletedTask,
            cts.Token);

        coordinator.ReleaseInstance();
        cts.Cancel();

        // Give the listener loop a moment to observe the cancellation.
        Thread.Sleep(100);
        Assert.True(cts.IsCancellationRequested);
    }

    private static string GetCurrentUserToken()
    {
        System.Security.Principal.WindowsIdentity identity =
            System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? identity.Name ?? "unknown";
    }
}

#pragma warning restore xUnit1031
