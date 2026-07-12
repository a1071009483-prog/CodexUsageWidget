using CodexUsageWidget.Core.Abstractions;
using CodexUsageWidget.Core.Quota;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class FakeQuotaSource : IQuotaSource
{
    private readonly Queue<QuotaSourceResult> _results = new();

    public event EventHandler? Updated;

    public int ReadCount { get; private set; }

    public void EnqueueResult(QuotaSourceResult result) => _results.Enqueue(result);

    public void EnqueueSuccess(RawRateLimitSnapshot snapshot) =>
        _results.Enqueue(new QuotaSourceResult(true, snapshot));

    public Task<QuotaSourceResult> ReadAsync(CancellationToken cancellationToken)
    {
        ReadCount++;

        if (_results.Count == 0)
        {
            return Task.FromResult(new QuotaSourceResult(false, null, "No queued result"));
        }

        return Task.FromResult(_results.Dequeue());
    }

    public void RaiseUpdated() => Updated?.Invoke(this, EventArgs.Empty);
}
