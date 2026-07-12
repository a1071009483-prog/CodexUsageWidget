using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class FakeQuotaSource : IQuotaSource
{
    private readonly Queue<QuotaSourceResult> _results = new();

    public event EventHandler<QuotaSnapshot>? Updated;

    public int ReadCount { get; private set; }

    public Exception? ExceptionToThrow { get; set; }

    public void EnqueueResult(QuotaSourceResult result) => _results.Enqueue(result);

    public void EnqueueSuccess(RawRateLimitSnapshot snapshot) =>
        _results.Enqueue(new QuotaSourceResult(true, snapshot));

    public Task<QuotaSourceResult> ReadAsync(CancellationToken cancellationToken)
    {
        ReadCount++;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        if (_results.Count == 0)
        {
            return Task.FromResult(new QuotaSourceResult(false, null, "No queued result"));
        }

        return Task.FromResult(_results.Dequeue());
    }

    public void RaiseUpdated(QuotaSnapshot? snapshot = null) =>
        Updated?.Invoke(this, snapshot ?? new QuotaSnapshot(
            null,
            new QuotaBucketSnapshot(QuotaBucket.FiveHour, 0, 100, null, null, false),
            new QuotaBucketSnapshot(QuotaBucket.Weekly, 0, 100, null, null, false),
            DateTimeOffset.UtcNow,
            true,
            MonitoringConnectionState.Connected,
            null));
}
