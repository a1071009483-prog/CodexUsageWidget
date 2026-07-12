using System.Runtime.CompilerServices;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class FakeAuditStore : IAuditStore
{
    private readonly Dictionary<string, AuditEntry> _entries = new();

    public Exception? ExceptionToThrow { get; set; }

    public IReadOnlyDictionary<string, AuditEntry> Entries => _entries;

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        _entries[entry.AuditId] = entry;
        return Task.CompletedTask;
    }

    public Task<AuditEntry?> ReadAsync(string auditId, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.TryGetValue(auditId, out AuditEntry? entry) ? entry : null);

    public async IAsyncEnumerable<AuditEntry> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (AuditEntry entry in _entries.Values.OrderByDescending(e => e.RecordedAt))
        {
            await Task.Yield();
            yield return entry;
        }
    }
}
