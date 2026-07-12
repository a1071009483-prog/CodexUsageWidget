using System.Text;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;
using CodexUsageWidget.Infrastructure.Time;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class AppServerCapabilityPreflightTests
{
    [Fact]
    public async Task PreflightReturnsCompatibleWhenCapabilitiesSchemaAdvertisesAllRequiredMethods()
    {
        AsyncLineTransport transport = new();
        FakeProcessHost host = new(transport);
        Func<CancellationToken, Task<AppServerCapabilityResult>> preflight =
            AppServerCapabilityPreflight.ForProcess(
                host,
                new ProcessStartRequest("codex", ["app-server"]),
                new ClientInformation("test", "1.0.0"),
                TimeSpan.FromMilliseconds(10),
                new TaskDelay());

        Task<AppServerCapabilityResult> resultTask = preflight(CancellationToken.None);

        JsonElement initRequest = await ReadRequestAsync(transport.ClientInput);
        Respond(
            transport,
            initRequest,
            """
            {
              "codexHome": "C:\\Codex",
              "platformFamily": "windows",
              "platformOs": "windows",
              "userAgent": "fake",
              "capabilities": {
                "oneOf": [
                  {"properties": {"method": {"enum": ["initialize"]}}},
                  {"properties": {"method": {"enum": ["account/read"]}}},
                  {"properties": {"method": {"enum": ["account/rateLimits/read"]}}},
                  {"properties": {"method": {"enum": ["model/list"]}}},
                  {"properties": {"method": {"enum": ["thread/start"]}}},
                  {"properties": {"method": {"enum": ["turn/start"]}}},
                  {"properties": {"method": {"enum": ["turn/interrupt"]}}},
                  {"properties": {"method": {"enum": ["thread/delete"]}}}
                ]
              }
            }
            """);

        AppServerCapabilityResult result = await resultTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.IsCompatible);
        Assert.Empty(result.MissingMethods);
    }

    [Fact]
    public async Task PreflightReturnsIncompatibleWhenCapabilitiesSchemaIsMissingARequiredMethod()
    {
        AsyncLineTransport transport = new();
        FakeProcessHost host = new(transport);
        Func<CancellationToken, Task<AppServerCapabilityResult>> preflight =
            AppServerCapabilityPreflight.ForProcess(
                host,
                new ProcessStartRequest("codex", ["app-server"]),
                new ClientInformation("test", "1.0.0"),
                TimeSpan.FromMilliseconds(10),
                new TaskDelay());

        Task<AppServerCapabilityResult> resultTask = preflight(CancellationToken.None);

        JsonElement initRequest = await ReadRequestAsync(transport.ClientInput);
        Respond(
            transport,
            initRequest,
            """
            {
              "codexHome": "C:\\Codex",
              "platformFamily": "windows",
              "platformOs": "windows",
              "userAgent": "fake",
              "capabilities": {
                "oneOf": [
                  {"properties": {"method": {"enum": ["initialize"]}}},
                  {"properties": {"method": {"enum": ["account/read"]}}}
                ]
              }
            }
            """);

        AppServerCapabilityResult result = await resultTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.IsCompatible);
        Assert.Contains("model/list", result.MissingMethods);
    }

    [Fact]
    public async Task PreflightReturnsIncompatibleWhenProcessFailsToStart()
    {
        FailingProcessHost host = new();
        Func<CancellationToken, Task<AppServerCapabilityResult>> preflight =
            AppServerCapabilityPreflight.ForProcess(
                host,
                new ProcessStartRequest("codex", ["app-server"]),
                new ClientInformation("test", "1.0.0"),
                TimeSpan.FromMilliseconds(10),
                new TaskDelay());

        AppServerCapabilityResult result = await preflight(CancellationToken.None);

        Assert.False(result.IsCompatible);
        Assert.NotEmpty(result.MissingMethods);
    }

    [Fact]
    public async Task PreflightReturnsCompatibleWhenServerOmitsCapabilitiesSchema()
    {
        AsyncLineTransport transport = new();
        FakeProcessHost host = new(transport);
        Func<CancellationToken, Task<AppServerCapabilityResult>> preflight =
            AppServerCapabilityPreflight.ForProcess(
                host,
                new ProcessStartRequest("codex", ["app-server"]),
                new ClientInformation("test", "1.0.0"),
                TimeSpan.FromMilliseconds(10),
                new TaskDelay());

        Task<AppServerCapabilityResult> resultTask = preflight(CancellationToken.None);

        JsonElement initRequest = await ReadRequestAsync(transport.ClientInput);
        Respond(
            transport,
            initRequest,
            """
            {
              "codexHome": "C:\\Codex",
              "platformFamily": "windows",
              "platformOs": "windows",
              "userAgent": "fake"
            }
            """);

        AppServerCapabilityResult result = await resultTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.IsCompatible);
        Assert.Empty(result.MissingMethods);
    }

    private static async Task<JsonElement> ReadRequestAsync(ChannelLineWriter input) =>
        JsonDocument.Parse(await input.ReadLineAsync(CancellationToken.None)).RootElement.Clone();

    private static void Respond(AsyncLineTransport transport, JsonElement request, string resultJson) =>
        transport.ServerOutput.WriteLine(JsonSerializer.Serialize(new
        {
            id = request.GetProperty("id").GetInt64(),
            result = JsonDocument.Parse(resultJson).RootElement.Clone(),
        }));

    private sealed class FakeProcessHost : IProcessHost
    {
        private readonly AsyncLineTransport _transport;

        public FakeProcessHost(AsyncLineTransport transport)
        {
            _transport = transport;
        }

        public Task<IHostedProcess> StartAsync(
            ProcessStartRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult<IHostedProcess>(new FakeHostedProcess(_transport));
        }
    }

    private sealed class FailingProcessHost : IProcessHost
    {
        public Task<IHostedProcess> StartAsync(
            ProcessStartRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            throw new InvalidOperationException("The App Server process could not be started.");
        }
    }

    private sealed class FakeHostedProcess : IHostedProcess
    {
        private readonly AsyncLineTransport _transport;
        private readonly TaskCompletionSource<ProcessExitResult> _exitCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeHostedProcess(AsyncLineTransport transport)
        {
            _transport = transport;
        }

        public TextWriter StandardInput => _transport.ClientInput;

        public TextReader StandardOutput => _transport.ServerOutput;

        public TextReader StandardError => TextReader.Null;

        public Task<ProcessExitResult> WaitForExitAsync(CancellationToken cancellationToken) =>
            _exitCompletion.Task.WaitAsync(cancellationToken);

        public Task<ProcessExitResult> TerminateAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            _exitCompletion.TrySetResult(new ProcessExitResult(0, true));
            return Task.FromResult(new ProcessExitResult(0, true));
        }

        public ValueTask DisposeAsync()
        {
            _exitCompletion.TrySetResult(new ProcessExitResult(0, false));
            return ValueTask.CompletedTask;
        }
    }
}
