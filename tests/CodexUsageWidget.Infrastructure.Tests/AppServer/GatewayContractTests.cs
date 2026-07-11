using System.Text;
using System.Text.Json;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class GatewayContractTests
{
    [Fact]
    public async Task PerformsInitializeHandshakeAndPublishesRateLimitNotifications()
    {
        var transport = new AsyncLineTransport();
        await using var connection = new JsonRpcConnection(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);
        var gateway = new CodexAppServerGateway(connection);

        Task<InitializeResponse> initialization = gateway.InitializeAsync(
            new ClientInformation("codex-usage-widget", "1.0.0", "Codex Usage Widget"),
            CancellationToken.None);
        JsonElement initializeRequest = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("initialize", initializeRequest.GetProperty("method").GetString());
        Assert.True(
            initializeRequest.GetProperty("params").GetProperty("capabilities")
                .GetProperty("experimentalApi").GetBoolean());
        Respond(
            transport,
            initializeRequest,
            "{\"codexHome\":\"C:\\\\Codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\",\"userAgent\":\"test\"}");
        _ = await initialization;

        JsonElement initialized = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("initialized", initialized.GetProperty("method").GetString());
        Assert.False(initialized.TryGetProperty("id", out _));

        var update = new TaskCompletionSource<RateLimitsUpdatedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.RateLimitsUpdated += (_, args) => update.TrySetResult(args);
        transport.ServerOutput.WriteLine(
            "{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":7,\"windowDurationMins\":300}}}}");
        RateLimitsUpdatedEventArgs args = await update.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(7, args.RateLimits.Primary?.UsedPercent);
    }

    [Fact]
    public async Task SendsInitializedAfterSuccessfulResponseEvenIfCallerCancels()
    {
        var serverOutput = new ChannelLineReader();
        using var cancelled = new CancellationTokenSource();
        var clientInput = new CancelOnSecondWriteLineWriter(cancelled);
        await using var connection = new JsonRpcConnection(serverOutput, clientInput);
        await connection.StartAsync(CancellationToken.None);
        var gateway = new CodexAppServerGateway(connection);

        Task<InitializeResponse> initialization = gateway.InitializeAsync(
            new ClientInformation("codex-usage-widget", "1.0.0"),
            cancelled.Token);
        JsonElement request = Parse(await clientInput.ReadLineAsync());
        serverOutput.WriteLine(JsonSerializer.Serialize(new
        {
            id = request.GetProperty("id").GetInt64(),
            result = Parse(
                "{\"codexHome\":\"C:\\\\Codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\",\"userAgent\":\"test\"}"),
        }));

        _ = await initialization;
        JsonElement initialized = Parse(await clientInput.ReadLineAsync());
        Assert.True(cancelled.IsCancellationRequested);
        Assert.Equal("initialized", initialized.GetProperty("method").GetString());
        Assert.False(initialized.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task UsesExactTypedMethodsAndPaginatesModels()
    {
        var transport = new AsyncLineTransport();
        await using var connection = new JsonRpcConnection(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);
        var gateway = new CodexAppServerGateway(connection);

        Task<AccountReadResponse> account = gateway.ReadAccountAsync(false, CancellationToken.None);
        JsonElement accountRequest = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("account/read", accountRequest.GetProperty("method").GetString());
        Respond(
            transport,
            accountRequest,
            "{\"requiresOpenaiAuth\":true,\"account\":{\"type\":\"chatgpt\",\"email\":null,\"planType\":\"plus\"}}");
        Assert.Equal("chatgpt", (await account).Account?.Type);

        Task<RateLimitsReadResponse> limits = gateway.ReadRateLimitsAsync(CancellationToken.None);
        JsonElement limitsRequest = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("account/rateLimits/read", limitsRequest.GetProperty("method").GetString());
        Respond(
            transport,
            limitsRequest,
            "{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"resetsAt\":1770000000,\"windowDurationMins\":300}}}");
        Assert.Equal(300, (await limits).RateLimits.Primary?.WindowDurationMins);

        Task<IReadOnlyList<ModelDescriptor>> models = gateway.ListAllModelsAsync(
            true,
            CancellationToken.None);
        JsonElement firstPage = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("model/list", firstPage.GetProperty("method").GetString());
        Assert.True(firstPage.GetProperty("params").GetProperty("includeHidden").GetBoolean());
        Respond(transport, firstPage, ModelPage("gpt-mini", "next"));

        JsonElement secondPage = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("next", secondPage.GetProperty("params").GetProperty("cursor").GetString());
        Respond(transport, secondPage, ModelPage("gpt-default", null, isDefault: true));

        IReadOnlyList<ModelDescriptor> allModels = await models;
        Assert.Collection(
            allModels,
            model => Assert.Equal("gpt-mini", model.Id),
            model => Assert.True(model.IsDefault));
    }

    [Fact]
    public async Task SendsThreadTurnInterruptAndDeleteContracts()
    {
        var transport = new AsyncLineTransport();
        await using var connection = new JsonRpcConnection(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);
        var gateway = new CodexAppServerGateway(connection);

        Task<ThreadStartResponse> thread = gateway.StartThreadAsync(
            new ThreadStartOptions
            {
                Model = "gpt-mini",
                WorkingDirectory = @"C:\empty",
                Ephemeral = false,
            },
            CancellationToken.None);
        JsonElement threadRequest = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("thread/start", threadRequest.GetProperty("method").GetString());
        JsonElement threadParams = threadRequest.GetProperty("params");
        Assert.Equal("never", threadParams.GetProperty("approvalPolicy").GetString());
        Assert.Equal("read-only", threadParams.GetProperty("sandbox").GetString());
        Assert.Equal(0, threadParams.GetProperty("dynamicTools").GetArrayLength());
        Respond(
            transport,
            threadRequest,
            "{\"model\":\"gpt-mini\",\"thread\":{\"id\":\"thread-1\",\"ephemeral\":false}}");
        Assert.Equal("thread-1", (await thread).Thread.Id);

        using JsonDocument sandbox = JsonDocument.Parse("{\"type\":\"readOnly\",\"networkAccess\":false}");
        Task<TurnStartResponse> turn = gateway.StartTurnAsync(
            new TurnStartOptions
            {
                ThreadId = "thread-1",
                Input = [new TextUserInput("text", "OK", [])],
                Effort = "low",
                SandboxPolicy = sandbox.RootElement.Clone(),
            },
            CancellationToken.None);
        JsonElement turnRequest = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("turn/start", turnRequest.GetProperty("method").GetString());
        Assert.Equal("none", turnRequest.GetProperty("params").GetProperty("summary").GetString());
        Respond(
            transport,
            turnRequest,
            "{\"turn\":{\"id\":\"turn-1\",\"status\":\"inProgress\",\"items\":[]}}");
        Assert.Equal("turn-1", (await turn).Turn.Id);

        Task interrupt = gateway.InterruptTurnAsync("thread-1", "turn-1", CancellationToken.None);
        JsonElement interruptRequest = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("turn/interrupt", interruptRequest.GetProperty("method").GetString());
        Respond(transport, interruptRequest, "{}");
        await interrupt;

        Task delete = gateway.DeleteThreadAsync("thread-1", CancellationToken.None);
        JsonElement deleteRequest = Parse(await transport.ClientInput.ReadLineAsync());
        Assert.Equal("thread/delete", deleteRequest.GetProperty("method").GetString());
        Respond(transport, deleteRequest, "{}");
        await delete;
    }

    private static void Respond(AsyncLineTransport transport, JsonElement request, string resultJson) =>
        transport.ServerOutput.WriteLine(JsonSerializer.Serialize(new
        {
            id = request.GetProperty("id").GetInt64(),
            result = Parse(resultJson),
        }));

    private static string ModelPage(string id, string? nextCursor, bool isDefault = false)
    {
        return JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new
                {
                    id,
                    model = id,
                    displayName = id,
                    description = "test",
                    hidden = false,
                    isDefault,
                    defaultReasoningEffort = "low",
                    supportedReasoningEfforts = new[]
                    {
                        new { reasoningEffort = "low", description = "Low" },
                    },
                },
            },
            nextCursor,
        });
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class CancelOnSecondWriteLineWriter : TextWriter
    {
        private readonly ChannelLineWriter _inner = new();
        private readonly CancellationTokenSource _cancellation;
        private int _writeCount;

        public CancelOnSecondWriteLineWriter(CancellationTokenSource cancellation) =>
            _cancellation = cancellation;

        public override Encoding Encoding => Encoding.UTF8;

        public Task<string> ReadLineAsync(CancellationToken cancellationToken = default) =>
            _inner.ReadLineAsync(cancellationToken);

        public override Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _writeCount) == 2)
            {
                _cancellation.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return _inner.WriteLineAsync(buffer, cancellationToken);
        }
    }
}
