using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Tests.Testing;

internal sealed class FakeNamespaceHasher : IAccountNamespaceHasher
{
    public string Hash { get; set; } = "namespace-hash";

    public ValueTask<string> GetNamespaceHashAsync(
        AccountIdentity identity,
        CancellationToken cancellationToken) =>
        new(Hash);
}
