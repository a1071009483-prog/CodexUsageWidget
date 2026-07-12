namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// A persisted window position and size, recorded in device-independent pixels.
/// </summary>
/// <param name="Left">Distance from the left edge of the virtual screen.</param>
/// <param name="Top">Distance from the top edge of the virtual screen.</param>
/// <param name="Width">Window width.</param>
/// <param name="Height">Window height.</param>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height);

/// <summary>
/// Persists and restores window placement safely across restarts, monitor changes,
/// and work-area changes. Implementations exclude credentials and sensitive content.
/// </summary>
public interface IWindowPlacementService
{
    /// <summary>Saves placement to per-user local storage.</summary>
    Task SavePlacementAsync(WindowPlacement placement, CancellationToken cancellationToken = default);

    /// <summary>Loads placement from per-user local storage, or <c>null</c> if none exists.</summary>
    Task<WindowPlacement?> LoadPlacementAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adjusts a placement so the window is visible on the available screens and fits the work area.
    /// </summary>
    WindowPlacement ClampPlacement(
        WindowPlacement placement,
        IReadOnlyList<WindowPlacementScreen> screens,
        WindowPlacementScreen workArea);
}

/// <summary>
/// A screen rectangle used by <see cref="IWindowPlacementService.ClampPlacement"/>.
/// </summary>
public sealed record WindowPlacementScreen(double Left, double Top, double Width, double Height);
