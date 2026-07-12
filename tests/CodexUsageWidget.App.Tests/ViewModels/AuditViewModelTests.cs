using CodexUsageWidget.App.Tests.Testing;
using CodexUsageWidget.App.ViewModels;
using CodexUsageWidget.Core.Abstractions;
using Xunit;

namespace CodexUsageWidget.App.Tests.ViewModels;

public sealed class AuditViewModelTests
{
    [Fact]
    public async Task LoadAsyncPresentsRedactedMetadata()
    {
        FakeAuditStore store = new();
        AuditEntry entry = new(
            "audit-1",
            "namespace-hash",
            "attempt-1",
            "o4-mini",
            "2026-01-01T10:00:00Z",
            new AuditQuotaSnapshot(0, 100, "2026-01-01T15:00:00Z"),
            new AuditQuotaSnapshot(0, 100, "2026-01-01T20:00:00Z"),
            true,
            "succeeded",
            null,
            "2026-01-01T10:01:00Z");
        await store.WriteAsync(entry, CancellationToken.None);

        AuditViewModel vm = new(store, new SynchronousDispatcher());
        await vm.LoadAsync();

        Assert.Single(vm.Rows);
        AuditRowViewModel row = vm.Rows[0];
        Assert.Equal("attempt-1", row.AttemptId);
        Assert.Equal("o4-mini", row.ModelId);
        Assert.Equal(0, row.PreUsedPercent);
        Assert.Equal(0, row.PostUsedPercent);
        Assert.Equal("succeeded", row.Outcome);
        Assert.Null(row.ErrorCategory);
    }

    [Fact]
    public async Task LoadAsyncExcludesNoRowsWhenStoreEmpty()
    {
        FakeAuditStore store = new();
        AuditViewModel vm = new(store, new SynchronousDispatcher());

        await vm.LoadAsync();

        Assert.Empty(vm.Rows);
    }
}
