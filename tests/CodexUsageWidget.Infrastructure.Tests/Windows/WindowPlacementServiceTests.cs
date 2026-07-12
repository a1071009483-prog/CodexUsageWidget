using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Windows;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Windows;

public sealed class WindowPlacementServiceTests
{
    private const string Path = @"C:\AppData\CodexUsageWidget\window-placement.json";

    [Fact]
    public async Task SaveAndLoadRoundTrip()
    {
        FakeAppFileSystem fileSystem = new();
        WindowPlacementService service = new(fileSystem, Path);
        WindowPlacement original = new(100, 200, 320, 240);

        await service.SavePlacementAsync(original);
        WindowPlacement? loaded = await service.LoadPlacementAsync();

        Assert.NotNull(loaded);
        Assert.Equal(original.Left, loaded.Left);
        Assert.Equal(original.Top, loaded.Top);
        Assert.Equal(original.Width, loaded.Width);
        Assert.Equal(original.Height, loaded.Height);
    }

    [Fact]
    public async Task LoadReturnsNullWhenFileMissing()
    {
        FakeAppFileSystem fileSystem = new();
        WindowPlacementService service = new(fileSystem, Path);

        WindowPlacement? loaded = await service.LoadPlacementAsync();

        Assert.Null(loaded);
    }

    [Fact]
    public void ClampPlacementKeepsVisibleWindowOnScreen()
    {
        FakeAppFileSystem fileSystem = new();
        WindowPlacementService service = new(fileSystem, Path);
        WindowPlacement placement = new(1800, 900, 400, 300);
        WindowPlacementScreen screen = new(0, 0, 1920, 1080);

        WindowPlacement clamped = service.ClampPlacement(placement, new[] { screen }, screen);

        Assert.True(clamped.Left + clamped.Width <= screen.Left + screen.Width);
        Assert.True(clamped.Top + clamped.Height <= screen.Top + screen.Height);
        Assert.True(clamped.Width >= 48);
        Assert.True(clamped.Height >= 48);
    }

    [Fact]
    public void ClampPlacementFitsOversizedWindowToWorkArea()
    {
        FakeAppFileSystem fileSystem = new();
        WindowPlacementService service = new(fileSystem, Path);
        WindowPlacement placement = new(0, 0, 3000, 2000);
        WindowPlacementScreen workArea = new(0, 0, 1920, 1080);

        WindowPlacement clamped = service.ClampPlacement(placement, new[] { workArea }, workArea);

        Assert.Equal(workArea.Width, clamped.Width);
        Assert.Equal(workArea.Height, clamped.Height);
    }

    [Fact]
    public void ClampPlacementReturnsWorkAreaWhenNoScreens()
    {
        FakeAppFileSystem fileSystem = new();
        WindowPlacementService service = new(fileSystem, Path);
        WindowPlacement placement = new(-1000, -1000, 320, 240);
        WindowPlacementScreen workArea = new(0, 0, 1920, 1080);

        WindowPlacement clamped = service.ClampPlacement(placement, Array.Empty<WindowPlacementScreen>(), workArea);

        Assert.True(clamped.Left >= workArea.Left - clamped.Width + 48);
        Assert.True(clamped.Top >= workArea.Top - clamped.Height + 48);
    }
}
