using System.Text.Json.Serialization;

namespace CodexUsageWidget.Infrastructure.AppServer.Protocol;

public sealed record ModelListParameters(
    [property: JsonPropertyName("cursor")] string? Cursor = null,
    [property: JsonPropertyName("includeHidden")] bool? IncludeHidden = null,
    [property: JsonPropertyName("limit")] uint? Limit = null);

public sealed record ReasoningEffortOption(
    [property: JsonPropertyName("reasoningEffort")] string ReasoningEffort,
    [property: JsonPropertyName("description")] string Description);

public sealed record ModelDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("hidden")] bool Hidden,
    [property: JsonPropertyName("isDefault")] bool IsDefault,
    [property: JsonPropertyName("defaultReasoningEffort")] string DefaultReasoningEffort,
    [property: JsonPropertyName("supportedReasoningEfforts")]
    IReadOnlyList<ReasoningEffortOption> SupportedReasoningEfforts);

public sealed record ModelListResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<ModelDescriptor> Data,
    [property: JsonPropertyName("nextCursor")] string? NextCursor = null);
