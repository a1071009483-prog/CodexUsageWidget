using System.Runtime.CompilerServices;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App.Services;

/// <summary>
/// Design-time audit store that returns no rows. Used for the non-generating smoke mode.
/// </summary>
public sealed class DesignAuditStore : IAuditStore
{
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<AuditEntry?> ReadAsync(string auditId, CancellationToken cancellationToken) =>
        Task.FromResult<AuditEntry?>(null);

    public async IAsyncEnumerable<AuditEntry> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield break;
    }
}
