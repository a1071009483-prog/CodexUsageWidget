using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageWidget.Infrastructure.AppServer.Protocol;

public sealed record ClientInformation(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string? Title = null);

public sealed record InitializeCapabilities(
    [property: JsonPropertyName("experimentalApi")] bool ExperimentalApi = true,
    [property: JsonPropertyName("requestAttestation")] bool RequestAttestation = false);

public sealed record InitializeParameters(
    [property: JsonPropertyName("clientInfo")] ClientInformation ClientInfo,
    [property: JsonPropertyName("capabilities")] InitializeCapabilities? Capabilities = null);

public sealed record InitializeResponse(
    [property: JsonPropertyName("codexHome")] string CodexHome,
    [property: JsonPropertyName("platformFamily")] string PlatformFamily,
    [property: JsonPropertyName("platformOs")] string PlatformOs,
    [property: JsonPropertyName("userAgent")] string UserAgent);

public sealed class AppServerNotificationEventArgs : EventArgs
{
    public AppServerNotificationEventArgs(string method, JsonElement? parameters)
    {
        Method = method;
        Parameters = parameters;
    }

    public string Method { get; }

    public JsonElement? Parameters { get; }
}

public sealed class AppServerRequestEventArgs : EventArgs
{
    public AppServerRequestEventArgs(JsonElement id, string method, JsonElement? parameters)
    {
        Id = id;
        Method = method;
        Parameters = parameters;
    }

    public JsonElement Id { get; }

    public string Method { get; }

    public JsonElement? Parameters { get; }
}

public sealed record AppServerEmptyResponse;
