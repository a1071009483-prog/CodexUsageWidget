using System.Text.Json;

namespace FakeCodexAppServer;

public static class Program
{
    private const string FailureMarker = "fake-app-server-expectation-mismatch";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            await Console.Error.WriteLineAsync(FailureMarker).ConfigureAwait(false);
            return 2;
        }

        try
        {
            await using FileStream scriptStream = File.OpenRead(args[0]);
            using JsonDocument script = await JsonDocument.ParseAsync(scriptStream)
                .ConfigureAwait(false);

            foreach (JsonElement step in script.RootElement.GetProperty("steps").EnumerateArray())
            {
                if (step.TryGetProperty("expect", out JsonElement expectation))
                {
                    if (!await MatchExpectedInputAsync(step, expectation.GetString())
                            .ConfigureAwait(false))
                    {
                        await Console.Error.WriteLineAsync(FailureMarker).ConfigureAwait(false);
                        return 2;
                    }

                    continue;
                }

                if (step.TryGetProperty("writeStderrMarker", out JsonElement marker)
                    && marker.ValueKind == JsonValueKind.True)
                {
                    await Console.Error.WriteLineAsync("fake-app-server-marker")
                        .ConfigureAwait(false);
                    await Console.Error.FlushAsync().ConfigureAwait(false);
                    continue;
                }

                if (step.TryGetProperty("waitForEof", out JsonElement waitForEof)
                    && waitForEof.ValueKind == JsonValueKind.True)
                {
                    while (await Console.In.ReadLineAsync().ConfigureAwait(false) is not null)
                    {
                    }

                    continue;
                }

                if (step.TryGetProperty("writeCommandLineArg", out JsonElement argIndex)
                    && argIndex.ValueKind == JsonValueKind.Number)
                {
                    int index = argIndex.GetInt32();
                    await WriteProtocolLineAsync(
                            index >= 0 && index < args.Length ? args[index] : string.Empty)
                        .ConfigureAwait(false);
                    continue;
                }

                if (step.TryGetProperty("writeEnvironmentVariable", out JsonElement envVarName)
                    && envVarName.ValueKind == JsonValueKind.String)
                {
                    string? envName = envVarName.GetString();
                    string? value = !string.IsNullOrEmpty(envName)
                        ? Environment.GetEnvironmentVariable(envName)
                        : null;
                    await WriteProtocolLineAsync(value ?? string.Empty)
                        .ConfigureAwait(false);
                    continue;
                }

                if (step.TryGetProperty("writeWorkingDirectory", out JsonElement writeCwd)
                    && writeCwd.ValueKind == JsonValueKind.True)
                {
                    await WriteProtocolLineAsync(Directory.GetCurrentDirectory())
                        .ConfigureAwait(false);
                    continue;
                }

                if (step.TryGetProperty("hangAfterEof", out JsonElement hangAfterEof)
                    && hangAfterEof.ValueKind == JsonValueKind.True)
                {
                    while (await Console.In.ReadLineAsync().ConfigureAwait(false) is not null)
                    {
                    }

                    await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None)
                        .ConfigureAwait(false);
                    continue;
                }

                if (step.TryGetProperty("exit", out JsonElement exitCode))
                {
                    return exitCode.GetInt32();
                }

                await Console.Error.WriteLineAsync(FailureMarker).ConfigureAwait(false);
                return 2;
            }

            return 0;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidOperationException
                or KeyNotFoundException)
        {
            await Console.Error.WriteLineAsync(FailureMarker).ConfigureAwait(false);
            return 2;
        }
    }

    private static async Task<bool> MatchExpectedInputAsync(
        JsonElement step,
        string? expectation)
    {
        string? line = await Console.In.ReadLineAsync().ConfigureAwait(false);
        if (line is null)
        {
            return false;
        }

        using JsonDocument input = JsonDocument.Parse(line);
        JsonElement message = input.RootElement;
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("method", out JsonElement method)
            || method.ValueKind != JsonValueKind.String
            || method.GetString() != step.GetProperty("method").GetString())
        {
            return false;
        }

        bool hasId = message.TryGetProperty("id", out JsonElement id);
        if (expectation == "notification")
        {
            return !hasId;
        }

        if (expectation != "request" || !hasId)
        {
            return false;
        }

        if (step.TryGetProperty("numericId", out JsonElement numericId)
            && numericId.ValueKind == JsonValueKind.True
            && id.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (step.TryGetProperty("result", out JsonElement result))
        {
            await WriteProtocolLineAsync(JsonSerializer.Serialize(new { id, result }))
                .ConfigureAwait(false);
        }

        return true;
    }

    private static async Task WriteProtocolLineAsync(string line)
    {
        await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
        await Console.Out.FlushAsync().ConfigureAwait(false);
    }
}
