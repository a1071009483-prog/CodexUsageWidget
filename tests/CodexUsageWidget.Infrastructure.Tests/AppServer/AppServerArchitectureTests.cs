using System.Reflection;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class AppServerArchitectureTests
{
    [Fact]
    public void InfrastructureExposesTheRequiredAppServerBoundaries()
    {
        Assembly infrastructure = Assembly.Load("CodexUsageWidget.Infrastructure");
        string[] requiredTypes =
        [
            "CodexUsageWidget.Infrastructure.AppServer.JsonRpcConnection",
            "CodexUsageWidget.Infrastructure.AppServer.AppServerProcess",
            "CodexUsageWidget.Infrastructure.AppServer.CodexAppServerGateway",
            "CodexUsageWidget.Infrastructure.AppServer.CodexExecutableLocator",
            "CodexUsageWidget.Infrastructure.AppServer.AppServerCapabilityDiagnostics",
            "CodexUsageWidget.Infrastructure.AppServer.AccountAuthenticationEvaluator",
            "CodexUsageWidget.Infrastructure.AppServer.SystemProcessHost",
            "CodexUsageWidget.Infrastructure.AppServer.Protocol.AccountReadResponse",
            "CodexUsageWidget.Infrastructure.AppServer.Protocol.RateLimitsReadResponse",
            "CodexUsageWidget.Infrastructure.AppServer.Protocol.ModelListResponse",
            "CodexUsageWidget.Infrastructure.AppServer.Protocol.ThreadStartOptions",
            "CodexUsageWidget.Infrastructure.AppServer.Protocol.TurnStartOptions",
            "CodexUsageWidget.Infrastructure.AppServer.Protocol.AppServerNotificationEventArgs",
        ];

        string[] missing = requiredTypes
            .Where(typeName => infrastructure.GetType(typeName) is null)
            .ToArray();

        Assert.True(missing.Length == 0, $"Missing App Server types: {string.Join(", ", missing)}");
    }
}
