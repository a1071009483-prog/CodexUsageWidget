using System.Text.Json;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Windows;

/// <summary>
/// Persists and restores the widget window placement under the current user's
/// <c>%LOCALAPPDATA%\CodexUsageWidget\window-placement.json</c>. The file contains only
/// position and size numbers; no credentials, tokens, or account data.
/// </summary>
public sealed class WindowPlacementService : IWindowPlacementService
{
    private const string FileName = "window-placement.json";
    private const string AppFolderName = "CodexUsageWidget";
    private const double MinVisibleSize = 48.0;

    private readonly IAppFileSystem _fileSystem;
    private readonly string _filePath;

    public WindowPlacementService(IAppFileSystem fileSystem)
        : this(fileSystem, GetDefaultFilePath())
    {
    }

    public WindowPlacementService(IAppFileSystem fileSystem, string filePath)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <inheritdoc/>
    public async Task<WindowPlacement?> LoadPlacementAsync(CancellationToken cancellationToken = default)
    {
        AppFileReadResult result = await _fileSystem.ReadTextAsync(
                new AppFileReadRequest(_filePath),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Content))
        {
            return null;
        }

        try
        {
            WindowPlacementDto? dto = JsonSerializer.Deserialize<WindowPlacementDto>(result.Content);
            if (dto is null)
            {
                return null;
            }

            return new WindowPlacement(dto.Left, dto.Top, dto.Width, dto.Height);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SavePlacementAsync(
        WindowPlacement placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placement);

        string content = JsonSerializer.Serialize(new WindowPlacementDto
        {
            Left = placement.Left,
            Top = placement.Top,
            Width = placement.Width,
            Height = placement.Height,
        });

        await _fileSystem.WriteTextAsync(
                new AppFileWriteRequest(_filePath, content),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public WindowPlacement ClampPlacement(
        WindowPlacement placement,
        IReadOnlyList<WindowPlacementScreen> screens,
        WindowPlacementScreen workArea)
    {
        if (screens.Count == 0)
        {
            // No screen information: keep the work area and clamp size.
            return ClampToScreen(placement, workArea);
        }

        // Find the screen that contains the largest part of the window center.
        double centerX = placement.Left + placement.Width / 2.0;
        double centerY = placement.Top + placement.Height / 2.0;
        WindowPlacementScreen? target = null;
        foreach (WindowPlacementScreen screen in screens)
        {
            if (centerX >= screen.Left
                && centerX < screen.Left + screen.Width
                && centerY >= screen.Top
                && centerY < screen.Top + screen.Height)
            {
                target = screen;
                break;
            }
        }

        target ??= screens[0];
        return ClampToScreen(placement, target);
    }

    private static WindowPlacement ClampToScreen(WindowPlacement placement, WindowPlacementScreen screen)
    {
        double width = Math.Max(MinVisibleSize, Math.Min(placement.Width, screen.Width));
        double height = Math.Max(MinVisibleSize, Math.Min(placement.Height, screen.Height));

        double left = placement.Left;
        double top = placement.Top;

        // Ensure a minimum visible edge remains on screen.
        double right = left + width;
        double bottom = top + height;

        if (right < screen.Left + MinVisibleSize)
        {
            left = screen.Left + MinVisibleSize - width;
        }
        else if (left > screen.Left + screen.Width - MinVisibleSize)
        {
            left = screen.Left + screen.Width - MinVisibleSize;
        }

        if (bottom < screen.Top + MinVisibleSize)
        {
            top = screen.Top + MinVisibleSize - height;
        }
        else if (top > screen.Top + screen.Height - MinVisibleSize)
        {
            top = screen.Top + screen.Height - MinVisibleSize;
        }

        left = Math.Max(left, screen.Left - width + MinVisibleSize);
        top = Math.Max(top, screen.Top - height + MinVisibleSize);

        return new WindowPlacement(left, top, width, height);
    }

    private static string GetDefaultFilePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppFolderName, FileName);
    }

    private sealed class WindowPlacementDto
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
