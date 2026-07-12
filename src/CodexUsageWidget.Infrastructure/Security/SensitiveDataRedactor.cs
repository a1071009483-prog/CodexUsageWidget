using System.Text.RegularExpressions;

namespace CodexUsageWidget.Infrastructure.Security;

/// <summary>
/// Shared redaction helpers used by logging, crash reports, and any other local output
/// that must not retain authentication material, contact information, or workspace paths.
/// </summary>
public static class SensitiveDataRedactor
{
    /// <summary>The token substituted for sensitive values.</summary>
    public const string RedactedValue = "[REDACTED]";

    /// <summary>
    /// Key fragments that identify properties whose values are always dropped rather than
    /// logged, even when the value itself does not match a pattern.
    /// </summary>
    public static readonly string[] SensitiveKeyFragments =
    [
        "token",
        "secret",
        "credential",
        "cookie",
        "password",
        "authorization",
        "email",
        "workspace_path",
        "prompt",
        "response",
    ];

    private static readonly Regex BearerOrKeyPattern = new(
        @"(?i)(?:\bbearer\s+\S+|\bsk-[a-z0-9_-]{6,})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AbsolutePathPattern = new(
        @"(?i)(?:\b[A-Z]:\\|(?:^|\s)/(?:users|home|var|tmp|etc)/)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns <see cref="RedactedValue"/> when the value appears to contain a bearer token,
    /// API key, email address, or absolute filesystem path; otherwise returns the value unchanged.
    /// </summary>
    public static string? Redact(string? value) =>
        IsSensitiveValue(value) ? RedactedValue : value;

    /// <summary>Determines whether a value matches a known sensitive-data pattern.</summary>
    public static bool IsSensitiveValue(string? value) =>
        value is not null
        && (BearerOrKeyPattern.IsMatch(value)
            || EmailPattern.IsMatch(value)
            || AbsolutePathPattern.IsMatch(value));
}
