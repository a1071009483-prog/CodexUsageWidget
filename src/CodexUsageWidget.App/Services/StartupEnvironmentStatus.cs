namespace CodexUsageWidget.App.Services;

/// <summary>
/// User-facing startup environment classification. Every non-ready state keeps
/// automatic activation disabled.
/// </summary>
public enum StartupEnvironmentKind
{
    Ready,
    CodexCliMissing,
    AuthenticationRequired,
    UnsupportedAuthentication,
    AppServerIncompatible,
    StartupError,
}

/// <summary>
/// Structured startup environment result surfaced to the user, plus the
/// non-sensitive runtime diagnostic payload (no credentials or account data).
/// </summary>
public sealed record StartupEnvironmentStatus(
    StartupEnvironmentKind Kind,
    string UserMessage,
    string WidgetVersion,
    string? CodexCliVersion,
    string WindowsVersion,
    bool CanActivate)
{
    public bool IsReady => Kind == StartupEnvironmentKind.Ready;
}
