using System.Text.Json.Serialization;

namespace CodexUsageWidget.Infrastructure.AppServer.Protocol;

public sealed record AccountReadParameters(
    [property: JsonPropertyName("refreshToken")] bool RefreshToken = false);

public sealed record AccountDescriptor(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("email")] string? Email = null,
    [property: JsonPropertyName("planType")] string? PlanType = null);

public sealed record AccountReadResponse(
    [property: JsonPropertyName("requiresOpenaiAuth")] bool RequiresOpenaiAuth,
    [property: JsonPropertyName("account")] AccountDescriptor? Account = null);
