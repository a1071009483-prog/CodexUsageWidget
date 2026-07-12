using System.Windows.Forms;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Infrastructure.Windows;

/// <summary>
/// Windows notify-icon implementation of <see cref="IUserNotifier"/>.
/// Deduplicates by <see cref="UserNotificationRequest.DeduplicationKey"/> so the same
/// activation result only notifies once.
/// </summary>
public sealed class WindowsNotificationService : IUserNotifier
{
    private readonly INotifyIcon _icon;
    private readonly HashSet<string> _shownKeys = new(StringComparer.Ordinal);

    public WindowsNotificationService(INotifyIcon icon)
    {
        _icon = icon ?? throw new ArgumentNullException(nameof(icon));
    }

    public Task<UserNotificationResult> NotifyAsync(
        UserNotificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.DeduplicationKey is not null
            && !_shownKeys.Add(request.DeduplicationKey))
        {
            return Task.FromResult(new UserNotificationResult(true, "deduplicated"));
        }

        ToolTipIcon icon = ClassifyIcon(request.Title);
        _icon.ShowBalloonTip(3000, request.Title, request.Message, icon);
        return Task.FromResult(new UserNotificationResult(true));
    }

    private static ToolTipIcon ClassifyIcon(string title)
    {
        if (title.Contains("succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return ToolTipIcon.Info;
        }

        if (title.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || title.Contains("ambiguous", StringComparison.OrdinalIgnoreCase))
        {
            return ToolTipIcon.Error;
        }

        return ToolTipIcon.Warning;
    }
}
