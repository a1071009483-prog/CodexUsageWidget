namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Persisted widget-level user preferences. Implementations store data under the
/// current user's local application data and must not contain sensitive values.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Loads the persisted settings, returning defaults when none exist.</summary>
    Task<WidgetSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the persisted settings.</summary>
    Task SaveAsync(WidgetSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// User-controlled widget preferences. A missing file is interpreted as the
/// application defaults (Start with Windows enabled, automatic triggering enabled).
/// </summary>
public sealed record WidgetSettings(
    bool StartWithWindows = true,
    bool IsAutomationEnabled = true);
