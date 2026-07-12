using System.Threading;
using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Settings;
using CodexUsageWidget.Infrastructure.Tests.Windows;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Settings;

public sealed class JsonSettingsStoreTests
{
    private const string Path = @"C:\AppData\CodexUsageWidget\settings.json";

    [Fact]
    public async Task LoadAsyncReturnsDefaultsWhenFileMissing()
    {
        FakeAppFileSystem fileSystem = new();
        JsonSettingsStore store = new(fileSystem, Path);

        WidgetSettings settings = await store.LoadAsync();

        Assert.True(settings.StartWithWindows);
        Assert.True(settings.IsAutomationEnabled);
    }

    [Fact]
    public async Task SaveAndLoadRoundTrip()
    {
        FakeAppFileSystem fileSystem = new();
        JsonSettingsStore store = new(fileSystem, Path);

        await store.SaveAsync(new WidgetSettings(false, false));
        WidgetSettings loaded = await store.LoadAsync();

        Assert.False(loaded.StartWithWindows);
        Assert.False(loaded.IsAutomationEnabled);
    }

    [Fact]
    public async Task LoadAsyncReturnsDefaultsForMalformedJson()
    {
        FakeAppFileSystem fileSystem = new();
        await fileSystem.WriteTextAsync(
            new AppFileWriteRequest(Path, "not-json"),
            CancellationToken.None);
        JsonSettingsStore store = new(fileSystem, Path);

        WidgetSettings settings = await store.LoadAsync();

        Assert.True(settings.StartWithWindows);
        Assert.True(settings.IsAutomationEnabled);
    }
}
