using System.Runtime.CompilerServices;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Tests.Testing;

internal sealed class FakeAuditStore : IAuditStore
{
    private readonly List<AuditEntry> _entries = new();

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<AuditEntry?> ReadAsync(string auditId, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.FirstOrDefault(e => e.AuditId == auditId));

    public async IAsyncEnumerable<AuditEntry> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach (AuditEntry entry in _entries.OrderByDescending(e => e.RecordedAt))
        {
            yield return entry;
        }
    }
}
