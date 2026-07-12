using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

public sealed class JsonRpcConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly IRedactingLog? _log;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private EventHandler<AppServerNotificationEventArgs>? _notificationReceived;
    private EventHandler<AppServerRequestEventArgs>? _serverRequestReceived;
    private Task? _readLoop;
    private long _nextRequestId;
    private int _started;
    private int _disposed;

    public JsonRpcConnection(TextReader input, TextWriter output, IRedactingLog? log = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _log = log;
    }

    public event EventHandler<AppServerNotificationEventArgs>? NotificationReceived
    {
        add => _notificationReceived += value;
        remove => _notificationReceived -= value;
    }

    public event EventHandler<AppServerRequestEventArgs>? ServerRequestReceived
    {
        add => _serverRequestReceived += value;
        remove => _serverRequestReceived -= value;
    }

    public Task Completion => _completion.Task;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The JSON-RPC connection has already been started.");
        }

        _readLoop = ReadLoopAsync();
        return Task.CompletedTask;
    }

    public async Task<TResult> SendRequestAsync<TResult>(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        EnsureUsable();
        cancellationToken.ThrowIfCancellationRequested();

        long id = Interlocked.Increment(ref _nextRequestId);
        var response = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, response))
        {
            throw new InvalidOperationException("A duplicate JSON-RPC request id was generated.");
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => CancelPendingRequest(id, response, cancellationToken));

        try
        {
            await WriteMessageAsync(
                    new { id, method, @params = parameters },
                    cancellationToken)
                .ConfigureAwait(false);

            JsonElement result = await response.Task.ConfigureAwait(false);
            try
            {
                TResult? value = result.Deserialize<TResult>(SerializerOptions);
                if (value is null)
                {
                    throw new JsonException("The response result was null.");
                }

                return value!;
            }
            catch (JsonException exception)
            {
                throw Malformed("The App Server response result was malformed.", exception);
            }
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async ValueTask SendNotificationAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        EnsureUsable();
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is null)
        {
            await WriteMessageAsync(new { method }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteMessageAsync(
                new { method, @params = parameters },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        FailPending(new AppServerProtocolException(
            AppServerProtocolErrorKind.Disconnected,
            "The App Server connection was closed."));

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
            }
        }
        else
        {
            _completion.TrySetResult();
        }

        await _writeGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _writeGate.Release();
        _shutdown.Dispose();
        _writeGate.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                string? line = await _input.ReadLineAsync(_shutdown.Token).ConfigureAwait(false);
                if (line is null)
                {
                    throw new AppServerProtocolException(
                        AppServerProtocolErrorKind.Disconnected,
                        "The App Server connection ended unexpectedly.");
                }

                await ProcessMessageAsync(line).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            _completion.TrySetResult();
        }
        catch (AppServerProtocolException exception)
        {
            FailPending(exception);
            _completion.TrySetException(exception);
        }
        catch (Exception exception)
        {
            var disconnected = new AppServerProtocolException(
                AppServerProtocolErrorKind.Disconnected,
                "The App Server connection failed.",
                innerException: exception);
            FailPending(disconnected);
            _completion.TrySetException(disconnected);
        }
    }

    private async ValueTask ProcessMessageAsync(string line)
    {
        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw Malformed("The App Server sent malformed JSON.", exception);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Malformed("The App Server sent a malformed message.");
        }

        bool hasMethod = root.TryGetProperty("method", out JsonElement methodElement);
        bool hasId = root.TryGetProperty("id", out JsonElement idElement);

        if (hasMethod)
        {
            if (methodElement.ValueKind != JsonValueKind.String)
            {
                throw Malformed("The App Server sent a malformed method.");
            }

            string method = methodElement.GetString()!;
            JsonElement? parameters = root.TryGetProperty("params", out JsonElement parameterElement)
                ? parameterElement.Clone()
                : null;

            if (hasId)
            {
                if (!IsValidIncomingId(idElement))
                {
                    throw Malformed("The App Server sent a malformed request id.");
                }

                try
                {
                    _serverRequestReceived?.Invoke(
                        this,
                        new AppServerRequestEventArgs(idElement.Clone(), method, parameters));
                }
                catch (Exception ex)
                {
                    // A subscriber must not be able to kill the read loop and
                    // fault the entire connection.
                    await LogHandlerExceptionAsync(
                        "ServerRequestHandlerFailed",
                        method,
                        ex).ConfigureAwait(false);
                }
            }
            else
            {
                try
                {
                    _notificationReceived?.Invoke(
                        this,
                        new AppServerNotificationEventArgs(method, parameters));
                }
                catch (Exception ex)
                {
                    await LogHandlerExceptionAsync(
                        "NotificationHandlerFailed",
                        method,
                        ex).ConfigureAwait(false);
                }
            }

            return;
        }

        if (!hasId)
        {
            throw Malformed("The App Server sent an unrecognized message.");
        }

        if (!IsValidIncomingId(idElement))
        {
            throw Malformed("The App Server sent a malformed response id.");
        }

        if (!TryReadNumericId(idElement, out long id))
        {
            return;
        }

        bool hasResult = root.TryGetProperty("result", out JsonElement result);
        bool hasError = root.TryGetProperty("error", out JsonElement error);
        if (hasResult == hasError)
        {
            throw Malformed("The App Server sent a malformed response.");
        }

        AppServerProtocolException? remoteError = hasError ? ReadRemoteError(error) : null;
        if (!_pending.TryRemove(id, out TaskCompletionSource<JsonElement>? pending))
        {
            return;
        }

        if (hasResult)
        {
            pending.TrySetResult(result.Clone());
            return;
        }

        pending.TrySetException(remoteError!);
    }

    private async ValueTask LogHandlerExceptionAsync(
        string eventName,
        string method,
        Exception ex)
    {
        if (_log is null)
        {
            return;
        }

        await _log.WriteAsync(
            new StructuredLogEvent(
                RedactingLogLevel.Warning,
                eventName,
                new Dictionary<string, string?>
                {
                    ["method"] = method,
                    ["exceptionType"] = ex.GetType().FullName,
                }),
            CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        string line = JsonSerializer.Serialize(message, SerializerOptions);
        using var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        CancellationToken writeToken = writeCancellation.Token;

        await _writeGate.WaitAsync(writeToken).ConfigureAwait(false);
        try
        {
            EnsureUsable();
            await _output.WriteLineAsync(line.AsMemory(), writeToken).ConfigureAwait(false);
            await _output.FlushAsync(writeToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _started) == 0)
        {
            throw new InvalidOperationException("The JSON-RPC connection has not been started.");
        }

        if (_completion.Task.IsCompleted)
        {
            throw new InvalidOperationException("The JSON-RPC connection is no longer available.");
        }
    }

    private void CancelPendingRequest(
        long id,
        TaskCompletionSource<JsonElement> response,
        CancellationToken cancellationToken)
    {
        if (_pending.TryRemove(id, out _))
        {
            response.TrySetCanceled(cancellationToken);
        }
    }

    private void FailPending(Exception exception)
    {
        foreach ((long id, TaskCompletionSource<JsonElement> pending) in _pending)
        {
            if (_pending.TryRemove(id, out _))
            {
                pending.TrySetException(exception);
            }
        }
    }

    private static AppServerProtocolException ReadRemoteError(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out JsonElement codeElement)
            || !codeElement.TryGetInt64(out long code))
        {
            throw Malformed("The App Server sent a malformed error response.");
        }

        AppServerProtocolErrorKind kind = code == -32601
            ? AppServerProtocolErrorKind.MethodNotFound
            : AppServerProtocolErrorKind.RemoteError;
        return new AppServerProtocolException(
            kind,
            "The App Server returned a redacted protocol error.",
            code);
    }

    private static bool TryReadNumericId(JsonElement idElement, out long id)
    {
        if (idElement.ValueKind == JsonValueKind.Number)
        {
            return idElement.TryGetInt64(out id);
        }

        if (idElement.ValueKind == JsonValueKind.String)
        {
            return long.TryParse(
                idElement.GetString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out id);
        }

        id = default;
        return false;
    }

    private static bool IsValidIncomingId(JsonElement idElement) =>
        idElement.ValueKind == JsonValueKind.String
        || (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out _));

    private static AppServerProtocolException Malformed(
        string message,
        Exception? innerException = null) =>
        new(
            AppServerProtocolErrorKind.MalformedMessage,
            message,
            innerException: innerException);
}
