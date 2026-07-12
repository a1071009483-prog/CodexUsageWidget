using System.Text.Json;

namespace CodexUsageWidget.Infrastructure.Tests.AppServer;

/// <summary>
/// Builds JSON scripts for the <see cref="FakeCodexAppServer.Program"/> test harness.
/// The harness reads standard input, matches expected JSON-RPC frames, and writes
/// scripted responses so that end-to-end tests can exercise the real process host
/// and supervisor against a deterministic Codex App Server stand-in.
/// </summary>
public sealed class FakeAppServerScriptBuilder
{
    private readonly List<object> _steps = new();
    private int _requestCounter;

    /// <summary>
    /// Reads the <paramref name="initialize"/> handshake request and replies with the
    /// given result object, then reads the <see cref="InitializedNotification"/>.
    /// </summary>
    public FakeAppServerScriptBuilder Handshake(object initializeResult)
    {
        ExpectRequest("initialize", initializeResult, numericId: true);
        ExpectNotification("initialized");
        return this;
    }

    /// <summary>Reads a request with the given method and writes the result.</summary>
    public FakeAppServerScriptBuilder ExpectRequest(
        string method,
        object? result = null,
        bool numericId = true)
    {
        var step = new Dictionary<string, object?>
        {
            ["method"] = method,
            ["expect"] = "request",
            ["numericId"] = numericId,
        };

        if (result is not null)
        {
            step["result"] = result;
        }

        _steps.Add(step);
        _requestCounter++;
        return this;
    }

    /// <summary>Reads a notification with the given method.</summary>
    public FakeAppServerScriptBuilder ExpectNotification(string method)
    {
        _steps.Add(new
        {
            method,
            expect = "notification",
        });
        return this;
    }

    /// <summary>Writes a JSON-RPC notification line to stdout without reading input.</summary>
    public FakeAppServerScriptBuilder EmitNotification(string method, object parameters)
    {
        _steps.Add(new
        {
            emitRaw = JsonSerializer.Serialize(new { method, @params = parameters }),
        });
        return this;
    }

    /// <summary>Writes an arbitrary raw JSON line to stdout.</summary>
    public FakeAppServerScriptBuilder EmitRaw(string line)
    {
        _steps.Add(new { emitRaw = line });
        return this;
    }

    /// <summary>Reads stdin until EOF.</summary>
    public FakeAppServerScriptBuilder WaitForEof()
    {
        _steps.Add(new { waitForEof = true });
        return this;
    }

    /// <summary>Reads stdin until EOF and then hangs indefinitely.</summary>
    public FakeAppServerScriptBuilder HangAfterEof()
    {
        _steps.Add(new { hangAfterEof = true });
        return this;
    }

    /// <summary>Writes the value of an environment variable to stdout as a raw line.</summary>
    public FakeAppServerScriptBuilder WriteEnvironmentVariable(string envVarName)
    {
        _steps.Add(new { writeEnvironmentVariable = envVarName });
        return this;
    }

    /// <summary>Exits the fake server with the given code.</summary>
    public FakeAppServerScriptBuilder Exit(int code)
    {
        _steps.Add(new { exit = code });
        return this;
    }

    /// <summary>Returns the JSON script payload.</summary>
    public string Build() => JsonSerializer.Serialize(new { steps = _steps });

    /// <summary>Writes the script to a temp file and returns its path.</summary>
    public string WriteToFile(string directory)
    {
        string path = Path.Combine(directory, $"fake-app-server-script-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, Build());
        return path;
    }
}
