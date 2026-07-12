using System.IO.Pipes;
using System.Runtime.Versioning;
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
[SupportedOSPlatform("windows")]
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

        // The kernel may need a moment to observe the released mutex; poll
        // instead of a single fixed sleep to keep the test stable under load.
        bool acquired = false;
        for (int i = 0; i < 50 && !acquired; i++)
        {
            acquired = second.TryAcquireInstance();
            if (!acquired)
            {
                Thread.Sleep(20);
            }
        }

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
    public void SecondInstanceSignalsFirstInstanceViaPublicApi()
    {
        string name = UniqueName();
        SingleInstanceCoordinator first = new(name);
        Assert.True(first.TryAcquireInstance());

        TaskCompletionSource tcs = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        first.StartListening(
            (CancellationToken ct) =>
            {
                tcs.TrySetResult();
                return Task.CompletedTask;
            },
            cts.Token);

        try
        {
            bool signaled = Task.Run(() =>
            {
                SingleInstanceCoordinator second = new(name);
                if (second.TryAcquireInstance())
                {
                    return false;
                }

                second.SignalExistingInstanceAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
                return true;
            }).GetAwaiter().GetResult();

            Assert.True(signaled);
            Assert.True(tcs.Task.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(tcs.Task.IsCompletedSuccessfully);
        }
        finally
        {
            first.ReleaseInstance();
        }
    }

    [Fact]
    public void ListenerSurvivesCallbackExceptionAndAcceptsNextSignal()
    {
        string name = UniqueName();
        SingleInstanceCoordinator first = new(name);
        Assert.True(first.TryAcquireInstance());

        int callCount = 0;
        TaskCompletionSource secondSignal = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        first.StartListening(
            (CancellationToken ct) =>
            {
                int current = Interlocked.Increment(ref callCount);
                if (current == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                secondSignal.TrySetResult();
                return Task.CompletedTask;
            },
            cts.Token);

        try
        {
            Task.Run(() =>
            {
                SingleInstanceCoordinator second = new(name);
                second.SignalExistingInstanceAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
                Thread.Sleep(200);
                second.SignalExistingInstanceAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
            }).GetAwaiter().GetResult();

            Assert.True(secondSignal.Task.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(secondSignal.Task.IsCompletedSuccessfully);
            Assert.True(callCount >= 2);
        }
        finally
        {
            first.ReleaseInstance();
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

        // After ReleaseInstance returns, the listener loop must have exited so
        // that a new client connection to the per-user pipe times out.
        string pipeName = $"{name}_BringForward_{GetCurrentUserToken()}";
        using NamedPipeClientStream client = new(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);

        Assert.Throws<TimeoutException>(() => client.Connect(500));
    }

    [Fact]
    public void SignalExistingInstanceAsyncThrowsTimeoutWhenOwnerNotListening()
    {
        string name = UniqueName();
        SingleInstanceCoordinator first = new(name);
        Assert.True(first.TryAcquireInstance());

        try
        {
            SingleInstanceCoordinator second = new(name);
            Assert.False(second.TryAcquireInstance());

            Assert.Throws<TimeoutException>(
                () => second.SignalExistingInstanceAsync(CancellationToken.None)
                    .GetAwaiter().GetResult());
        }
        finally
        {
            first.ReleaseInstance();
        }
    }

    [Fact]
    public void StartListeningIsIdempotent()
    {
        string name = UniqueName();
        SingleInstanceCoordinator coordinator = new(name);
        Assert.True(coordinator.TryAcquireInstance());

        int callCount = 0;
        TaskCompletionSource signaled = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));

        coordinator.StartListening(
            (CancellationToken ct) =>
            {
                Interlocked.Increment(ref callCount);
                signaled.TrySetResult();
                return Task.CompletedTask;
            },
            cts.Token);

        // A second StartListening call must be ignored and must not replace the
        // already-active listener or its callback.
        coordinator.StartListening(
            (CancellationToken ct) => Task.CompletedTask,
            cts.Token);

        try
        {
            SingleInstanceCoordinator signaller = new(name);
            Assert.False(signaller.TryAcquireInstance());
            signaller.SignalExistingInstanceAsync(CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.True(signaled.Task.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, callCount);
        }
        finally
        {
            coordinator.ReleaseInstance();
        }
    }

    private static string GetCurrentUserToken()
    {
        System.Security.Principal.WindowsIdentity identity =
            System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? identity.Name ?? "unknown";
    }
}

#pragma warning restore xUnit1031
