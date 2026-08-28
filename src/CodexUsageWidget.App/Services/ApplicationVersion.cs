using System.Reflection;

namespace CodexUsageWidget.App.Services;

/// <summary>
/// Single source of truth for the running application's semantic version.
/// The value is embedded by MSBuild from the release version input.
/// </summary>
public static class ApplicationVersion
{
    public static string Current => Normalize(
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0");

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        int metadata = value.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? value : value[..metadata];
    }
}
