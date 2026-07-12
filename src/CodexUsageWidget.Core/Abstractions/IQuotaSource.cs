using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.Core.Abstractions;

/// <summary>
/// A read-only source of quota data consumed by <see cref="Monitoring.QuotaMonitor"/>.
/// The source is responsible for raising <see cref="Updated"/> when it receives a
/// push notification; the monitor will then call <see cref="ReadAsync"/> to fetch
/// the latest data.
/// </summary>
public interface IQuotaSource
{
    /// <summary>
    /// Raised when a push notification indicates that quota data may have changed.
    /// The event carries no snapshot because the monitor will call <see cref="ReadAsync"/>
    /// to fetch the authoritative state.
    /// </summary>
    event EventHandler? Updated;

    /// <summary>
    /// Fetches the latest quota data from the source.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can stop the read.</param>
    /// <returns>A result containing the raw snapshot, or failure information.</returns>
    Task<QuotaSourceResult> ReadAsync(CancellationToken cancellationToken);
}
