using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Settings;

/// <summary>
/// JSON-backed implementation of <see cref="ISettingsStore"/>. Stores only the
/// two user-facing toggles under the current user's local application data.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private const string FileName = "settings.json";
    private const string AppFolderName = "CodexUsageWidget";

    private readonly IAppFileSystem _fileSystem;
    private readonly string _filePath;

    public JsonSettingsStore(IAppFileSystem fileSystem)
        : this(fileSystem, GetDefaultFilePath())
    {
    }

    public JsonSettingsStore(IAppFileSystem fileSystem, string filePath)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <inheritdoc/>
    public async Task<WidgetSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppFileReadResult result = await _fileSystem.ReadTextAsync(
                new AppFileReadRequest(_filePath),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Content))
        {
            return new WidgetSettings();
        }

        try
        {
            WidgetSettingsDto? dto = JsonSerializer.Deserialize<WidgetSettingsDto>(result.Content);
            if (dto is null)
            {
                return new WidgetSettings();
            }

            return new WidgetSettings(dto.StartWithWindows, dto.IsAutomationEnabled);
        }
        catch (JsonException)
        {
            return new WidgetSettings();
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        WidgetSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string content = JsonSerializer.Serialize(new WidgetSettingsDto
        {
            StartWithWindows = settings.StartWithWindows,
            IsAutomationEnabled = settings.IsAutomationEnabled,
        });

        await _fileSystem.WriteTextAsync(
                new AppFileWriteRequest(_filePath, content),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string GetDefaultFilePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppFolderName, FileName);
    }

    private sealed class WidgetSettingsDto
    {
        public bool StartWithWindows { get; set; } = true;
        public bool IsAutomationEnabled { get; set; } = true;
    }
}
