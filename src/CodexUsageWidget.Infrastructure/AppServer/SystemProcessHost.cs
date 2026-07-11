using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.AppServer;

public sealed class SystemProcessHost : IProcessHost
{
    public Task<IHostedProcess> StartAsync(
        ProcessStartRequest request,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}
