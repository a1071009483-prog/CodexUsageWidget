using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Infrastructure.Windows;
using Xunit;

namespace CodexUsageWidget.Infrastructure.Tests.Windows;

public sealed class WindowsNotificationServiceTests
{
    [Fact]
    public async Task NotifyShowsBalloonTip()
    {
        FakeNotifyIcon icon = new();
        WindowsNotificationService notifier = new(icon);

        await notifier.NotifyAsync(
            new UserNotificationRequest("Activation succeeded", "The window was activated."),
            CancellationToken.None);

        Assert.Single(icon.Balloons);
        Assert.Equal("Activation succeeded", icon.Balloons[0].Title);
    }

    [Fact]
    public async Task SameDeduplicationKeyOnlyNotifiesOnce()
    {
        FakeNotifyIcon icon = new();
        WindowsNotificationService notifier = new(icon);

        await notifier.NotifyAsync(
            new UserNotificationRequest("Activation succeeded", "Message", "key-1"),
            CancellationToken.None);
        await notifier.NotifyAsync(
            new UserNotificationRequest("Activation succeeded", "Message", "key-1"),
            CancellationToken.None);

        Assert.Single(icon.Balloons);
    }

    [Fact]
    public async Task DifferentDeduplicationKeysNotifySeparately()
    {
        FakeNotifyIcon icon = new();
        WindowsNotificationService notifier = new(icon);

        await notifier.NotifyAsync(
            new UserNotificationRequest("Activation succeeded", "Message", "key-1"),
            CancellationToken.None);
        await notifier.NotifyAsync(
            new UserNotificationRequest("Activation succeeded", "Message", "key-2"),
            CancellationToken.None);

        Assert.Equal(2, icon.Balloons.Count);
    }

    [Fact]
    public async Task NullDeduplicationKeyAlwaysNotifies()
    {
        FakeNotifyIcon icon = new();
        WindowsNotificationService notifier = new(icon);

        await notifier.NotifyAsync(
            new UserNotificationRequest("Activation succeeded", "Message"),
            CancellationToken.None);
        await notifier.NotifyAsync(
            new UserNotificationRequest("Activation succeeded", "Message"),
            CancellationToken.None);

        Assert.Equal(2, icon.Balloons.Count);
    }
}
