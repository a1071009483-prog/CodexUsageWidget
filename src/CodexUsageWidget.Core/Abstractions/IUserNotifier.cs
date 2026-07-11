namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// Delivers user-facing notifications through an injectable asynchronous seam.
/// </summary>
public interface IUserNotifier
{
    Task<UserNotificationResult> NotifyAsync(
        UserNotificationRequest request,
        CancellationToken cancellationToken);
}

public sealed record UserNotificationRequest(
    string Title,
    string Message,
    string? DeduplicationKey = null);

public sealed record UserNotificationResult(bool Delivered, string? FailureReason = null);
