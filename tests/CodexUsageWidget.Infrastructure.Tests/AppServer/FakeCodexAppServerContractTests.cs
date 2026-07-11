using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

public sealed class FakeCodexAppServerContractTests
{
    [Fact]
    public async Task RequiresInitializeThenInitializedAndCopiesTheRequestId()
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"fake-codex-app-server-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            scriptPath,
            """
            {
              "steps": [
                {
                  "expect": "request",
                  "method": "initialize",
                  "numericId": true,
                  "result": {
                    "codexHome": "C:\\Codex",
                    "platformFamily": "windows",
                    "platformOs": "windows",
                    "userAgent": "fake"
                  }
                },
                { "expect": "notification", "method": "initialized" },
                { "writeStderrMarker": true },
                { "waitForEof": true },
                { "exit": 0 }
              ]
            }
            """);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "FakeCodexAppServer.dll"));
        process.StartInfo.ArgumentList.Add(scriptPath);

        try
        {
            Assert.True(process.Start());
            await process.StandardInput.WriteLineAsync(
                "{\"id\":17,\"method\":\"initialize\",\"params\":{}}");
            await process.StandardInput.FlushAsync();

            string? responseLine = await process.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(responseLine);
            using JsonDocument response = JsonDocument.Parse(responseLine);
            Assert.Equal(17, response.RootElement.GetProperty("id").GetInt64());
            Assert.Equal(
                "fake",
                response.RootElement.GetProperty("result").GetProperty("userAgent").GetString());

            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            Assert.Equal(
                "fake-app-server-marker",
                await process.StandardError.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            File.Delete(scriptPath);
        }
    }
}
