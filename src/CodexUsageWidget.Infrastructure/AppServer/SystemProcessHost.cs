using System.Diagnostics;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.AppServer;

/// <summary>
/// Starts real child processes with redirected standard streams, without leaking
/// <see cref="Process"/> beyond this infrastructure boundary. A started process is
/// owned by the returned <see cref="IHostedProcess"/> for its entire lifetime.
/// </summary>
public sealed class SystemProcessHost : IProcessHost
{
    public Task<IHostedProcess> StartAsync(
        ProcessStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Cancellation observed before launch surfaces without starting anything.
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = new(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach ((string name, string? value) in request.EnvironmentVariables)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        Process process = new() { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException(
                    "The child process was already started by another component.");
            }
        }
        catch
        {
            // A failed start (e.g. file not found) must not leak the Process object.
            process.Dispose();
            throw;
        }

        // Cancellation racing after a successful launch must not orphan the child:
        // terminate and dispose it before surfacing cancellation.
        if (cancellationToken.IsCancellationRequested)
        {
            TerminateAndDisposeSynchronously(process);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Task.FromResult<IHostedProcess>(new HostedProcess(process));
    }

    private static void TerminateAndDisposeSynchronously(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (InvalidOperationException)
        {
            // The process may have exited between the HasExited check and Kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process or a descendant could not be terminated; best-effort cleanup.
        }
        finally
        {
            process.Dispose();
        }
    }

    private sealed class HostedProcess : IHostedProcess
    {
        private readonly Process _process;
        private readonly TaskCompletionSource _exitCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _terminated;
        private bool _disposed;
        private int _terminationStarted;

        public HostedProcess(Process process)
        {
            _process = process;
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => _exitCompletion.TrySetResult();
        }

        public TextWriter StandardInput => _process.StandardInput;

        public TextReader StandardOutput => _process.StandardOutput;

        public TextReader StandardError => _process.StandardError;

        public Task<ProcessExitResult> WaitForExitAsync(CancellationToken cancellationToken)
        {
            // An already-exited process completes immediately with the observed lifetime.
            if (_process.HasExited)
            {
                return Task.FromResult(BuildResult());
            }

            return WaitForExitCoreAsync(cancellationToken);
        }

        private async Task<ProcessExitResult> WaitForExitCoreAsync(CancellationToken cancellationToken)
        {
            // Race natural exit against cancellation WITHOUT faulting the shared exit
            // completion source, so a canceled wait does not poison a later TerminateAsync.
            Task cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            Task completed = await Task.WhenAny(_exitCompletion.Task, cancellationTask)
                .ConfigureAwait(false);

            // Cancellation of this wait must NOT kill the child. It only stops waiting,
            // leaving the child running for an owner to TerminateAsync or retry.
            if (completed == cancellationTask)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return BuildResult();
        }

        public Task<ProcessExitResult> TerminateAsync(CancellationToken cancellationToken)
        {
            bool firstCall = Interlocked.Exchange(ref _terminationStarted, 1) == 0;

            // An already-exited process completes immediately with the observed lifetime.
            if (_process.HasExited)
            {
                return Task.FromResult(BuildResult());
            }

            if (!firstCall)
            {
                // A prior call already issued a kill (or is still awaiting exit). Await the
                // shared exit completion so this repeated call returns the same completed
                // lifetime result rather than reading ExitCode before the process has exited.
                return AwaitExitCompletionAsync(cancellationToken);
            }

            return TerminateCoreAsync(cancellationToken);
        }

        private async Task<ProcessExitResult> AwaitExitCompletionAsync(CancellationToken cancellationToken)
        {
            await AwaitExitAsync(cancellationToken).ConfigureAwait(false);
            return BuildResult();
        }

        private async Task AwaitExitAsync(CancellationToken cancellationToken)
        {
            if (_process.HasExited)
            {
                return;
            }

            // Race exit against caller cancellation WITHOUT faulting the shared completion,
            // so an abandoned await does not poison a later one.
            Task cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            Task completed = await Task.WhenAny(_exitCompletion.Task, cancellationTask)
                .ConfigureAwait(false);

            if (completed == cancellationTask)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        private async Task<ProcessExitResult> TerminateCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Raced to exit naturally; nothing more to do.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Best-effort termination; still await observed exit.
            }

            // Mark self-initiated termination BEFORE awaiting exit, so an abandoned await
            // (caller cancellation) still records that this wrapper killed the child.
            _terminated = true;

            // Await actual exit, honoring caller cancellation. The kill was already issued,
            // so the child will still exit even if the caller abandons this await.
            await AwaitExitAsync(cancellationToken).ConfigureAwait(false);

            return BuildResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Final safety net: never leave a live child orphaned by disposal.
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }

                try
                {
                    await _process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                }
            }

            _process.Dispose();
        }

        private ProcessExitResult BuildResult() => new(_process.ExitCode, _terminated);
    }
}
