using System.Text;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class AppServerModelBoundaryTests
{
    [Fact]
    public async Task StartGenerationStartsEphemeralThreadAndLowEffortTurn()
    {
        AsyncLineTransport transport = new();
        await using JsonRpcConnection connection = new(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);

        AppServerModelBoundary boundary = new(new CodexAppServerGateway(connection));
        ModelGenerationRequest request = new(
            AttemptId: "attempt-1",
            ModelId: "gpt-4o-mini",
            Prompt: "OK",
            WorkingDirectory: @"C:\test",
            Timeout: TimeSpan.FromSeconds(10));

        Task<ModelGenerationResult> generation = boundary.StartGenerationAsync(
            request,
            CancellationToken.None);

        JsonElement threadRequest = await ReadRequestAsync(transport.ClientInput);
        Assert.Equal("thread/start", threadRequest.GetProperty("method").GetString());
        JsonElement threadParams = threadRequest.GetProperty("params");
        Assert.Equal("gpt-4o-mini", threadParams.GetProperty("model").GetString());
        Assert.Equal(@"C:\test", threadParams.GetProperty("cwd").GetString());
        Assert.True(threadParams.GetProperty("ephemeral").GetBoolean());
        Assert.Equal("never", threadParams.GetProperty("approvalPolicy").GetString());
        Assert.Equal("read-only", threadParams.GetProperty("sandbox").GetString());
        Assert.Equal(0, threadParams.GetProperty("dynamicTools").GetArrayLength());
        Assert.Equal(0, threadParams.GetProperty("environments").GetArrayLength());
        Respond(transport, threadRequest, "{\"model\":\"gpt-4o-mini\",\"thread\":{\"id\":\"thread-1\",\"ephemeral\":true}}");

        JsonElement turnRequest = await ReadRequestAsync(transport.ClientInput);
        Assert.Equal("turn/start", turnRequest.GetProperty("method").GetString());
        JsonElement turnParams = turnRequest.GetProperty("params");
        Assert.Equal("thread-1", turnParams.GetProperty("threadId").GetString());
        Assert.Equal("OK", turnParams.GetProperty("input")[0].GetProperty("text").GetString());
        Assert.Equal("text", turnParams.GetProperty("input")[0].GetProperty("type").GetString());
        Assert.Equal("low", turnParams.GetProperty("effort").GetString());
        Assert.Equal("never", turnParams.GetProperty("approvalPolicy").GetString());
        Assert.Equal("none", turnParams.GetProperty("summary").GetString());
        Respond(transport, turnRequest, "{\"turn\":{\"id\":\"turn-1\",\"status\":\"inProgress\",\"items\":[]}}");

        ModelGenerationResult result = await generation;

        Assert.True(result.WasAccepted);
        Assert.True(result.GenerationStarted);
        Assert.Equal("thread-1", result.ThreadId);
        Assert.Equal("turn-1", result.TurnId);
    }

    [Fact]
    public async Task StartGenerationReturnsModelUnavailableOnMethodNotFound()
    {
        AsyncLineTransport transport = new();
        await using JsonRpcConnection connection = new(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);

        AppServerModelBoundary boundary = new(new CodexAppServerGateway(connection));
        ModelGenerationRequest request = new(
            AttemptId: "attempt-2",
            ModelId: "unknown-model",
            Prompt: "OK",
            WorkingDirectory: string.Empty,
            Timeout: TimeSpan.FromSeconds(10));

        Task<ModelGenerationResult> generation = boundary.StartGenerationAsync(
            request,
            CancellationToken.None);

        JsonElement threadRequest = await ReadRequestAsync(transport.ClientInput);
        RespondWithError(transport, threadRequest, -32601, "Method not found");

        ModelGenerationResult result = await generation;

        Assert.False(result.WasAccepted);
        Assert.False(result.GenerationStarted);
        Assert.Equal("model-unavailable", result.FailureCategory);
    }

    [Fact]
    public async Task InterruptTurnAndDeleteThreadForwardToGateway()
    {
        AsyncLineTransport transport = new();
        await using JsonRpcConnection connection = new(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);

        AppServerModelBoundary boundary = new(new CodexAppServerGateway(connection));

        Task interrupt = boundary.InterruptTurnAsync("thread-2", "turn-2", CancellationToken.None);
        JsonElement interruptRequest = await ReadRequestAsync(transport.ClientInput);
        Assert.Equal("turn/interrupt", interruptRequest.GetProperty("method").GetString());
        Respond(transport, interruptRequest, "{}");
        await interrupt;

        Task delete = boundary.DeleteThreadAsync("thread-2", CancellationToken.None);
        JsonElement deleteRequest = await ReadRequestAsync(transport.ClientInput);
        Assert.Equal("thread/delete", deleteRequest.GetProperty("method").GetString());
        Respond(transport, deleteRequest, "{}");
        await delete;
    }

    [Fact]
    public async Task StartGenerationPropagatesCallerCancellation()
    {
        AsyncLineTransport transport = new();
        await using JsonRpcConnection connection = new(
            transport.ServerOutput,
            transport.ClientInput);
        await connection.StartAsync(CancellationToken.None);

        AppServerModelBoundary boundary = new(new CodexAppServerGateway(connection));
        ModelGenerationRequest request = new(
            AttemptId: "attempt-cancel",
            ModelId: "gpt-4o-mini",
            Prompt: "OK",
            WorkingDirectory: string.Empty,
            Timeout: TimeSpan.FromSeconds(10));

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Task<ModelGenerationResult> generation = boundary.StartGenerationAsync(
            request,
            cancellationTokenSource.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => generation);
    }

    private static async Task<JsonElement> ReadRequestAsync(ChannelLineWriter input) =>
        JsonDocument.Parse(await input.ReadLineAsync(CancellationToken.None)).RootElement.Clone();

    private static void Respond(AsyncLineTransport transport, JsonElement request, string resultJson) =>
        transport.ServerOutput.WriteLine(JsonSerializer.Serialize(new
        {
            id = request.GetProperty("id").GetInt64(),
            result = JsonDocument.Parse(resultJson).RootElement.Clone(),
        }));

    private static void RespondWithError(
        AsyncLineTransport transport,
        JsonElement request,
        long code,
        string message) =>
        transport.ServerOutput.WriteLine(JsonSerializer.Serialize(new
        {
            id = request.GetProperty("id").GetInt64(),
            error = new { code, message },
        }));
}
