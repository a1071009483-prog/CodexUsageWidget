using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Windows;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Windows;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void IsRegisteredWhenValuePresent()
    {
        FakeRunRegistryKey key = new();
        key.SetValue("CodexUsageWidget", @"C:\app.exe");
        StartupRegistration registration = new("CodexUsageWidget", @"C:\app.exe", key);

        Assert.True(registration.IsRegistered);
    }

    [Fact]
    public void IsNotRegisteredWhenValueAbsent()
    {
        FakeRunRegistryKey key = new();
        StartupRegistration registration = new("CodexUsageWidget", @"C:\app.exe", key);

        Assert.False(registration.IsRegistered);
    }

    [Fact]
    public async Task RegisterWritesExecutablePath()
    {
        FakeRunRegistryKey key = new();
        StartupRegistration registration = new("CodexUsageWidget", @"C:\app.exe", key);

        await registration.RegisterAsync();

        Assert.True(registration.IsRegistered);
        Assert.Equal(@"C:\app.exe", key.GetValue("CodexUsageWidget"));
    }

    [Fact]
    public async Task UnregisterRemovesValue()
    {
        FakeRunRegistryKey key = new();
        StartupRegistration registration = new("CodexUsageWidget", @"C:\app.exe", key);
        await registration.RegisterAsync();

        await registration.UnregisterAsync();

        Assert.False(registration.IsRegistered);
        Assert.Null(key.GetValue("CodexUsageWidget"));
    }
}
