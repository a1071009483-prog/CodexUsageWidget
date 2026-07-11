using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

/// <summary>
/// Exercises the real fake App Server executable through SystemProcessHost and
/// AppServerProcess so that a structurally malformed frame is proven to fault the
/// session Completion via the full process/stdio/session path, not only in-memory.
/// </summary>
public sealed class AppServerProcessContractTests
{
    private static string FakeAppServerDllPath =>
        Path.Combine(AppContext.BaseDirectory, "FakeCodexAppServer.dll");

    private static string DotnetHostPath =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    [Fact]
    public async Task StructurallyMalformedFrameFromRealProcessFaultsSessionCompletion()
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"app-server-malformed-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                {
                  "steps": [
                    {
                      "expect": "request",
                      "method": "initialize",
                      "numericId": true,
                      "result": { "codexHome": "C:\\Codex", "platformFamily": "windows", "platformOs": "windows", "userAgent": "fake" }
                    },
                    { "expect": "notification", "method": "initialized" },
                    { "emitRaw": "{\"id\":1,\"result\":{},\"error\":{\"code\":-1,\"message\":\"m\"}}" },
                    { "waitForEof": true },
                    { "exit": 0 }
                  ]
                }
                """);

            var request = new ProcessStartRequest(
                DotnetHostPath,
                [FakeAppServerDllPath, scriptPath]);

            var processHost = new SystemProcessHost();
            var appServer = new AppServerProcess(
                processHost,
                request,
                new ClientInformation("widget-tests", "1.0", null),
                TimeSpan.FromSeconds(2));

            AppServerSession session = await appServer.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));

            AppServerProtocolException exception = await Assert.ThrowsAsync<
                AppServerProtocolException>(() => session.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(AppServerProtocolErrorKind.MalformedMessage, exception.Kind);

            await appServer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }
}
