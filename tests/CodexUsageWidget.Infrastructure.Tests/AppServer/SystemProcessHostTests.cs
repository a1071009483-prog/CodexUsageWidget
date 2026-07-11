using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.AppServer;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class SystemProcessHostTests
{
    private static string FakeAppServerDllPath =>
        Path.Combine(AppContext.BaseDirectory, "FakeCodexAppServer.dll");

    private static string DotnetHostPath =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    [Fact]
    public async Task StartsExecutableWithRedirectedStdioAndReportsNaturalExit()
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"system-process-host-natural-{Guid.NewGuid():N}.json");
        string workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"system-process-host-cwd-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(workingDirectory);
            await File.WriteAllTextAsync(scriptPath, """
                {
                  "steps": [
                    { "writeCommandLineArg": 1 },
                    { "writeEnvironmentVariable": "FAKE_PROCESS_TEST_ENV" },
                    { "writeWorkingDirectory": true },
                    {
                      "expect": "request",
                      "method": "initialize",
                      "numericId": true,
                      "result": { "userAgent": "fake" }
                    },
                    { "expect": "notification", "method": "initialized" },
                    { "writeStderrMarker": true },
                    { "waitForEof": true },
                    { "exit": 0 }
                  ]
                }
                """);

            var request = new ProcessStartRequest(
                DotnetHostPath,
                [FakeAppServerDllPath, scriptPath, "ordered-arg-marker"],
                workingDirectory,
                new Dictionary<string, string?>
                {
                    ["FAKE_PROCESS_TEST_ENV"] = "env-value-reached",
                });

            var host = new SystemProcessHost();
            await using IHostedProcess process = await host.StartAsync(
                request, CancellationToken.None);

            // Ordered argument reached the child process.
            Assert.Equal(
                "ordered-arg-marker",
                await process.StandardOutput.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)));

            // Environment variable value reached the child process.
            Assert.Equal(
                "env-value-reached",
                await process.StandardOutput.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)));

            // Working directory reached the child process.
            Assert.Equal(
                workingDirectory,
                await process.StandardOutput.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)));

            // Write a JSONL request to stdin; read the protocol response from stdout.
            await process.StandardInput.WriteLineAsync(
                "{\"id\":42,\"method\":\"initialize\",\"params\":{}}");
            await process.StandardInput.FlushAsync();

            string? responseLine = await process.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(responseLine);
            using JsonDocument response = JsonDocument.Parse(responseLine);
            Assert.Equal(42, response.RootElement.GetProperty("id").GetInt64());
            Assert.Equal(
                "fake",
                response.RootElement.GetProperty("result")
                    .GetProperty("userAgent").GetString());

            // Write a notification to stdin.
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            await process.StandardInput.FlushAsync();

            // Read the harmless stderr marker from the redirected stderr stream.
            Assert.Equal(
                "fake-app-server-marker",
                await process.StandardError.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)));

            // Close stdin so the fake reaches EOF and exits naturally.
            process.StandardInput.Close();

            // Natural exit: zero exit code, not terminated.
            ProcessExitResult exitResult = await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, exitResult.ExitCode);
            Assert.False(exitResult.WasTerminated);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }

            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TerminateAsyncKillsAChildThatIgnoresEof()
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"system-process-host-terminate-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                {
                  "steps": [
                    { "hangAfterEof": true }
                  ]
                }
                """);

            var request = new ProcessStartRequest(
                DotnetHostPath,
                [FakeAppServerDllPath, scriptPath]);

            var host = new SystemProcessHost();
            await using IHostedProcess process = await host.StartAsync(
                request, CancellationToken.None);

            // Close stdin — the fake reads EOF then deliberately hangs forever.
            process.StandardInput.Close();

            // Cancel a wait — per contract, cancellation must NOT kill the child.
            using var cancelCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => process.WaitForExitAsync(cancelCts.Token));

            // TerminateAsync returning WasTerminated=true proves the child survived
            // the canceled wait. The call must complete within a bounded duration.
            ProcessExitResult result = await process.TerminateAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.WasTerminated);

            // Idempotent: repeat terminate observes the same completed lifetime.
            ProcessExitResult repeatedTerminate = await process
                .TerminateAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(result.WasTerminated, repeatedTerminate.WasTerminated);

            // A subsequent wait also observes the same completed lifetime.
            ProcessExitResult waitResult = await process
                .WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(result.WasTerminated, waitResult.WasTerminated);
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
