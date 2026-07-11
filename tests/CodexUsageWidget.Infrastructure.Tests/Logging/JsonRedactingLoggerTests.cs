using System.Reflection;
using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Logging;

public sealed class JsonRedactingLoggerTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 7, 11, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsyncRemovesSensitiveKeysAndRedactsSensitiveValues()
    {
        using var output = new StringWriter();
        object logger = CreateLogger(output);
        var properties = new Dictionary<string, string?>
        {
            ["model"] = "gpt-5-mini",
            ["access_token"] = "sk-test-raw-token-value",
            ["email"] = "alice@example.com",
            ["workspace_path"] = @"C:\Users\Alice\private-workspace",
            ["prompt"] = "Return the private prompt verbatim",
            ["response"] = "Private response body",
            ["detail"] = "Bearer raw-bearer-token",
            ["contact"] = "bob@example.com",
            ["location"] = @"C:\Secrets\account.json",
        };

        await WriteAsync(logger, CreateEvent("activation_attempt", properties));

        string line = output.ToString();
        Assert.DoesNotContain("sk-test-raw-token-value", line);
        Assert.DoesNotContain("alice@example.com", line);
        Assert.DoesNotContain(@"C:\\Users\\Alice\\private-workspace", line);
        Assert.DoesNotContain("Return the private prompt verbatim", line);
        Assert.DoesNotContain("Private response body", line);
        Assert.DoesNotContain("raw-bearer-token", line);
        Assert.DoesNotContain("bob@example.com", line);
        Assert.DoesNotContain(@"C:\\Secrets\\account.json", line);

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement loggedProperties = document.RootElement.GetProperty("properties");
        Assert.Equal("gpt-5-mini", loggedProperties.GetProperty("model").GetString());
        Assert.False(loggedProperties.TryGetProperty("access_token", out _));
        Assert.False(loggedProperties.TryGetProperty("email", out _));
        Assert.False(loggedProperties.TryGetProperty("workspace_path", out _));
        Assert.False(loggedProperties.TryGetProperty("prompt", out _));
        Assert.False(loggedProperties.TryGetProperty("response", out _));
        Assert.Equal("[REDACTED]", loggedProperties.GetProperty("detail").GetString());
        Assert.Equal("[REDACTED]", loggedProperties.GetProperty("contact").GetString());
        Assert.Equal("[REDACTED]", loggedProperties.GetProperty("location").GetString());
    }

    [Fact]
    public async Task WriteAsyncEmitsOneStructuredJsonObjectPerLine()
    {
        using var output = new StringWriter();
        object logger = CreateLogger(output);

        await WriteAsync(
            logger,
            CreateEvent("quota_sync", new Dictionary<string, string?> { ["account"] = "hashed-account" }));
        await WriteAsync(
            logger,
            CreateEvent("quota_poll", new Dictionary<string, string?> { ["outcome"] = "success" }));

        string[] lines = ReadLines(output.ToString());
        Assert.Equal(2, lines.Length);

        using JsonDocument first = JsonDocument.Parse(lines[0]);
        Assert.Equal("2026-07-11T08:30:00.0000000+00:00", first.RootElement.GetProperty("timestampUtc").GetString());
        Assert.Equal("Information", first.RootElement.GetProperty("level").GetString());
        Assert.Equal("quota_sync", first.RootElement.GetProperty("eventName").GetString());
        Assert.Equal(
            "hashed-account",
            first.RootElement.GetProperty("properties").GetProperty("account").GetString());

        using JsonDocument second = JsonDocument.Parse(lines[1]);
        Assert.Equal("quota_poll", second.RootElement.GetProperty("eventName").GetString());
    }

    private static object CreateLogger(TextWriter output)
    {
        Assembly infrastructure = Assembly.Load("CodexUsageWidget.Infrastructure");
        Type? loggerType = infrastructure.GetType(
            "CodexUsageWidget.Infrastructure.Logging.JsonRedactingLogger");

        Assert.NotNull(loggerType);
        return Activator.CreateInstance(loggerType, output, new FixedClock())!;
    }

    private static object CreateEvent(
        string eventName,
        IReadOnlyDictionary<string, string?> properties)
    {
        Assembly core = typeof(IClock).Assembly;
        Type? eventType = core.GetType("CodexUsageWidget.Core.Abstractions.StructuredLogEvent");
        Type? levelType = core.GetType("CodexUsageWidget.Core.Abstractions.RedactingLogLevel");

        Assert.NotNull(eventType);
        Assert.NotNull(levelType);
        object level = Enum.Parse(levelType, "Information");
        return Activator.CreateInstance(eventType, level, eventName, properties)!;
    }

    private static async Task WriteAsync(object logger, object logEvent)
    {
        MethodInfo? writeAsync = logger.GetType().GetMethod("WriteAsync");
        Assert.NotNull(writeAsync);

        object? pending = writeAsync.Invoke(logger, [logEvent, CancellationToken.None]);
        Assert.NotNull(pending);
        await (ValueTask)pending;
    }

    private static string[] ReadLines(string content)
    {
        var lines = new List<string>();
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return [.. lines];
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => FixedUtcNow;
    }
}
