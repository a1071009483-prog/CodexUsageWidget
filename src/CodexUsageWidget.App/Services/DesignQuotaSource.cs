using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.App.Services;

/// <summary>
/// Design-time quota source that always reports no data. Used for the non-generating
/// layout smoke mode until the real App Server gateway is wired.
/// </summary>
public sealed class DesignQuotaSource : IQuotaSource
{
    public event EventHandler? Updated;

    public Task<QuotaSourceResult> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new QuotaSourceResult(false, null, "design-mode"));

    public void RaiseUpdated() => Updated?.Invoke(this, EventArgs.Empty);
}
