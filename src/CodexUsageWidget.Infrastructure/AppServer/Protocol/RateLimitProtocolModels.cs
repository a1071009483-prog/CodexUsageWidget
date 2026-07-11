using System.Text.Json.Serialization;

namespace CodexUsageWidget.Infrastructure.AppServer.Protocol;

public sealed record RateLimitWindow(
    [property: JsonPropertyName("usedPercent")] int UsedPercent,
    [property: JsonPropertyName("resetsAt")] long? ResetsAt = null,
    [property: JsonPropertyName("windowDurationMins")] long? WindowDurationMins = null);

public sealed record RateLimitSnapshot(
    [property: JsonPropertyName("limitId")] string? LimitId = null,
    [property: JsonPropertyName("limitName")] string? LimitName = null,
    [property: JsonPropertyName("planType")] string? PlanType = null,
    [property: JsonPropertyName("primary")] RateLimitWindow? Primary = null,
    [property: JsonPropertyName("secondary")] RateLimitWindow? Secondary = null,
    [property: JsonPropertyName("rateLimitReachedType")] string? RateLimitReachedType = null);

public sealed record RateLimitsReadResponse(
    [property: JsonPropertyName("rateLimits")] RateLimitSnapshot RateLimits,
    [property: JsonPropertyName("rateLimitsByLimitId")]
    IReadOnlyDictionary<string, RateLimitSnapshot>? RateLimitsByLimitId = null);

public sealed record RateLimitsUpdatedParameters(
    [property: JsonPropertyName("rateLimits")] RateLimitSnapshot RateLimits);

public sealed class RateLimitsUpdatedEventArgs : EventArgs
{
    public RateLimitsUpdatedEventArgs(RateLimitSnapshot rateLimits) => RateLimits = rateLimits;

    public RateLimitSnapshot RateLimits { get; }
}
