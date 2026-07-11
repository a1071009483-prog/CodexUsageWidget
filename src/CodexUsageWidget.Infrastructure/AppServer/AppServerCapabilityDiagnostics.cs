using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CodexUsageWidget.Infrastructure.AppServer;

public sealed record AppServerCapabilityResult(
    bool IsCompatible,
    IReadOnlyList<string> MissingMethods,
    IReadOnlySet<string> AdvertisedMethods);

public sealed class AppServerCapabilityDiagnostics
{
    public static IReadOnlySet<string> RequiredMethods { get; } = new[]
    {
        "initialize",
        "account/read",
        "account/rateLimits/read",
        "model/list",
        "thread/start",
        "turn/start",
        "turn/interrupt",
        "thread/delete",
    }.ToFrozenSet(StringComparer.Ordinal);

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This boundary is intentionally exposed as an injectable instance service.")]
    public AppServerCapabilityResult Evaluate(IEnumerable<string> advertisedMethods)
    {
        ArgumentNullException.ThrowIfNull(advertisedMethods);
        FrozenSet<string> advertised = advertisedMethods
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .ToFrozenSet(StringComparer.Ordinal);
        string[] missing = RequiredMethods
            .Where(method => !advertised.Contains(method))
            .OrderBy(method => method, StringComparer.Ordinal)
            .ToArray();

        return new AppServerCapabilityResult(
            missing.Length == 0,
            missing,
            advertised);
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This boundary is intentionally exposed as an injectable instance service.")]
    public IReadOnlySet<string> ReadMethodsFromSchema(string schemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);

        try
        {
            using JsonDocument document = JsonDocument.Parse(schemaJson);
            var methods = new HashSet<string>(StringComparer.Ordinal);
            ReadMethods(document.RootElement, methods);
            return methods.ToFrozenSet(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new AppServerProtocolException(
                AppServerProtocolErrorKind.MalformedMessage,
                "The App Server schema was malformed.",
                innerException: exception);
        }
    }

    private static void ReadMethods(JsonElement element, ISet<string> methods)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "method", StringComparison.Ordinal)
                    && property.Value.ValueKind == JsonValueKind.Object)
                {
                    ReadMethodConstraint(property.Value, methods);
                }

                ReadMethods(property.Value, methods);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                ReadMethods(item, methods);
            }
        }
    }

    private static void ReadMethodConstraint(JsonElement constraint, ISet<string> methods)
    {
        if (constraint.TryGetProperty("const", out JsonElement constant)
            && constant.ValueKind == JsonValueKind.String)
        {
            methods.Add(constant.GetString()!);
        }

        if (!constraint.TryGetProperty("enum", out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                methods.Add(value.GetString()!);
            }
        }
    }
}
