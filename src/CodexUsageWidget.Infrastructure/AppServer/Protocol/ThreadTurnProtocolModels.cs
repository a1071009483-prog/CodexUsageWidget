using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageWidget.Infrastructure.AppServer.Protocol;

public sealed record ThreadStartOptions
{
    [JsonPropertyName("allowProviderModelFallback")]
    public bool AllowProviderModelFallback { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public string ApprovalPolicy { get; init; } = "never";

    [JsonPropertyName("cwd")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("dynamicTools")]
    public IReadOnlyList<object> DynamicTools { get; init; } = [];

    [JsonPropertyName("environments")]
    public IReadOnlyList<object> Environments { get; init; } = [];

    [JsonPropertyName("ephemeral")]
    public bool Ephemeral { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("sandbox")]
    public string Sandbox { get; init; } = "read-only";
}

public sealed record ThreadDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ephemeral")] bool Ephemeral = false);

public sealed record ThreadStartResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("thread")] ThreadDescriptor Thread);

public sealed record TextUserInput(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("text_elements")] IReadOnlyList<object>? TextElements = null);

public sealed record TurnStartOptions
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("input")]
    public required IReadOnlyList<TextUserInput> Input { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public string ApprovalPolicy { get; init; } = "never";

    [JsonPropertyName("effort")]
    public string? Effort { get; init; }

    [JsonPropertyName("sandboxPolicy")]
    public JsonElement? SandboxPolicy { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "none";
}

public sealed record TurnDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("items")] IReadOnlyList<JsonElement> Items);

public sealed record TurnStartResponse(
    [property: JsonPropertyName("turn")] TurnDescriptor Turn);

public sealed record TurnInterruptParameters(
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId);

public sealed record ThreadDeleteParameters(
    [property: JsonPropertyName("threadId")] string ThreadId);
