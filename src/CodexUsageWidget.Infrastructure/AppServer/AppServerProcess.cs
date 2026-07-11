using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.AppServer;

public sealed class AppServerProcess : IAsyncDisposable
{
    public AppServerProcess(
        IProcessHost processHost,
        ProcessStartRequest startRequest,
        IRedactingLog? log = null)
    {
        _ = processHost;
        _ = startRequest;
        _ = log;
    }

    public Task<CodexAppServerGateway> StartAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task StopAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
