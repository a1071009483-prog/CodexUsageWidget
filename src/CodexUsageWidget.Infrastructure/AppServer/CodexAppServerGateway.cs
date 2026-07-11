using System.Text.Json;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

public sealed class CodexAppServerGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly JsonRpcConnection _connection;
    private EventHandler<RateLimitsUpdatedEventArgs>? _rateLimitsUpdated;

    public CodexAppServerGateway(JsonRpcConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _connection.NotificationReceived += OnNotificationReceived;
    }

    public event EventHandler<RateLimitsUpdatedEventArgs>? RateLimitsUpdated
    {
        add => _rateLimitsUpdated += value;
        remove => _rateLimitsUpdated -= value;
    }

    public async Task<InitializeResponse> InitializeAsync(
        ClientInformation clientInformation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientInformation);
        InitializeResponse response = await _connection.SendRequestAsync<InitializeResponse>(
                "initialize",
                new InitializeParameters(clientInformation, new InitializeCapabilities()),
                cancellationToken)
            .ConfigureAwait(false);

        await _connection.SendNotificationAsync(
                "initialized",
                null,
                CancellationToken.None)
            .ConfigureAwait(false);
        return response;
    }

    public Task<AccountReadResponse> ReadAccountAsync(
        bool refreshToken,
        CancellationToken cancellationToken) =>
        _connection.SendRequestAsync<AccountReadResponse>(
            "account/read",
            new AccountReadParameters(refreshToken),
            cancellationToken);

    public Task<RateLimitsReadResponse> ReadRateLimitsAsync(
        CancellationToken cancellationToken) =>
        _connection.SendRequestAsync<RateLimitsReadResponse>(
            "account/rateLimits/read",
            null,
            cancellationToken);

    public async Task<IReadOnlyList<ModelDescriptor>> ListAllModelsAsync(
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        var models = new List<ModelDescriptor>();
        string? cursor = null;

        do
        {
            ModelListResponse page = await _connection.SendRequestAsync<ModelListResponse>(
                    "model/list",
                    new ModelListParameters(cursor, includeHidden),
                    cancellationToken)
                .ConfigureAwait(false);
            models.AddRange(page.Data);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return models;
    }

    public Task<ThreadStartResponse> StartThreadAsync(
        ThreadStartOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _connection.SendRequestAsync<ThreadStartResponse>(
            "thread/start",
            options,
            cancellationToken);
    }

    public Task<TurnStartResponse> StartTurnAsync(
        TurnStartOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _connection.SendRequestAsync<TurnStartResponse>(
            "turn/start",
            options,
            cancellationToken);
    }

    public async Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        _ = await _connection.SendRequestAsync<AppServerEmptyResponse>(
                "turn/interrupt",
                new TurnInterruptParameters(threadId, turnId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteThreadAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _ = await _connection.SendRequestAsync<AppServerEmptyResponse>(
                "thread/delete",
                new ThreadDeleteParameters(threadId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private void OnNotificationReceived(
        object? sender,
        AppServerNotificationEventArgs eventArgs)
    {
        _ = sender;
        if (!string.Equals(
                eventArgs.Method,
                "account/rateLimits/updated",
                StringComparison.Ordinal)
            || eventArgs.Parameters is not JsonElement parameters)
        {
            return;
        }

        try
        {
            RateLimitsUpdatedParameters? update =
                parameters.Deserialize<RateLimitsUpdatedParameters>(SerializerOptions);
            if (update is not null)
            {
                _rateLimitsUpdated?.Invoke(
                    this,
                    new RateLimitsUpdatedEventArgs(update.RateLimits));
            }
        }
        catch (JsonException)
        {
            // Invalid notifications are ignored without retaining or exposing their contents.
        }
    }
}
