using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class FakeNotifier : IUserNotifier
{
    private readonly List<UserNotificationRequest> _calls = new();

    public IReadOnlyList<UserNotificationRequest> Calls => _calls;

    public Task<UserNotificationResult> NotifyAsync(
        UserNotificationRequest request,
        CancellationToken cancellationToken)
    {
        _calls.Add(request);
        return Task.FromResult(new UserNotificationResult(true));
    }
}
