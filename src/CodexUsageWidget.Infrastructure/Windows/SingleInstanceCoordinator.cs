using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Windows;

/// <summary>
/// Windows-only implementation of <see cref="ISingleInstanceCoordinator"/>.
/// Uses a per-user named mutex for ownership and a per-user named pipe for
/// bring-forward signaling.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SingleInstanceCoordinator : ISingleInstanceCoordinator, IDisposable
{
    private static readonly string CurrentUserToken = ComputeCurrentUserToken();

    private readonly string _mutexName;
    private readonly string _pipeName;

    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool _disposed;

    /// <summary>
    /// Creates a coordinator using the given application-specific instance name.
    /// The name is combined with the current Windows user identity so that the
    /// mutex and pipe are scoped to the interactive user.
    /// </summary>
    public SingleInstanceCoordinator(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);

        _mutexName = @$"Local\{instanceName}_SingleInstance_{CurrentUserToken}";
        _pipeName = $"{instanceName}_BringForward_{CurrentUserToken}";
    }

    /// <inheritdoc/>
    public bool TryAcquireInstance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_mutex is not null)
        {
            return _ownsMutex;
        }

        try
        {
            // The standard Windows single-instance pattern: CreateMutex returns
            // createdNew=true only when the named mutex did not already exist.
            // An existing mutex means another instance is running (or has very
            // recently exited and not yet destroyed the object).
            _mutex = new Mutex(initiallyOwned: true, _mutexName, out bool createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                // We opened an existing mutex. We do not own it, so just close
                // our handle and report that another instance exists.
                _mutex.Dispose();
                _mutex = null;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // We can see the mutex but cannot acquire it (e.g., different ACL).
            _ownsMutex = false;
        }

        return _ownsMutex;
    }

    /// <inheritdoc/>
    public void StartListening(
        Func<CancellationToken, Task> onBringForward,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onBringForward);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_ownsMutex)
        {
            throw new InvalidOperationException(
                "Cannot start listening without owning the single-instance mutex.");
        }

        if (_listenerCts is not null)
        {
            return;
        }

        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken listenerToken = _listenerCts.Token;
        _listenerTask = Task.Run(() => ListenAsync(_pipeName, onBringForward, listenerToken), listenerToken);
    }

    /// <inheritdoc/>
    public async Task SignalExistingInstanceAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ownsMutex)
        {
            throw new InvalidOperationException(
                "Cannot signal the existing instance while owning the single-instance mutex.");
        }

        await using NamedPipeClientStream client = new(
            ".",
            _pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(2000, cancellationToken).ConfigureAwait(false);

        byte[] bytes = Encoding.UTF8.GetBytes("bring-forward\n");
        await client.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await client.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void ReleaseInstance()
    {
        if (_disposed)
        {
            return;
        }

        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _listenerCts = null;

        if (_ownsMutex && _mutex is not null)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex?.Dispose();
        _mutex = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseInstance();
        _disposed = true;
    }

    private static async Task ListenAsync(
        string pipeName,
        Func<CancellationToken, Task> onBringForward,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using NamedPipeServerStream server = new(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                // Read at least one byte to ensure the client did not just connect
                // and disconnect. Any content is treated as a bring-forward signal.
                byte[] buffer = new byte[64];
                int read = await server.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read > 0)
                {
                    await onBringForward(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when ReleaseInstance / Dispose cancels the listener.
        }
        catch (IOException)
        {
            // Pipe broken or other transient error; the loop ends. A production
            // app would typically log and continue, but losing bring-forward
            // signals is non-fatal.
        }
    }

    private static string ComputeCurrentUserToken()
    {
        WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? identity.Name ?? "unknown";
    }
}
