using System.Text;
using System.Text.Json;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class JsonRpcConnectionTests
{
    [Fact]
    public async Task CorrelatesOutOfOrderResponsesAndIgnoresUnknownFields()
    {
        var transport = new AsyncLineTransport();
        await using var connection = new JsonRpcConnection(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);

        Task<JsonElement> first = connection.SendRequestAsync<JsonElement>(
            "account/read",
            new { refreshToken = false },
            CancellationToken.None);
        Task<JsonElement> second = connection.SendRequestAsync<JsonElement>(
            "account/rateLimits/read",
            null,
            CancellationToken.None);

        JsonElement firstRequest = Parse(await transport.ClientInput.ReadLineAsync());
        JsonElement secondRequest = Parse(await transport.ClientInput.ReadLineAsync());
        long firstId = firstRequest.GetProperty("id").GetInt64();
        long secondId = secondRequest.GetProperty("id").GetInt64();

        transport.ServerOutput.WriteLine(
            Success(secondId, "{\"value\":\"second\",\"future\":true}"));
        transport.ServerOutput.WriteLine(
            Success(firstId, "{\"value\":\"first\"}"));

        Assert.Equal("first", (await first).GetProperty("value").GetString());
        Assert.Equal("second", (await second).GetProperty("value").GetString());
    }

    [Fact]
    public async Task DispatchesNotificationsAndRejectsLateCancelledResponses()
    {
        var transport = new AsyncLineTransport();
        await using var connection = new JsonRpcConnection(
            transport.ServerOutput,
            transport.ClientInput);
        var notification = new TaskCompletionSource<AppServerNotificationEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.NotificationReceived += (_, args) => notification.TrySetResult(args);
        await connection.StartAsync(CancellationToken.None);

        transport.ServerOutput.WriteLine(
            "{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"primary\":{\"usedPercent\":4}}}}");
        AppServerNotificationEventArgs received = await notification.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        Assert.Equal("account/rateLimits/updated", received.Method);

        using var cancelled = new CancellationTokenSource();
        Task<JsonElement> cancelledRequest = connection.SendRequestAsync<JsonElement>(
            "account/read",
            new { },
            cancelled.Token);
        JsonElement outbound = Parse(await transport.ClientInput.ReadLineAsync());
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledRequest);

        transport.ServerOutput.WriteLine(
            Success(outbound.GetProperty("id").GetInt64(), "{\"obsolete\":true}"));

        Task<JsonElement> current = connection.SendRequestAsync<JsonElement>(
            "account/read",
            new { },
            CancellationToken.None);
        JsonElement currentOutbound = Parse(await transport.ClientInput.ReadLineAsync());
        transport.ServerOutput.WriteLine(
            Success(currentOutbound.GetProperty("id").GetInt64(), "{\"current\":true}"));
        Assert.True((await current).GetProperty("current").GetBoolean());
    }

    [Theory]
    [InlineData(-32601, AppServerProtocolErrorKind.MethodNotFound)]
    [InlineData(-32000, AppServerProtocolErrorKind.RemoteError)]
    public async Task ClassifiesRemoteErrors(long code, AppServerProtocolErrorKind expectedKind)
    {
        var transport = new AsyncLineTransport();
        await using var connection = new JsonRpcConnection(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);

        Task<JsonElement> pending = connection.SendRequestAsync<JsonElement>(
            "unknown",
            null,
            CancellationToken.None);
        JsonElement outbound = Parse(await transport.ClientInput.ReadLineAsync());
        transport.ServerOutput.WriteLine(
            Error(outbound.GetProperty("id").GetInt64(), code));

        AppServerProtocolException exception = await Assert.ThrowsAsync<AppServerProtocolException>(
            () => pending);
        Assert.Equal(expectedKind, exception.Kind);
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task MalformedInputFaultsTheConnectionAndPendingRequests()
    {
        var transport = new AsyncLineTransport();
        await using var connection = new JsonRpcConnection(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);

        Task<JsonElement> pending = connection.SendRequestAsync<JsonElement>(
            "account/read",
            null,
            CancellationToken.None);
        _ = await transport.ClientInput.ReadLineAsync();
        transport.ServerOutput.WriteLine("not-json");

        AppServerProtocolException exception = await Assert.ThrowsAsync<AppServerProtocolException>(
            () => pending);
        Assert.Equal(AppServerProtocolErrorKind.MalformedMessage, exception.Kind);
        await Assert.ThrowsAsync<AppServerProtocolException>(() => connection.Completion);
    }

    [Fact]
    public async Task InvalidResponseIdFaultsTheConnectionAndPendingRequests()
    {
        var transport = new AsyncLineTransport();
        await using var connection = new JsonRpcConnection(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);

        Task<JsonElement> pending = connection.SendRequestAsync<JsonElement>(
            "account/read",
            null,
            CancellationToken.None);
        _ = await transport.ClientInput.ReadLineAsync();
        transport.ServerOutput.WriteLine("{\"id\":true,\"result\":{}}");

        AppServerProtocolException exception = await Assert.ThrowsAsync<AppServerProtocolException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(AppServerProtocolErrorKind.MalformedMessage, exception.Kind);
        await Assert.ThrowsAsync<AppServerProtocolException>(() => connection.Completion);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"id\":1,\"result\":{},\"error\":{\"code\":-1,\"message\":\"m\"}}")]
    [InlineData("{\"id\":1}")]
    [InlineData("{\"id\":{\"x\":1},\"result\":{}}")]
    [InlineData("{\"method\":123}")]
    public async Task StructurallyMalformedFramesFaultTheConnectionAndPendingRequests(string frame)
    {
        var transport = new AsyncLineTransport();
        await using var connection = new JsonRpcConnection(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);

        Task<JsonElement> pending = connection.SendRequestAsync<JsonElement>(
            "account/read",
            null,
            CancellationToken.None);
        _ = await transport.ClientInput.ReadLineAsync();
        transport.ServerOutput.WriteLine(frame);

        AppServerProtocolException pendingException =
            await Assert.ThrowsAsync<AppServerProtocolException>(
                () => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(AppServerProtocolErrorKind.MalformedMessage, pendingException.Kind);

        AppServerProtocolException completionException =
            await Assert.ThrowsAsync<AppServerProtocolException>(
                () => connection.Completion);
        Assert.Equal(AppServerProtocolErrorKind.MalformedMessage, completionException.Kind);
    }

    [Fact]
    public async Task DisposalCompletesRequestsWithActiveAndQueuedWrites()
    {
        var input = new ChannelLineReader();
        var output = new CancellationBlockingLineWriter();
        var connection = new JsonRpcConnection(input, output);
        await connection.StartAsync(CancellationToken.None);

        Task<JsonElement> active = connection.SendRequestAsync<JsonElement>(
            "account/read",
            null,
            CancellationToken.None);
        await output.WriteStarted.WaitAsync(TimeSpan.FromSeconds(2));
        Task<JsonElement> queued = connection.SendRequestAsync<JsonElement>(
            "account/rateLimits/read",
            null,
            CancellationToken.None);

        await connection.DisposeAsync();

        Task settled = Task.WhenAll(ObserveCompletion(active), ObserveCompletion(queued));
        await settled.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(active.IsCompleted);
        Assert.True(queued.IsCompleted);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Success(long id, string resultJson) => JsonSerializer.Serialize(new
    {
        id,
        result = Parse(resultJson),
        extra = "ignored",
    });

    private static string Error(long id, long code) => JsonSerializer.Serialize(new
    {
        id,
        error = new
        {
            code,
            message = "failure",
            data = new { category = "test" },
        },
    });

    private static Task ObserveCompletion(Task task) =>
        task.ContinueWith(
            static _ => { },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private sealed class CancellationBlockingLineWriter : TextWriter
    {
        private readonly TaskCompletionSource _writeStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override Encoding Encoding => Encoding.UTF8;

        public Task WriteStarted => _writeStarted.Task;

        public override async Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            _ = buffer;
            _writeStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
