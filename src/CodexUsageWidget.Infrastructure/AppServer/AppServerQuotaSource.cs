using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;
using CodexUsageWidget.Infrastructure.AppServer.Protocol;

namespace CodexUsageWidget.Infrastructure.AppServer;

/// <summary>
/// Adapts a live <see cref="AppServerSupervisor"/> into the read-only
/// <see cref="IQuotaSource"/> consumed by <see cref="Monitoring.QuotaMonitor"/>.
/// It forwards App Server rate-limit notifications as <see cref="Updated"/>
/// events and maps gateway responses into the Core-owned
/// <see cref="RawRateLimitSnapshot"/> shape.
/// </summary>
public sealed class AppServerQuotaSource : IQuotaSource, IDisposable
{
    private readonly AppServerSupervisor _supervisor;
    private readonly EventHandler<RateLimitsUpdatedEventArgs> _forwardHandler;

    public AppServerQuotaSource(AppServerSupervisor supervisor)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _forwardHandler = (_, _) => Updated?.Invoke(this, EventArgs.Empty);
        _supervisor.RateLimitsUpdated += _forwardHandler;
    }

    /// <inheritdoc/>
    public event EventHandler? Updated;

    /// <inheritdoc/>
    public async Task<QuotaSourceResult> ReadAsync(CancellationToken cancellationToken)
    {
        AppServerGenerationSession? generation = _supervisor.CurrentGeneration;
        if (generation is null)
        {
            return new QuotaSourceResult(false, null, "App Server session is not available.");
        }

        try
        {
            RateLimitsReadResponse response = await generation.Session.Gateway
                .ReadRateLimitsAsync(cancellationToken)
                .ConfigureAwait(false);

            RawRateLimitSnapshot snapshot = ToSnapshot(response);
            return new QuotaSourceResult(true, snapshot, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AppServerProtocolException ex)
        {
            return new QuotaSourceResult(false, null, $"App Server error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new QuotaSourceResult(false, null, $"Unexpected quota read failure: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _supervisor.RateLimitsUpdated -= _forwardHandler;
    }

    private static RawRateLimitSnapshot ToSnapshot(RateLimitsReadResponse response)
    {
        RateLimitSnapshot limits = response.RateLimits;

        RawRateLimitWindow? primary = limits.Primary is null
            ? null
            : ToRawWindow(limits.Primary);

        Dictionary<string, RawRateLimitWindow> buckets = new(StringComparer.Ordinal);

        if (limits.Secondary is not null)
        {
            buckets["secondary"] = ToRawWindow(limits.Secondary);
        }

        if (response.RateLimitsByLimitId is not null)
        {
            foreach ((string? key, RateLimitSnapshot? value) in response.RateLimitsByLimitId)
            {
                if (string.IsNullOrWhiteSpace(key) || value?.Primary is null)
                {
                    continue;
                }

                buckets[key] = ToRawWindow(value.Primary);
            }
        }

        return new RawRateLimitSnapshot(
            limits.LimitId,
            limits.LimitName,
            limits.PlanType,
            primary,
            buckets);
    }

    private static RawRateLimitWindow ToRawWindow(RateLimitWindow window) =>
        new(window.UsedPercent, window.ResetsAt, window.WindowDurationMins);
}
